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
