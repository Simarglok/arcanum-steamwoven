using System.Collections.ObjectModel;
using Arcanum.Input;

namespace GamepadLab.Core;

public sealed record LabAction(string Id,string Label,string Category,string TargetKind,bool Supported=true);
public static class LabActionCatalog
{
    public static IReadOnlyList<LabAction> Entries { get; }=Array.AsReadOnly(new LabAction[] {
        new("synthetic.item.self","Синтетический эликсир (self)","Items","self"),
        new("synthetic.skill.enemy","Synthetic skill (enemy)","Skills","enemy"),
        new("synthetic.spell.ally","Синтетическое заклинание союзнику","Spells","ally"),
        new("synthetic.spell.ground","Synthetic ground spell","Spells","ground"),
        new("mock.sneak","Sneak — mock intent","Commands","none"),new("mock.rest","Rest — mock intent","Commands","none"),
        new("mock.turn-end","Turn end — mock intent","Commands","none"),new("mock.follower-follow","Follower follow — mock intent","Followers","none"),
        new("unsupported.crafting","Crafting — NOT IMPLEMENTED","Rare","none",false),new("unsupported.fate","Fate — NOT IMPLEMENTED","Rare","none",false),
        new("unsupported.graphics-mode","Graphics mode — NOT IMPLEMENTED","Rare","none",false) });
    public static LabAction? Get(string id)=>Entries.FirstOrDefault(a=>a.Id==id);
    public static bool IsAssignable(string id)=>Get(id) is not null;
}
public sealed record LabWorldTarget(string Id,string Kind,Vec2 Anchor,int CycleOrder,int Revision=1,bool Alive=true,bool Visible=true,bool Interactive=true);
public sealed record LabEvent(string Kind,string? ActionId=null,string? SlotId=null,string? TargetId=null,Vec2? GroundPoint=null,long EdgeId=0,int ResourceCost=0,Vec2? From=null,Vec2? To=null,int APCost=0);

