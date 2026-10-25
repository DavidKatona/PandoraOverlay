using System.Windows.Media;

namespace PandoraOverlay;

/// <summary>
/// Colours that are the website's (or Discord's) MEANINGS, not our chrome:
/// data, never a theme's (themes PLAN.md section 11, "Never a theme's";
/// .claude/rules/themes.md), the Settings pages' counterpart of the maps'
/// MapBrushes. A theme switch leaves every one of them as it is: Discord's
/// blue on the sign-in button and behind the avatar, the diet colours on
/// Server rules, and on Dino storage a dino's sex, its mutation and
/// compensated badges, the colours its vitals have on the website's page
/// (the stats panel's own, so a stored dino reads like your live one) and
/// the stored spot's waypoint blue. Prime and Elder are the overlay's own
/// looks for those badges, so they are tokens (Theme.Storage). Frozen, read
/// from anywhere (the Discord button's style in Themes/Controls.xaml by
/// x:Static).
/// </summary>
public static class SiteBrushes
{
    // Discord's own button (the Account page's "Sign in with Discord").
    public static readonly SolidColorBrush Discord = Frozen(0x58, 0x65, 0xF2);
    public static readonly SolidColorBrush DiscordHover = Frozen(0x47, 0x52, 0xC4);
    public static readonly SolidColorBrush DiscordDisabled = Frozen(0x2B, 0x2F, 0x4A);
    public static readonly SolidColorBrush DiscordDisabledText = Frozen(0x7A, 0x80, 0xA8);
    public static readonly SolidColorBrush DiscordText = Frozen(0xFF, 0xFF, 0xFF);

    // Server rules: the website's diet colours.
    public static readonly SolidColorBrush Herbivore = Frozen(0x81, 0xC7, 0x84);
    public static readonly SolidColorBrush Carnivore = Frozen(0xEF, 0x53, 0x50);
    public static readonly SolidColorBrush Omnivore = Frozen(0xFF, 0xB7, 0x4D);

    // Dino storage: the website's meanings.
    public static readonly SolidColorBrush Male = Frozen(0x7F, 0xB3, 0xFF);
    public static readonly SolidColorBrush Female = Frozen(0xFF, 0x8F, 0xB8);
    public static readonly (Brush Fore, Brush Back) MutationBadge = (Frozen(0xC8, 0xA0, 0xFF), Frozen(0x2B, 0x23, 0x40));
    public static readonly (Brush Fore, Brush Back) CompensatedBadge = (Frozen(0x7F, 0xD6, 0xC9), Frozen(0x16, 0x35, 0x31));
    /// <summary>The stored spot on the island map: the waypoint blue.</summary>
    public static readonly SolidColorBrush StoredSpot = Frozen(0x4F, 0xC3, 0xF7);

    /// <summary>A stored dino's vitals, in the stats panel's bar colours (1.38's); blood has no bar there.</summary>
    public static readonly (string Label, Brush Fill)[] Vitals =
    {
        ("Health", Frozen(0x4C, 0xAF, 0x50)),
        ("Stamina", Frozen(0xFF, 0xD5, 0x4F)),
        ("Hunger", Frozen(0xFF, 0x98, 0x00)),
        ("Thirst", Frozen(0x4F, 0xC3, 0xF7)),
        ("Blood", Frozen(0xE0, 0x5A, 0x5A))
    };

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
