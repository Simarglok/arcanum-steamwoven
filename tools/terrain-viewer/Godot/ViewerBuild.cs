using System.Reflection;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-DEV-047: the running build (<see cref="ViewerBuildInfo"/>) from this assembly's metadata, and the small line the
/// launcher and the main menu show in their bottom right corner.</summary>
internal static class ViewerBuild
{
    public static ViewerBuildInfo Current { get; } = ViewerBuildInfo.FromMetadata(typeof(ViewerBuild).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Select(attribute => new KeyValuePair<string, string?>(attribute.Key, attribute.Value)));

    /// <summary>A label for a full-window backdrop: anchored to its bottom right corner, growing to the left and up; ARC-DEV-050:
    /// with <see cref="ViewerDebugMode.BuildSuffix"/> while the debug mode is on.</summary>
    public static Label MakeLabel(bool debugMode = false)
    {
        var label = new Label { Name = "BuildInfo", Text = Current.Text + (debugMode ? ViewerDebugMode.BuildSuffix : ""), Modulate = new Color(.62f, .56f, .46f), MouseFilter = Control.MouseFilterEnum.Ignore };
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight, Control.LayoutPresetMode.Minsize, 8);
        label.GrowHorizontal = Control.GrowDirection.Begin; label.GrowVertical = Control.GrowDirection.Begin;
        return label;
    }
}