/// <summary>Explicit synthetic execution only. Action authority never follows automatic presentation fallback.</summary>
public sealed class LabActionExecutor
{
    private readonly Queue<LabEvent> _journal=new();
    public IReadOnlyList<LabEvent> Journal=>Array.AsReadOnly(_journal.ToArray());
    public ActionTargetSnapshot? Pending { get; private set; }
    public string? SelectedTargetId { get; private set; }
    public int? SelectedRevision { get; private set; }
    public bool Locked { get; private set; }
    public long Epoch { get; private set; }
    public int ResourceUses { get; private set; }
    public long EventSequence { get; private set; }
    public Vec2 GridActor { get; private set; }=new(4,4);
    public Vec2? GridPreview { get; private set; }
    public int APSpent { get; private set; }
    public bool TurnBased { get; set; }
    private Vec2 _gridDirection;private double _gridHeldSeconds;private double _gridNextRepeat=.35;
    public void Record(LabEvent e){if(_journal.Count==128)_journal.Dequeue();_journal.Enqueue(e);EventSequence++;}
    public void Boundary(long epoch){Epoch=epoch;Pending=null;GridPreview=null;_gridDirection=default;_gridHeldSeconds=0;_gridNextRepeat=.35;}
    public void ObservePresentation(LabWorldTarget? target)
    {
        if(!Locked&&SelectedTargetId is null&&target is not null){SelectedTargetId=target.Id;SelectedRevision=target.Revision;}
    }
    public void Select(LabWorldTarget? target,bool locked=true){SelectedTargetId=target?.Id;SelectedRevision=target?.Revision;Locked=locked;}
    public void ToggleLock(){Locked=!Locked;if(!Locked){SelectedTargetId=null;SelectedRevision=null;}Record(new(Locked?"TargetLocked":"TargetUnlocked",TargetId:SelectedTargetId));}
    public IReadOnlyList<LabWorldTarget> Eligible(IEnumerable<LabWorldTarget> targets,string? kind=null)=>Array.AsReadOnly(targets.Where(t=>t.Alive&&t.Visible&&t.Interactive&&(kind is null||t.Kind==kind)).OrderBy(t=>t.CycleOrder).ThenBy(t=>t.Id,StringComparer.Ordinal).ToArray());
    public void Cycle(IEnumerable<LabWorldTarget> targets,int direction)
    {
        var list=Eligible(targets,Pending?.Kind is "enemy" or "ally"?Pending.Kind:null);if(list.Count==0){Record(new("TargetUnavailable"));return;}
        var current=SelectedTargetId is null?-1:list.ToList().FindIndex(t=>t.Id==SelectedTargetId);
        var index=(current+(direction>=0?1:-1)+list.Count)%list.Count;var target=list[index];Select(target);
        if(Pending is not null)Pending=Pending with{RequestedTargetId=target.Id,RequestedRevision=target.Revision};
        Record(new("TargetSelected",TargetId:target.Id));
    }
    public void UseSlot(string slot,LabSlots slots,long edge,IReadOnlyList<LabWorldTarget> targets,Vec2 ground)
    {
        if(!slots.Assignments.TryGetValue(slot,out var action)||action is null){Record(new("EmptySlot",SlotId:slot,EdgeId:edge));return;}
        SelectAction(action,slot,edge,targets,ground);
    }
    public void SelectAction(string id,string? slot,long edge,IReadOnlyList<LabWorldTarget> targets,Vec2 ground)
    {
        var action=LabActionCatalog.Get(id);if(action is null){Record(new("Unsupported",id,slot,EdgeId:edge));return;}
        if(!action.Supported){Record(new("Unsupported",id,slot,EdgeId:edge));return;}
        if(action.TargetKind=="none"){Record(new("MockIntent",id,slot,EdgeId:edge));return;}
        var snapshot=new ActionTargetSnapshot(id,slot,action.TargetKind=="self"?"actor":SelectedTargetId,action.TargetKind=="self"?1:SelectedRevision,action.TargetKind,
            action.TargetKind=="ground"&&Finite(ground)?ground:null,Epoch,edge);
        if(action.TargetKind=="self"){Activate(snapshot);return;}
        if(action.TargetKind=="ground"||snapshot.RequestedTargetId is null){Pending=snapshot;Record(new("PreviewOpened",id,slot,snapshot.RequestedTargetId,snapshot.GroundPoint,edge));return;}
        if(Valid(snapshot,targets))Activate(snapshot);else Record(new("TargetUnavailable",id,slot,snapshot.RequestedTargetId,EdgeId:edge));
    }
    public void Confirm(long edge,IReadOnlyList<LabWorldTarget> targets)
    {
        if(GridPreview is Vec2 destination){var from=GridActor;GridActor=destination;GridPreview=null;APSpent++;Record(new("MockMoveAP",EdgeId:edge,From:from,To:destination,APCost:1));return;}
        if(Pending is not ActionTargetSnapshot snapshot)return;
        if(edge<=snapshot.SelectingEdgeId)return; // Selecting press can never confirm itself.
        Pending=null;
        if(snapshot.ContextEpoch!=Epoch||!Valid(snapshot,targets)){Record(new("TargetUnavailable",snapshot.ActionId,snapshot.SlotId,snapshot.RequestedTargetId,EdgeId:edge));return;}
        Activate(snapshot);
    }
    public void Cancel(long edge){if(Pending is not null||GridPreview is not null)Record(new("PreviewCancelled",Pending?.ActionId,Pending?.SlotId,EdgeId:edge));Pending=null;GridPreview=null;_gridDirection=default;}
    public void MoveGround(Vec2 delta)
    {
        if(Pending?.Kind=="ground"&&Pending.GroundPoint is Vec2 point&&Finite(delta))Pending=Pending with{GroundPoint=new Vec2(Math.Clamp(point.X+delta.X,-200,1800),Math.Clamp(point.Y+delta.Y,-200,1800))};
    }
    public void PreviewGrid(Vec2 stick,double delta)
    {
        if(!TurnBased||!Finite(stick)||!double.IsFinite(delta)||delta<0)return;
        var direction=stick.Length<.65?default:Math.Abs(stick.X)>=Math.Abs(stick.Y)?new Vec2(Math.Sign(stick.X),0):new Vec2(0,Math.Sign(stick.Y));
        if(direction==default(Vec2)){_gridDirection=default;_gridHeldSeconds=0;_gridNextRepeat=.35;return;}
        bool move=direction!=_gridDirection;
        if(move){_gridDirection=direction;_gridHeldSeconds=0;_gridNextRepeat=.35;}else{_gridHeldSeconds+=Math.Min(delta,.5);if(_gridHeldSeconds>=_gridNextRepeat){move=true;_gridNextRepeat+=.10;}}
        if(!move)return;var from=GridPreview??GridActor;
        GridPreview=new(Math.Clamp(from.X+direction.X,0,9),Math.Clamp(from.Y+direction.Y,0,9));
        Record(new("GridPreviewOpened",From:GridActor,To:GridPreview));
    }
    private bool Valid(ActionTargetSnapshot s,IReadOnlyList<LabWorldTarget> targets)=>s.ContextEpoch==Epoch&&(s.Kind=="ground"?s.GroundPoint is Vec2 g&&Finite(g):s.Kind=="self"||targets.Any(t=>t.Id==s.RequestedTargetId&&t.Revision==s.RequestedRevision&&t.Kind==s.Kind&&t.Alive&&t.Visible&&t.Interactive));
    private void Activate(ActionTargetSnapshot s){ResourceUses++;Record(new("MockActivated",s.ActionId,s.SlotId,s.RequestedTargetId,s.GroundPoint,s.SelectingEdgeId,1));}
    private static bool Finite(Vec2 v)=>double.IsFinite(v.X)&&double.IsFinite(v.Y);
}
