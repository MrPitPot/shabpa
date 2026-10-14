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
        "Bg", "Bg2", "Side", "Side2", "Panel", "Panel2", "Panel3", "Line", "Text", "Muted", "Faint",
        "Accent", "Accent1", "Accent2", "AccentSoft", "OnAccent",
        "Gold", "GoldSoft", "Green", "GreenSoft", "Red", "RedSoft", "RedLine",
        "Hero", "Hero2", "Thumb", "Glow",
    ];

    private static Palette Make(string key, string fa, string en, bool light, string spec)
    {
        var colors = spec.Split([' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('='))
            .ToDictionary(p => p[0], p => p[1]);
        // The hero card fades from Hero to Hero2; themes that stay single-hued repeat Hero.
        if (!colors.ContainsKey("Hero2")) colors["Hero2"] = colors["Hero"];
        // Background and sidebar fade from the first colour to the second; single-colour themes repeat it.
        if (!colors.ContainsKey("Bg2")) colors["Bg2"] = colors["Bg"];
        if (!colors.ContainsKey("Side2")) colors["Side2"] = colors["Side"];
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
            Bg=04130D Bg2=08261B Side=071B13 Side2=0B3324 Panel=0B251B Panel2=103226 Panel3=173F31 Line=21573F
            Text=EDFFF7 Muted=A3D9C2 Faint=74B396
            Accent=3EE6A0 Accent1=2EDC9A Accent2=14B8D2 AccentSoft=0E4A37 OnAccent=02170F
            Gold=FFC764 GoldSoft=3A2F17 Green=7DF0C2 GreenSoft=124A38 Red=FF7A8D RedSoft=45182A RedLine=73283A
            Hero=0D5240 Hero2=0F4C62 Thumb=2F6B54 Glow=FFFFFF
            """),
        Make("bluviolet", "بنفش‌آبی", "Blue violet", false, """
            Bg=0C0D2B Bg2=151447 Side=10123A Side2=1A1760 Panel=1A1D52 Panel2=23276A Panel3=2E3485 Line=3F46A3
            Text=F2F3FF Muted=C0C6FF Faint=9AA3F0
            Accent=AAB6FF Accent1=5865F2 Accent2=8B5CF6 AccentSoft=343C9C OnAccent=FFFFFF
            Gold=FFC764 GoldSoft=40331B Green=5BF0A8 GreenSoft=16473A Red=FF7A8D RedSoft=4F1F3A RedLine=80304A
            Hero=2F39B5 Hero2=4E30B0 Thumb=5560C8 Glow=FFFFFF
            """),
        Make("pinkviolet", "صورتی‌بنفش", "Pink violet", false, """
            Bg=1B0A2E Bg2=2E0F3F Side=230D3C Side2=3A1050 Panel=30124F Panel2=3D1A63 Panel3=4C2379 Line=63308F
            Text=FFF1FB Muted=E6C4F2 Faint=C79CE0
            Accent=FF9AD8 Accent1=E03C9B Accent2=8A4DFF AccentSoft=66206F OnAccent=FFFFFF
            Gold=FFC764 GoldSoft=45331B Green=5AF0A8 GreenSoft=16473A Red=FF8A9A RedSoft=51203A RedLine=82304A
            Hero=8E1F66 Hero2=5428B0 Thumb=7B4AA8 Glow=FFFFFF
            """),
        Make("sunset", "غروب", "Sunset", false, """
            Bg=190C10 Bg2=33121C Side=211014 Side2=3D1722 Panel=2B151B Panel2=371B23 Panel3=47242E Line=5C2F3B
            Text=FFF1EC Muted=EBC4BA Faint=C79A8F
            Accent=FFA06B Accent1=F2693A Accent2=D6336C AccentSoft=5C2A22 OnAccent=1A0A06
            Gold=FFD27A GoldSoft=45331A Green=5FEDAA GreenSoft=15412F Red=FF8A9A RedSoft=4D1D27 RedLine=7F3340
            Hero=7A2B2B Hero2=8A2A55 Thumb=6B4350 Glow=FFFFFF
            """),
        Make("ocean", "اقیانوس", "Ocean", false, """
            Bg=06121C Bg2=0B2A44 Side=0A1A28 Side2=0E3556 Panel=0E2335 Panel2=143048 Panel3=1B3F5C Line=244F72
            Text=EAF6FF Muted=A0C6E3 Faint=73A0C4
            Accent=5CD6FF Accent1=2BB8F5 Accent2=3D6BFF AccentSoft=0F3F66 OnAccent=02101C
            Gold=FFC764 GoldSoft=3A2F17 Green=4EE8B0 GreenSoft=0F3F33 Red=FF7A8D RedSoft=45182A RedLine=73283A
            Hero=0F4778 Hero2=2A3F9E Thumb=36628A Glow=FFFFFF
            """),
        Make("aurora", "شفق", "Aurora", false, """
            Bg=0A0F1A Bg2=161F4A Side=0E1524 Side2=1C2A5C Panel=131C30 Panel2=1A2540 Panel3=233157 Line=2D3E6B
            Text=EEF3FF Muted=A9B8DC Faint=7F90BB
            Accent=7DF5C6 Accent1=34E0A1 Accent2=8B5CF6 AccentSoft=15405A OnAccent=03140E
            Gold=FFC764 GoldSoft=3A2F17 Green=7DF5C6 GreenSoft=124A3C Red=FF7A8D RedSoft=45182A RedLine=73283A
            Hero=2A3C8A Hero2=1B5A4E Thumb=3F4F80 Glow=FFFFFF
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
