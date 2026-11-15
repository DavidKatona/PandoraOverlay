using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
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
/// looked at, no timer made, no native code run, no member read by its name
/// and no picture made. The names the reflection calls look up ("planted")
/// name nothing that exists.
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

    /// <summary>An iterator: the compiler writes Environment.CurrentManagedThreadId into it, the one member of Environment allowed (A44).</summary>
    internal static IEnumerable<int> AnIterator()
    {
        yield return 1;
    }

    // ---- A60: reflection by name ---------------------------------------------------
    // Real calls, so the scan is shown the owner the compiler writes, not the owner the
    // list guessed: a call is written against the type that DECLARES the member.

    /// <summary>A field found by its name (Type::GetField) and read (FieldInfo::GetValue).</summary>
    internal static object? AFieldByName() => typeof(Planted).GetField("planted", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);

    /// <summary>A method found by its name (Type::GetMethod) and called: MethodInfo inherits Invoke, so the compiler writes MethodBase::Invoke.</summary>
    internal static object? AMethodByName() => typeof(Planted).GetMethod("planted")!.Invoke(null, null);

    /// <summary>A property found by its name (Type::GetProperty) and read (PropertyInfo::GetValue).</summary>
    internal static object? APropertyByName() => typeof(Planted).GetProperty("planted")!.GetValue(null);

    /// <summary>A delegate made of a method named in a string (Delegate::CreateDelegate).</summary>
    internal static Delegate ADelegateByName() => Delegate.CreateDelegate(typeof(Func<int>), typeof(Planted), "planted");

    /// <summary>A delegate made of a method found by its name, on the MethodInfo itself (MethodInfo::CreateDelegate, declared there).</summary>
    internal static Delegate AMethodsDelegate() => typeof(Planted).GetMethod("planted")!.CreateDelegate(typeof(Func<int>));

    /// <summary>An expression tree reading a field by its name (System.Linq.Expressions).</summary>
    internal static System.Linq.Expressions.Expression AnExpression() =>
        System.Linq.Expressions.Expression.Field(null, typeof(Planted), "planted");

    /// <summary>A dynamic member call: the compiler names the run-time binder's assembly (Microsoft.CSharp), and no member of the target.</summary>
    internal static object? ADynamicCall(dynamic target) => target.Planted();

    /// <summary>A type found by its name (Type::GetType, the static lookup).</summary>
    internal static System.Type? ATypeByName() => System.Type.GetType("planted");

    /// <summary>The declared members of a type, all of them (TypeInfo::get_DeclaredFields).</summary>
    internal static IEnumerable<FieldInfo> TheDeclaredFields() => typeof(Planted).GetTypeInfo().DeclaredFields;

    /// <summary>The extension methods that hand out members (RuntimeReflectionExtensions).</summary>
    internal static IEnumerable<FieldInfo> TheRuntimeFields() => typeof(Planted).GetRuntimeFields();

    /// <summary>The app's own assembly, the entry one (Assembly::GetEntryAssembly).</summary>
    internal static Assembly? TheEntryAssembly() => Assembly.GetEntryAssembly();

    // ---- A60: a picture by address -----------------------------------------------------

    /// <summary>A picture WPF would fetch by itself, given a UriSource (the type BitmapImage).</summary>
    internal static BitmapImage APicture() => new();

    /// <summary>The converter that turns an address string into a picture, as XAML and bindings do for "Source" (the type ImageSourceConverter).</summary>
    internal static object AConverter() => new System.Windows.Media.ImageSourceConverter();

    /// <summary>An address built with no "://" written (the type UriBuilder).</summary>
    internal static UriBuilder AnAddressBuilt() => new();

    /// <summary>"https://" made of the scheme names (Uri::UriSchemeHttps, Uri::SchemeDelimiter).</summary>
    internal static string AScheme() => Uri.UriSchemeHttps + Uri.SchemeDelimiter;

    /// <summary>A frame made by BitmapFrame.Create (every overload refused, the stream one included).</summary>
    internal static BitmapFrame AFrame(Stream stream) => BitmapFrame.Create(stream);

    /// <summary>A decoder made by BitmapDecoder.Create (the engine uses PngBitmapDecoder's constructor instead).</summary>
    internal static BitmapDecoder ADecoder(Stream stream) => BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
}
