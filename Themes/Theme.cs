using System.IO;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// The themes (1.39): which exist, which is on, and putting one on screen.
/// <para>
/// <b>Which exist</b> (<see cref="All"/>): Default, compiled in
/// (<see cref="DefaultTheme"/>), always first; then every embedded
/// <c>Assets/Themes/*.json</c> laid over it (<see cref="ThemeFile"/>), by its
/// Order. A new theme is a new file there and nothing else: the Settings row,
/// the tests and the harness all iterate this list.
/// </para>
/// <para>
/// <b>Which is on</b> (<see cref="Current"/>): the tokens alone, with no id
/// or name, so no code outside Themes/ can ask WHICH theme is on (PLAN.md
/// section 0); it asks for a state's look by its meaning.
/// </para>
/// <para>
/// <b>On screen</b> (<see cref="Apply"/>): every token goes into ONE
/// ResourceDictionary under <c>"Theme." + its path</c>
/// (<c>Theme.Text.Title</c>, <c>Theme.Corner.Widget</c>), merged into the
/// application's resources in place of the previous theme's. Every XAML
/// property that reads a token by DynamicResource, in every open window,
/// popup and card, then follows by itself; code that painted from
/// <see cref="Current"/> repaints on <see cref="Changed"/> (MainWindow's
/// ApplyTheme, in the order its comment gives, from step 3). The dictionary
/// is the application's, not a window's, because the maps' popup menus
/// resolve through the application.
/// </para>
/// <para>
/// What each token becomes: a colour → a frozen SolidColorBrush (and its
/// Color under <c>.Color</c>); a radius → a CornerRadius; a weight → a
/// FontWeight; the font → a FontFamily; a number → a double; a flag → a
/// bool (and a Visibility under <c>.Visibility</c>, Visible when true); a
/// shape pick → its <see cref="ShapeKind"/>; a string → itself. Plus one
/// derived key, <c>Theme.SliderStyle</c>: the slider thumb's Stock pick is
/// WPF's own slider, which no drawing can be, so it is a Style choice made
/// here (REVIEW.md objection 6).
/// </para>
/// </summary>
public static class Theme
{
    /// <summary>The compiled theme's id: what config.json holds by default.</summary>
    public const string DefaultId = "default";

    /// <summary>The resource keys' prefix.</summary>
    public const string KeyPrefix = "Theme.";

    /// <summary>The derived key: the Style every slider takes (DynamicResource), from the thumb's pick.</summary>
    public const string SliderStyleKey = KeyPrefix + "SliderStyle";

    /// <summary>The key, in Controls.xaml, of the drawn slider's Style (step 5); a Stock thumb needs none.</summary>
    public const string ThemedSliderKey = "ThemedSlider";

    private const string ResourcePrefix = "Themes/";
    private const string ControlsAddress = "/PandoraOverlay;component/Themes/Controls.xaml";

    private static readonly Lazy<IReadOnlyList<ThemeEntry>> Entries = new(Load);

    private static ResourceDictionary? _dictionary; // the current theme's, merged into the app's resources
    private static bool _installed;

    /// <summary>Every theme: Default first, then the bundled files by their Order (then id).</summary>
    public static IReadOnlyList<ThemeEntry> All => Entries.Value;

    /// <summary>The theme on screen, as its tokens. Default until <see cref="Apply"/> says otherwise.</summary>
    public static ThemeTokens Current { get; private set; } = DefaultTheme.Tokens;

    /// <summary>Raised after <see cref="Apply"/> has swapped the dictionary: code-painted parts repaint from <see cref="Current"/>.</summary>
    public static event Action? Changed;

    #region Which theme

    /// <summary>
    /// The theme a config value names, ignoring case and spaces; null, blank
    /// or unknown (a hand-edited "neon", a theme a newer copy had) is Default.
    /// </summary>
    public static ThemeEntry Resolve(string? id) => Resolve(id, All);

    /// <summary>The same, among the given themes (the tests' sample files).</summary>
    internal static ThemeEntry Resolve(string? id, IReadOnlyList<ThemeEntry> among)
    {
        var wanted = id?.Trim();
        return among.FirstOrDefault(e => string.Equals(e.Id, wanted, StringComparison.OrdinalIgnoreCase)) ?? among[0];
    }

