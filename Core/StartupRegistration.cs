using Microsoft.Win32;

namespace PandoraOverlay;

/// <summary>
/// Run-at-Windows-startup toggle via HKCU\...\Run. The registry entry itself
/// is the state — deliberately no config field that could drift from reality.
/// Fail soft: registry errors read as "off" and writes are best-effort.
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PandoraOverlay";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The exe the Run entry starts (quotes stripped), or null when off or unreadable.</summary>
    public static string? RegisteredExePath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string s && s.Trim().Trim('"') is { Length: > 0 } path ? path : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// v1.30: an entry left by an older copy would start that older copy from
    /// its old folder. When the entry is on, point it at THIS exe — the one the
    /// user actually runs — so Windows starts the copy that updates itself.
    /// </summary>
    public static void RepointToThisExe()
    {
        if (RegisteredExePath() is not { } registered || Environment.ProcessPath is not { } me) return;
        if (!string.Equals(registered, me, StringComparison.OrdinalIgnoreCase)) SetEnabled(true);
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Best-effort; the checkbox simply won't stick.
        }
    }
}
