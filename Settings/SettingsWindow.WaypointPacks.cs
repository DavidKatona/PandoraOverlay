using System.IO;
using System.Windows;

namespace PandoraOverlay;

/// <summary>
/// SettingsWindow, waypoint packs part: Import and Export on the Waypoints
/// page — a pack file merged into the draft, or the draft written out as
/// one (WaypointPacks does the parsing and the merge). Same class as
/// SettingsWindow.xaml.cs, split for reading; see CLAUDE.md.
/// </summary>
public partial class SettingsWindow
{
    // ---- Packs: import / export ------------------------------------------------
    private const string PackFilter = "Waypoint packs (*.json)|*.json|All files (*.*)|*.*";

    /// <summary>Merges a pack file into the draft; the outcome goes to the status line. Nothing persists until Save.</summary>
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import waypoints", Filter = PackFilter };
        if (dialog.ShowDialog(this) != true) return;

        string json;
        try
        {
            json = File.ReadAllText(dialog.FileName);
        }
        catch
        {
            SetWaypointStatus("Couldn't read that file.", HintWarn);
            return;
        }

        var pack = WaypointPacks.Parse(json, System.IO.Path.GetFileNameWithoutExtension(dialog.FileName));
        if (pack is null)
        {
            SetWaypointStatus("That file isn't a waypoint pack.", HintWarn);
            return;
        }

        var result = WaypointPacks.Merge(_draft, pack);
        if (result.Added > 0) _waypointsDirty = true;
        BuildWaypointRows();

        var text = $"Imported {result.Added} from \"{pack.Name}\"";
        if (result.Duplicates > 0) text += $" · {result.Duplicates} already here";
        if (result.Overflow > 0) text += $" · {result.Overflow} didn't fit ({WaypointLibrary.Capacity} max)";
        if (result.Added > 0) text += ". They start hidden: Show all on the pack, or tick Show per waypoint. Save to keep them.";
        SetWaypointStatus(text, result.Added > 0 ? HintGood : HintNeutral);
    }

    /// <summary>Writes the draft as a pack named after the chosen file.</summary>
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export waypoints", Filter = PackFilter, FileName = "pandora-waypoints.json",
            DefaultExt = ".json", AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;

        var name = WaypointLibrary.SanitizeName(System.IO.Path.GetFileNameWithoutExtension(dialog.FileName));
        try
        {
            File.WriteAllText(dialog.FileName, WaypointPacks.Export(_draft, name));
            SetWaypointStatus($"Exported {_draft.Count} waypoints as \"{name}\". Send the file to a friend; they import it from this page.", HintGood);
        }
        catch
        {
            SetWaypointStatus("Couldn't write that file.", HintWarn);
        }
    }
}
