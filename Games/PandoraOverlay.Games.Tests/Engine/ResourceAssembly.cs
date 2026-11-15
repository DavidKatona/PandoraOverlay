using System.IO;
using System.Reflection;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// An assembly that serves manifest resources from memory under a name the test
/// gives it, as a game's serves its embedded files: the engine's readers
/// (<c>Atlas</c>, <c>SoundBank</c>) ask nothing else of it. It counts every time
/// it is asked for its files (<see cref="Reads"/>), so a test can hold that an
/// assembly the engine refuses is never read (A94).
/// </summary>
internal sealed class ResourceAssembly : Assembly
{
    private readonly string _name;
    private readonly Dictionary<string, byte[]> _resources;
    private int _reads;

    public ResourceAssembly(string name, Dictionary<string, byte[]> resources) => (_name, _resources) = (name, resources);

    /// <summary>How many times its names or a file were asked for.</summary>
    public int Reads => _reads;

    public override string[] GetManifestResourceNames()
    {
        _reads++;
        return _resources.Keys.ToArray();
    }

    public override Stream? GetManifestResourceStream(string name)
    {
        _reads++;
        return _resources.TryGetValue(name, out var bytes) ? new MemoryStream(bytes, writable: false) : null;
    }

    public override AssemblyName GetName(bool copiedName) => new(_name);
}