    /// <summary>
    /// Default, then every embedded theme file laid over it. A broken file
    /// throws (<see cref="ThemeFileException"/>): a build defect, which
    /// ThemeFileTests catch before any release; no run-time fallback.
    /// </summary>
    private static IReadOnlyList<ThemeEntry> Load()
    {
        var assembly = typeof(Theme).Assembly;
        var files = new List<ThemeEntry>();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
                !resource.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            var id = resource[ResourcePrefix.Length..^".json".Length].ToLowerInvariant();
            if (id == DefaultId) throw new ThemeFileException(id, "takes the compiled Default's id");
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            files.Add(ThemeFile.Parse(id, reader.ReadToEnd(), DefaultTheme.Tokens));
        }
        return Ordered(files);
    }

    /// <summary>Default first, then the files by Order, then id (so the list never depends on the build's resource order).</summary>
    internal static IReadOnlyList<ThemeEntry> Ordered(IEnumerable<ThemeEntry> files) =>
        new[] { new ThemeEntry(DefaultId, "Default", 0, DefaultTheme.Tokens) }
            .Concat(files.OrderBy(e => e.Order).ThenBy(e => e.Id, StringComparer.Ordinal))
            .ToList();

    #endregion

    #region On screen

    /// <summary>
    /// Puts a theme on screen: fills a fresh dictionary from its tokens,
    /// merges it into the application's resources in place of the previous
    /// one, sets <see cref="Current"/>, raises <see cref="Changed"/> and logs
    /// one fact. Writes nothing into config and places no window: a theme
    /// changes paint, never a size or a position (PLAN.md section 12). On the
    /// UI thread.
    /// </summary>
    public static void Apply(string? id)
    {
        var entry = Resolve(id);
        Install(entry.Tokens);
        Changed?.Invoke();
        AppLog.App.Log($"Theme: {entry.Id}");
    }

    /// <summary>
    /// For windows built without the App (the golden-render harness, and any
    /// harness after it): merges Controls.xaml and puts Default's dictionary
    /// in place, once. In the overlay App.xaml has merged Controls.xaml and
    /// MainWindow applies the player's theme, so this finds nothing to do but
    /// the first install. Called from OverlayWindowBase's constructor (and,
    /// from step 5, the plain windows' constructors), BEFORE
    /// InitializeComponent. With no Application at all, a plain one is made
    /// for the resources to live in (a harness's job; the overlay always has
    /// its App first).
    /// </summary>
    public static void EnsureLoaded()
    {
        if (_installed) return;
        var app = Host();
        if (!app.Resources.MergedDictionaries.Any(IsControls))
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(ControlsAddress, UriKind.Relative) });
        Install(Current);
    }

    private static bool IsControls(ResourceDictionary d) =>
        d.Source?.OriginalString.EndsWith("Themes/Controls.xaml", StringComparison.OrdinalIgnoreCase) == true;

    private static Application Host() =>
        Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

    private static void Install(ThemeTokens tokens)
    {
        var app = Host();
        var fresh = BuildDictionary(tokens, app);
        var merged = app.Resources.MergedDictionaries;
        var at = _dictionary is null ? -1 : merged.IndexOf(_dictionary);
        if (at >= 0) merged[at] = fresh;
        else merged.Add(fresh);
        _dictionary = fresh;
        Current = tokens;
        _installed = true;
    }

    /// <summary>Every token as the WPF object a window reads, under its key; and the slider's Style.</summary>
    internal static ResourceDictionary BuildDictionary(ThemeTokens tokens, Application? app = null)
    {
        var dictionary = new ResourceDictionary();
        foreach (var (key, value) in Leaves(tokens))
        {
            switch (value)
            {
                case ThemeColor c:
                    var colour = Color.FromArgb(c.A, c.R, c.G, c.B);
                    var brush = new SolidColorBrush(colour);
                    brush.Freeze();
                    dictionary[key] = brush;
                    dictionary[key + ".Color"] = colour;
                    break;
                case ThemeRadius r:
                    dictionary[key] = new CornerRadius(r.Value);
                    break;
                case ThemeWeight w:
                    dictionary[key] = FontWeight.FromOpenTypeWeight((int)w);
                    break;
                case ThemeFont f:
                    dictionary[key] = FontFamilyOf(f.Address);
                    break;
                case bool b:
                    dictionary[key] = b;
                    dictionary[key + ".Visibility"] = b ? Visibility.Visible : Visibility.Collapsed;
                    break;
                default: // double, string, ShapeKind
                    dictionary[key] = value;
                    break;
            }
        }
        dictionary[SliderStyleKey] = SliderStyle(tokens.Shapes.SliderThumb, app);
        return dictionary;
    }

    /// <summary>
    /// Stock: an empty Slider Style, which leaves WPF's own template exactly
    /// as an unstyled slider has it (today's sliders). A drawn thumb: the
    /// themed Style in Controls.xaml (step 5 adds it; no theme picks one
    /// before then, and ThemeFileTests' pick check keeps it so).
    /// </summary>
    private static Style SliderStyle(ShapeKind thumb, Application? app)
    {
        if (thumb == ShapeKind.Stock || app is null)
        {
            var stock = new Style(typeof(Slider));
            stock.Seal();
            return stock;
        }
        return (Style)app.FindResource(ThemedSliderKey);
    }

    #endregion

    #region Keys

    /// <summary>
    /// Every key a theme's dictionary holds, from the token record's shape
    /// alone (no theme needed): what ThemeFileTests check each
    /// <c>{DynamicResource Theme.…}</c> in the XAML against.
    /// </summary>
    internal static IReadOnlySet<string> Keys { get; } = BuildKeys();

    private static HashSet<string> BuildKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal) { SliderStyleKey };
        foreach (var (key, type) in LeafTypes(typeof(ThemeTokens), KeyPrefix.TrimEnd('.')))
        {
            keys.Add(key);
            if (type == typeof(ThemeColor)) keys.Add(key + ".Color");
            if (type == typeof(bool)) keys.Add(key + ".Visibility");
        }
        return keys;
    }

    /// <summary>Every token of a theme with its key, by walking the record's properties (groups are the nested records).</summary>
    internal static IEnumerable<(string Key, object Value)> Leaves(ThemeTokens tokens) => Leaves(tokens, KeyPrefix.TrimEnd('.'));

    private static IEnumerable<(string Key, object Value)> Leaves(object group, string path)
    {
        foreach (var property in TokenProperties(group.GetType()))
        {
            var key = $"{path}.{property.Name}";
            var value = property.GetValue(group)!;
            if (IsGroup(property.PropertyType))
                foreach (var leaf in Leaves(value, key)) yield return leaf;
            else
                yield return (key, value);
        }
    }

    private static IEnumerable<(string Key, Type Type)> LeafTypes(Type group, string path)
    {
        foreach (var property in TokenProperties(group))
        {
            var key = $"{path}.{property.Name}";
            if (IsGroup(property.PropertyType))
                foreach (var leaf in LeafTypes(property.PropertyType, key)) yield return leaf;
            else
                yield return (key, property.PropertyType);
        }
    }

    // A token or a group is a public instance property with a getter (methods and the record's EqualityContract are not).
    private static IEnumerable<PropertyInfo> TokenProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0);

    // The groups are ThemeTokens' nested records; every other type is a value.
    private static bool IsGroup(Type type) => type.DeclaringType == typeof(ThemeTokens) && type.IsClass;

    #endregion

    #region Fonts

    /// <summary>
    /// A theme's font as a WPF FontFamily. A bundled family is addressed by
    /// the referenced-assembly pack form,
    /// <c>pack://application:,,,/PandoraOverlay;component/</c> + <c>./Assets/Fonts/…/#Family</c>:
    /// the plain <c>pack://application:,,,/</c> resolves against the ENTRY
    /// assembly, so in the golden-render harness (its own exe) the font would
    /// silently fall back to another and be baselined so (REVIEW.md
    /// objection 7). WPF throws nothing for a family it can't find, which is
    /// why MontserratTests ask the typeface whose glyphs it found. An
    /// installed family is just its name ("Segoe UI").
    /// <para>
    /// Where no Application exists yet (the tests), the address resolves only
    /// once Application's static constructor has run: it registers the
    /// application's resource package that pack:// font requests are served
    /// from. Without it the family silently falls back (seen in step 1's
    /// proof, Oct 24 2026). Touching PackUriHelper.UriSchemePack alone was not
    /// enough, and setting Application.ResourceAssembly, the other idea,
    /// throws wherever an entry assembly exists (every test host, every
    /// harness). In the overlay and the harnesses an Application already
    /// exists, so this is a no-op there.
    /// </para>
    /// </summary>
    public static FontFamily FontFamilyOf(string address)
    {
        var font = new ThemeFont(address);
        if (!font.Bundled) return new FontFamily(address);
        RuntimeHelpers.RunClassConstructor(typeof(Application).TypeHandle);
        return new FontFamily(new Uri("pack://application:,,,/PandoraOverlay;component/"), "./" + address.TrimStart('.', '/'));
    }

    #endregion
}
