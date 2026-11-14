using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// The forbidden list's proof (A29): one real use of each kind of thing it
/// names, here in the test assembly, which is not under the rule.
/// <see cref="ForbiddenApiTests.TheScanFindsWhatWasPlanted"/> scans this very
/// assembly and must report every one; with no game yet, it is what proves
/// that a scan which finds nothing in the Contract and the Engine is a scan
/// that would have found something.
/// <para>
/// Nothing here is ever called. The compiler writes the references down
/// whether a method runs or not, and that is all the scan reads; so no file is
/// looked at, no timer made, no native code run and no address used.
/// </para>
/// </summary>
internal static class Planted
{
    /// <summary>A type: storage is the host's.</summary>
    internal static bool AFile() => File.Exists("planted");

    /// <summary>A type: the dice are the engine's.</summary>
    internal static int ADie() => new Random(1).Next();

    /// <summary>A member: time is the host's.</summary>
    internal static DateTime TheWallClock() => DateTime.UtcNow;

    /// <summary>A native call (a P/Invoke): never one. A function with no arguments, so nothing to marshal.</summary>
    [DllImport("kernel32.dll")]
    internal static extern ulong GetTickCount64();

    /// <summary>A type: the keyboard is the window's.</summary>
    internal static ModifierKeys TheKeyboard() => Keyboard.Modifiers;

    /// <summary>The engine's one exception: outside the engine, a timer of one's own.</summary>
    internal static DispatcherTimer ATimer() => new();

    /// <summary>An assembly (System.Net.Primitives) and a namespace (System.Net): an address, only named.</summary>
    internal static System.Type TheNetwork() => typeof(System.Net.IPAddress);

    /// <summary>A namespace with nothing else forbidden in it: System.Timers.</summary>
    internal static System.Type ATimersType() => typeof(System.Timers.ElapsedEventArgs);

    /// <summary>A member of the application object, which is not LoadComponent.</summary>
    internal static Application? TheApplication() => Application.Current;
}
