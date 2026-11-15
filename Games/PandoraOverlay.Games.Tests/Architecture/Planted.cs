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
/// looked at, opened or walked, no timer made, no native code run, no member
/// read by its name, no picture made, no window of the desktop walked, no
/// process counted, no directory asked and nothing printed. The names the
/// reflection calls look up and the paths the A69 to A74 calls open
/// ("planted") name nothing that exists.
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

    // ---- A68: an object from an assembly and a type given as text ------------------------
    // Both answer an ObjectHandle, whose type the scan refuses (Activator itself stays, A43).

    /// <summary>An object of a type named in text, its assembly loaded by name (Activator.CreateInstance(assemblyName, typeName)).</summary>
    internal static object? AnObjectByName() => Activator.CreateInstance("planted", "planted");

    /// <summary>An object of a type named in text, its assembly loaded from a file (Activator.CreateInstanceFrom).</summary>
    internal static object? AnObjectFromAFile() => Activator.CreateInstanceFrom("planted", "planted");

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

    // ---- A69: a file or a folder by its path -------------------------------------------
    // Each opens what its path names with no File, Directory, FileStream or Path in the
    // metadata. Real calls again, so the scan is shown the owner the compiler writes;
    // each compiles with the framework a game builds against, and no package.

    /// <summary>A file read by its path: StreamReader's path constructor, the same name (.ctor) as its stream one (the type StreamReader).</summary>
    internal static StreamReader AReaderOnAPath() => new("planted");

    /// <summary>A file written by its path, the debugging session's new StreamWriter("debug.txt") (the type StreamWriter).</summary>
    internal static StreamWriter AWriterOnAPath() => new("planted");

    /// <summary>A trace listener writing the file its path names (the type TextWriterTraceListener).</summary>
    internal static System.Diagnostics.TraceListener ATextLog() => new System.Diagnostics.TextWriterTraceListener("planted");

    /// <summary>The same, one line a record (the type DelimitedListTraceListener).</summary>
    internal static System.Diagnostics.TraceListener ADelimitedLog() => new System.Diagnostics.DelimitedListTraceListener("planted");

    /// <summary>The same, as XML (the type XmlWriterTraceListener).</summary>
    internal static System.Diagnostics.TraceListener AnXmlLog() => new System.Diagnostics.XmlWriterTraceListener("planted");

    /// <summary>The default listener, which Debug and Trace write to, given a file (DefaultTraceListener::set_LogFileName; the type stays).</summary>
    internal static System.Diagnostics.DefaultTraceListener ADefaultLogFile() => new() { LogFileName = "planted" };

    /// <summary>A .resources file read by its path (the type ResourceReader).</summary>
    internal static System.Resources.ResourceReader AResourceFileRead() => new("planted");

    /// <summary>A .resources file written by its path (the type ResourceWriter).</summary>
    internal static System.Resources.ResourceWriter AResourceFileWritten() => new("planted");

    /// <summary>A resource manager on a folder's .resources files (ResourceManager::CreateFileBasedResourceManager; the type stays).</summary>
    internal static System.Resources.ResourceManager AResourceFolder() =>
        System.Resources.ResourceManager.CreateFileBasedResourceManager("planted", "planted", null);

    /// <summary>An exe's config file, read and written (the type ConfigurationManager).</summary>
    internal static object TheExesConfig() => System.Configuration.ConfigurationManager.OpenExeConfiguration("planted");

    /// <summary>An XML file read by its path (the namespace System.Xml).</summary>
    internal static object AnXmlFile() => System.Xml.XmlReader.Create("planted");

    /// <summary>The same through LINQ to XML (System.Xml.Linq, the namespace below).</summary>
    internal static object AnXmlDocument() => System.Xml.Linq.XDocument.Load("planted");

    /// <summary>A data set's XML read by its path (the namespace System.Data).</summary>
    internal static object ADataSetsXml() => new System.Data.DataSet().ReadXml("planted");

    /// <summary>A package opened by its path (the namespace System.IO.Packaging).</summary>
    internal static object APackage() => System.IO.Packaging.Package.Open("planted");

    /// <summary>A file mapped into memory by its path (the namespace System.IO.MemoryMappedFiles).</summary>
    internal static object AMappedFile() => System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile("planted");

    /// <summary>A folder walked by its path (the namespace System.IO.Enumeration).</summary>
    internal static object AFoldersWalk() =>
        new System.IO.Enumeration.FileSystemEnumerable<string>("planted", (ref System.IO.Enumeration.FileSystemEntry e) => e.FileName.ToString());

    /// <summary>A folder packed into a tar file, both by their paths (the namespace System.Formats.Tar).</summary>
    internal static void AnArchive() => System.Formats.Tar.TarFile.CreateFromDirectory("planted", "planted", false);

    /// <summary>XAML read from a file at run time (the namespace System.Xaml).</summary>
    internal static object AXamlFile() => System.Xaml.XamlServices.Load("planted");

    /// <summary>An XPS document opened by its path (System.Windows.Xps.Packaging, below System.Windows.Xps).</summary>
    internal static object AnXpsDocument() => new System.Windows.Xps.Packaging.XpsDocument("planted", FileAccess.Read);

    /// <summary>A file read through VisualBasic's FileSystem (Microsoft.VisualBasic.FileIO, below Microsoft.VisualBasic).</summary>
    internal static string AVisualBasicFile() => Microsoft.VisualBasic.FileIO.FileSystem.ReadAllText("planted");

    /// <summary>The PC's event log, by a log's name (the type EventLog: in the WPF framework, no package).</summary>
    internal static object AnEventLog() => new System.Diagnostics.EventLog("planted");

    // A70: the two assemblies behind ConfigurationManager and EventLog, whole. Neither
    // plant names either refused type: the assembly reference is what the scan finds.

    /// <summary>A settings class, as Visual Studio's Settings designer writes one (the assembly System.Configuration.ConfigurationManager, by its base type).</summary>
    internal sealed class PlantedSettings : System.Configuration.ApplicationSettingsBase { }

    /// <summary>Its Save, which writes user.config under the user's AppData with no ConfigurationManager named (SettingsBase::Save).</summary>
    internal static void ASettingsSave() => new PlantedSettings().Save();

    /// <summary>A log file read by its path (the assembly System.Diagnostics.EventLog, by EventLogReader, with no EventLog named).</summary>
    internal static object AnEventLogFile() =>
        new System.Diagnostics.Eventing.Reader.EventLogReader("planted", System.Diagnostics.Eventing.Reader.PathType.FilePath);

    // ---- A71, A73, A74: the last doors ------------------------------------------------
    // Real calls again, each compiled with the framework a game builds against and no
    // package. Never called: the UI Automation, counter and directory ones would walk
    // the desktop's windows, count the PC's processes and ask the network.

    /// <summary>A cursor read from a file by its path (Cursor::.ctor; Cursor as a type stays, A71).</summary>
    internal static System.Windows.Input.Cursor ACursorFile() => new("planted");

    /// <summary>The converter that turns a ".cur" name into a cursor read from that file (the type CursorConverter, A71).</summary>
    internal static object ACursorConverter() => new System.Windows.Input.CursorConverter();

    /// <summary>A .resources file read by its path through a resource set (ResourceSet::.ctor; the type stays, A73).</summary>
    internal static System.Resources.ResourceSet AResourceSetFile() => new("planted");

    /// <summary>A preserialized .resources file read by its path (the namespace System.Resources.Extensions, A73).</summary>
    internal static object AnExtensionsReader() => new System.Resources.Extensions.DeserializingResourceReader("planted");

    /// <summary>The same, written by its path (the namespace System.Resources.Extensions, A73).</summary>
    internal static object AnExtensionsWriter() => new System.Resources.Extensions.PreserializedResourceWriter("planted");

    /// <summary>A file sent to a printer by its path (the namespace System.Printing, by PrintQueue, A73).</summary>
    internal static object APrintJob(System.Printing.PrintQueue queue) => queue.AddJob("planted", "planted", false);

    /// <summary>The print dialog, a window of its own (the type PrintDialog, A73).</summary>
    internal static object APrintDialog() => new System.Windows.Controls.PrintDialog();

    /// <summary>The six decoders other than the engine's, each opening what a Uri names in its constructor (each its type, A73).</summary>
    internal static object AJpegByUri() => new JpegBitmapDecoder(new Uri("planted", UriKind.Relative), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

    /// <inheritdoc cref="AJpegByUri"/>
    internal static object AGifByUri() => new GifBitmapDecoder(new Uri("planted", UriKind.Relative), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

    /// <inheritdoc cref="AJpegByUri"/>
    internal static object ABmpByUri() => new BmpBitmapDecoder(new Uri("planted", UriKind.Relative), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

    /// <inheritdoc cref="AJpegByUri"/>
    internal static object ATiffByUri() => new TiffBitmapDecoder(new Uri("planted", UriKind.Relative), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

    /// <inheritdoc cref="AJpegByUri"/>
    internal static object AWmpByUri() => new WmpBitmapDecoder(new Uri("planted", UriKind.Relative), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

    /// <inheritdoc cref="AJpegByUri"/>
    internal static object AnIconByUri() => new IconBitmapDecoder(new Uri("planted", UriKind.Relative), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

    /// <summary>Every top-level window on the desktop, The Isle's included (the assembly UIAutomationClient, A74).</summary>
    internal static object TheDesktopsWindows() => System.Windows.Automation.AutomationElement.RootElement;

    /// <summary>Every running process by its name (the assembly System.Diagnostics.PerformanceCounter, A74).</summary>
    internal static string[] TheProcessCounters() => new System.Diagnostics.PerformanceCounterCategory("planted").GetInstanceNames();

    /// <summary>A query of the domain's directory over LDAP (the assembly System.DirectoryServices, A74).</summary>
    internal static object ADirectorySearch() => new System.DirectoryServices.DirectorySearcher().FindAll();

    /// <summary>A public method called by its name (the type ObjectDataProvider, A74).</summary>
    internal static object AProvidersCallByName() => new System.Windows.Data.ObjectDataProvider { MethodName = "planted" };

    /// <summary>An XML document read or fetched from what a Uri names (the type XmlDataProvider, A74).</summary>
    internal static object AnXmlProvidersSource() => new System.Windows.Data.XmlDataProvider { Source = new Uri("planted", UriKind.Relative) };
}
