using System.Globalization;

namespace Shabpa.Core;

/// <summary>One colour theme. Colours are "RRGGBB"; the UI turns them into brushes at startup.</summary>
public sealed record Palette(string Key, string Fa, string En, bool Light, IReadOnlyDictionary<string, string> Colors)
{
    public string Name => L.T(Fa, En);

    public const string Default = "night";

    /// <summary>Every colour a theme must define.</summary>
    public static readonly string[] Keys =
    [
        "Bg", "Side", "Panel", "Panel2", "Panel3", "Line", "Text", "Muted", "Faint",
        "Accent", "Accent1", "Accent2", "AccentSoft", "OnAccent",
        "Gold", "GoldSoft", "Green", "GreenSoft", "Red", "RedSoft", "RedLine",
        "Hero", "Thumb", "Glow",
    ];

    private static Palette Make(string key, string fa, string en, bool light, string spec)
    {
        var colors = spec.Split([' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('='))
            .ToDictionary(p => p[0], p => p[1]);
        return new Palette(key, fa, en, light, colors);
    }

    public static readonly IReadOnlyList<Palette> All =
    [
        Make("night", "آبی شب", "Night blue", false, """
            Bg=0A0D18 Side=0D111F Panel=111627 Panel2=171D33 Panel3=1E2540 Line=232B47
            Text=EEF1FA Muted=8E97B6 Faint=6A7396
            Accent=8087FF Accent1=7B6BF5 Accent2=5C6CFF AccentSoft=1B2052 OnAccent=FFFFFF
            Gold=FFC764 GoldSoft=3A2F17 Green=3DDC97 GreenSoft=10352B Red=FF6B7F RedSoft=3D1722 RedLine=6B2534
            Hero=1B1D4A Thumb=2C3559 Glow=FFFFFF
            """),
        Make("black", "مشکی", "Black", false, """
            Bg=000000 Side=060607 Panel=0E0E10 Panel2=17171A Panel3=222226 Line=2B2B31
            Text=F2F2F4 Muted=9D9DA7 Faint=6E6E78
            Accent=5AA9FF Accent1=74B7FF Accent2=4A98F2 AccentSoft=0F2A47 OnAccent=03101F
            Gold=FFC764 GoldSoft=33290F Green=3DDC97 GreenSoft=0D2E24 Red=FF6B7F RedSoft=3A141D RedLine=66222F
            Hero=0F2238 Thumb=3A3A42 Glow=FFFFFF
            """),
        Make("white", "سفید", "White", true, """
            Bg=F3F5FA Side=FFFFFF Panel=FFFFFF Panel2=EDF0F7 Panel3=DCE2EF Line=D3DAE9
            Text=141A2E Muted=4F5A78 Faint=737D99
            Accent=4A51E0 Accent1=6A63F2 Accent2=4250E0 AccentSoft=E1E3FF OnAccent=FFFFFF
            Gold=865600 GoldSoft=FFEFC7 Green=0A7349 GreenSoft=D8F4E7 Red=BE2340 RedSoft=FFDFE5 RedLine=F0A8B4
            Hero=E3E6FF Thumb=B6BFD8 Glow=000000
            """),
        Make("pink", "صورتی", "Pink", true, """
            Bg=FFF0F6 Side=FFE4EE Panel=FFFFFF Panel2=FFE8F1 Panel3=FAD0E0 Line=F2C2D4
            Text=3A0E24 Muted=75405A Faint=96667E
            Accent=B8156A Accent1=DB3A8A Accent2=B8156A AccentSoft=FFE1EF OnAccent=FFFFFF
            Gold=865600 GoldSoft=FFEFC7 Green=0A7349 GreenSoft=D8F4E7 Red=B51B38 RedSoft=FFDCE2 RedLine=EFA3B1
            Hero=FFD8E8 Thumb=E6A5C0 Glow=000000
            """),
        Make("purple", "بنفش", "Purple", false, """
            Bg=120A1F Side=160D26 Panel=1B1130 Panel2=24183F Panel3=2F2052 Line=3A2A62
            Text=F3EDFF Muted=AC9DD0 Faint=8171A8
            Accent=B892FF Accent1=BE9BFF Accent2=9A72FA AccentSoft=37226B OnAccent=160828
            Gold=FFC764 GoldSoft=3D3018 Green=4BE3A4 GreenSoft=123A30 Red=FF7A8D RedSoft=45182A RedLine=73283A
            Hero=3A1F73 Thumb=46347A Glow=FFFFFF
            """),
        Make("emerald", "سبز", "Emerald", false, """
            Bg=07140F Side=0A1A14 Panel=0E211A Panel2=142D24 Panel3=1B3B30 Line=23483B
            Text=EAF7F1 Muted=8FB6A6 Faint=67907F
            Accent=34D399 Accent1=4ADEA8 Accent2=1FC48C AccentSoft=0F3D2E OnAccent=03261A
            Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=124233 Red=FF7A8D RedSoft=45182A RedLine=73283A
            Hero=0F3D2E Thumb=2B5A4A Glow=FFFFFF
            """),
    ];

    public static Palette Find(string? key) => All.FirstOrDefault(p => p.Key == key) ?? All[0];

    public (byte r, byte g, byte b) Rgb(string name)
    {
        var hex = Colors[name];
        return (byte.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber), byte.Parse(hex.AsSpan(2, 2), NumberStyles.HexNumber),
            byte.Parse(hex.AsSpan(4, 2), NumberStyles.HexNumber));
    }

    /// <summary>WCAG contrast ratio between two colours of this theme (1 = identical, 21 = black on white).</summary>
    public double Contrast(string foreground, string background)
    {
        var a = Luminance(Rgb(foreground));
        var b = Luminance(Rgb(background));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance((byte r, byte g, byte b) c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.r) + 0.7152 * Channel(c.g) + 0.0722 * Channel(c.b);
    }
}
