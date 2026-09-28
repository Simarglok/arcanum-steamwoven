namespace Arcanum.Scripting.Dialog;

public readonly record struct LocalFlagQuery(
    DialogId Dialog,
    DialogParticipantId Npc,
    int Index);

public abstract record DialogCommand;

public sealed record LocalFlagWriteCommand(
    DialogId Dialog,
    DialogParticipantId Npc,
    int Index,
    int Value) : DialogCommand;

public sealed record QuestStateCommand(
    DialogParticipantId PlayerCharacter,
    int QuestId,
    int State,
    DialogParticipantId NpcContext) : DialogCommand;

public sealed record RumorLogCommand(
    DialogParticipantId PlayerCharacter,
    int RumorId) : DialogCommand;

public enum DialogCommandResult
{
    Applied,
    Rejected,
}

public interface IDialogStateQueryPort
{
    LocalFlagReadResult Read(LocalFlagQuery query);
}

public interface IDialogCommandPort
{
    DialogCommandResult Execute(LocalFlagWriteCommand command);
    DialogCommandResult Execute(QuestStateCommand command);
    DialogCommandResult Execute(RumorLogCommand command);
}
