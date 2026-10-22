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
        "Hero", "Hero2", "Thumb", "Glow", "Halo",
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
        // The soft glow that bleeds from the corner of the window; it follows the accent unless a theme says otherwise.
        if (!colors.ContainsKey("Halo")) colors["Halo"] = colors["Accent1"];
        return new Palette(key, fa, en, light, colors);
    }

    public static readonly IReadOnlyList<Palette> All =
    [
        Make("night", "آبی شب", "Night blue", false, """
            Bg=0A0D18 Side=0D111F Panel=111627 Panel2=171D33 Panel3=1E2540 Line=232B47
            Text=EEF1FA Muted=AAB3D3 Faint=8089AD
            Accent=8087FF Accent1=7B6BF5 Accent2=5C6CFF AccentSoft=1B2052 OnAccent=FFFFFF
            Gold=FFC764 GoldSoft=3A2F17 Green=3DDC97 GreenSoft=10352B Red=FF6B7F RedSoft=3D1722 RedLine=6B2534
            Hero=1B1D4A Thumb=2C3559 Glow=FFFFFF
            """),
        Make("black", "مشکی", "Black", false, """
            Bg=000000 Side=060607 Panel=0E0E10 Panel2=17171A Panel3=222226 Line=2B2B31
            Text=F2F2F4 Muted=9D9DA7 Faint=86868E
            Accent=5AA9FF Accent1=74B7FF Accent2=4A98F2 AccentSoft=0F2A47 OnAccent=03101F
            Gold=FFC764 GoldSoft=33290F Green=3DDC97 GreenSoft=0D2E24 Red=FF6B7F RedSoft=3A141D RedLine=66222F
            Hero=0F2238 Thumb=3A3A42 Glow=FFFFFF
            """),
        Make("white", "سفید", "White", true, """
            Bg=F3F5FA Side=FFFFFF Panel=FFFFFF Panel2=EDF0F7 Panel3=DCE2EF Line=D3DAE9
            Text=141A2E Muted=4F5A78 Faint=6F7995
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
            Text=F3EDFF Muted=AC9DD0 Faint=9687B8
            Accent=B892FF Accent1=BE9BFF Accent2=9A72FA AccentSoft=37226B OnAccent=160828
            Gold=FFC764 GoldSoft=3D3018 Green=4BE3A4 GreenSoft=123A30 Red=FF7A8D RedSoft=45182A RedLine=73283A
            Hero=3A1F73 Thumb=46347A Glow=FFFFFF
            """),
        Make("emerald", "سبز", "Emerald", false, """
            Bg=030907 Bg2=061811 Side=040C09 Side2=08170F Panel=0A1511 Panel2=101F19 Panel3=162B23 Line=1F3A2F
            Text=E8F5EE Muted=9DBFAF Faint=779F88
            Accent=34D399 Accent1=10B981 Accent2=0E9AA7 AccentSoft=0B3B2E OnAccent=02120C
            Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF7A8D RedSoft=45182A RedLine=73283A
            Hero=0A3A2D Hero2=0A2F33 Thumb=2A5545 Glow=FFFFFF Halo=10B981
            """),
        // forest: mossy, earthy greens on a near-black base; calm and low in saturation
        Make("forest", "سبز جنگلی", "Forest green", false, """
            Bg=060B07 Bg2=0C1A0F Side=070D08 Side2=0D1B10 Panel=0E1810 Panel2=15231A Panel3=1C2F22 Line=29402F
            Text=EAF3E6 Muted=A5BFA2 Faint=8CA48B
            Accent=7CC47C Accent1=5FAF63 Accent2=3F8F52 AccentSoft=173A22 OnAccent=06120A
            Gold=E6C068 GoldSoft=3A3217 Green=8FDB8F GreenSoft=143A1E Red=FF8A8A RedSoft=45201F RedLine=75342F
            Hero=17402A Hero2=1E3A1A Thumb=355E40 Glow=FFFFFF Halo=3F8F52
            """),
        // neon green: pure black with a vivid lime accent; the glow is strong on purpose
        Make("neongreen", "سبز نئونی", "Neon green", false, """
            Bg=000000 Bg2=021208 Side=010302 Side2=03140A Panel=060D08 Panel2=0B1A0F Panel3=11261A Line=17381F
            Text=ECFFF1 Muted=A3D4B0 Faint=86B494
            Accent=39FF88 Accent1=22F27A Accent2=00D96A AccentSoft=053A1B OnAccent=00150A
            Gold=FFE44D GoldSoft=3A3510 Green=5BFF9C GreenSoft=06381C Red=FF7F95 RedSoft=45151F RedLine=7A2433
            Hero=04401F Hero2=02291A Thumb=1F6E3C Glow=FFFFFF Halo=00FF6A
            """),
        Make("bluviolet", "بنفش‌آبی", "Blue violet", false, """
            Bg=0C0D2B Bg2=151447 Side=10123A Side2=1A1760 Panel=1A1D52 Panel2=23276A Panel3=2E3485 Line=3F46A3
            Text=F2F3FF Muted=C0C6FF Faint=A6AEF2
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
            Text=EAF6FF Muted=A0C6E3 Faint=86AECD
            Accent=5CD6FF Accent1=2BB8F5 Accent2=3D6BFF AccentSoft=0F3F66 OnAccent=02101C
            Gold=FFC764 GoldSoft=3A2F17 Green=4EE8B0 GreenSoft=0F3F33 Red=FF7A8D RedSoft=45182A RedLine=73283A
            Hero=0F4778 Hero2=2A3F9E Thumb=36628A Glow=FFFFFF
            """),
        Make("aurora", "شفق", "Aurora", false, """
            Bg=0A0F1A Bg2=161F4A Side=0E1524 Side2=1C2A5C Panel=131C30 Panel2=1A2540 Panel3=233157 Line=2D3E6B
            Text=EEF3FF Muted=A9B8DC Faint=8F9EC5
            Accent=7DF5C6 Accent1=34E0A1 Accent2=8B5CF6 AccentSoft=15405A OnAccent=03140E
            Gold=FFC764 GoldSoft=3A2F17 Green=7DF5C6 GreenSoft=124A3C Red=FF7A8D RedSoft=45182A RedLine=73283A
            Hero=2A3C8A Hero2=1B5A4E Thumb=3F4F80 Glow=FFFFFF
            """),
        Make("vampire", "خون‌آشام", "Vampire", false, """
            Bg=14141C Bg2=21202E Side=191A23 Side2=262436 Panel=1E1F2B Panel2=282A3A Panel3=343746 Line=44475A
            Text=F8F8F2 Muted=C3C7DA Faint=A5A8BC
            Accent=BD93F9 Accent1=BD93F9 Accent2=FF79C6 AccentSoft=3B2D5C OnAccent=1B1230
            Gold=FFC764 GoldSoft=3A2F17 Green=50FA7B GreenSoft=123A22 Red=FF7A8D RedSoft=45182A RedLine=73283A
            Hero=44307A Hero2=7A2A5C Thumb=565B78 Glow=FFFFFF Halo=BD93F9
            """),
        Make("fjord", "فیورد", "Fjord", false, """
            Bg=232831 Bg2=2E3440 Side=262B35 Side2=313846 Panel=2E3440 Panel2=3B4252 Panel3=3F485A Line=4C566A
            Text=ECEFF4 Muted=CED5E2 Faint=B4BDCE
            Accent=88C0D0 Accent1=88C0D0 Accent2=81A1C1 AccentSoft=2E4A5C OnAccent=14202A
            Gold=FFC764 GoldSoft=3A2F17 Green=A3E6B0 GreenSoft=21402B Red=FFA3B0 RedSoft=4A2530 RedLine=7A3A48
            Hero=2F4D63 Hero2=2F3E63 Thumb=5E6A82 Glow=FFFFFF Halo=88C0D0
            """),
        Make("neon", "نئون", "Neon city", false, """
            Bg=0F1019 Bg2=1A1B2E Side=13141F Side2=1E2038 Panel=181926 Panel2=20223A Panel3=2A2D4A Line=3A3F66
            Text=E6EAFF Muted=B0B8E6 Faint=949CCB
            Accent=7AA2F7 Accent1=7AA2F7 Accent2=BB9AF7 AccentSoft=26325C OnAccent=0E1530
            Gold=FFC764 GoldSoft=3A2F17 Green=9ECE6A GreenSoft=22381A Red=FF7A93 RedSoft=45182A RedLine=73283A
            Hero=2D3B7A Hero2=4A2F7A Thumb=4A5080 Glow=FFFFFF Halo=7AA2F7
            """),
        Make("retro", "رترو", "Retro", false, """
            Bg=1C1C1A Bg2=282621 Side=212120 Side2=2D2A24 Panel=282826 Panel2=33322E Panel3=403E38 Line=504E46
            Text=FBF1C7 Muted=D5C4A1 Faint=B9AB91
            Accent=FABD2F Accent1=FABD2F Accent2=FE8019 AccentSoft=4A3A12 OnAccent=231A04
            Gold=FFD27A GoldSoft=45331A Green=B8BB26 GreenSoft=2E3010 Red=FB8F7E RedSoft=4A2018 RedLine=7A3A2A
            Hero=4F3F16 Hero2=5A3412 Thumb=665C54 Glow=FFFFFF Halo=FE8019
            """),
        Make("teal", "فیروزه‌ای", "Deep teal", false, """
            Bg=00161C Bg2=02242C Side=011B22 Side2=033038 Panel=062A32 Panel2=0A363F Panel3=10434D Line=1B5A66
            Text=E9F7F5 Muted=A9D0CE Faint=91B9B8
            Accent=3CC4C0 Accent1=2AA198 Accent2=268BD2 AccentSoft=0B4A55 OnAccent=001418
            Gold=FFC764 GoldSoft=3A2F17 Green=7DF0C2 GreenSoft=124A38 Red=FF7A8D RedSoft=45182A RedLine=73283A
            Hero=084650 Hero2=0F3F6A Thumb=2E6F78 Glow=FFFFFF Halo=2AA198
            """),
        Make("duskrose", "رز غروب", "Dusk rose", false, """
            Bg=14111E Bg2=221A2E Side=191524 Side2=281E36 Panel=211B2E Panel2=2A2338 Panel3=372E49 Line=4A3F60
            Text=F4ECFA Muted=CDBFE0 Faint=AA9DC1
            Accent=EBBCBA Accent1=EBBCBA Accent2=C4A7E7 AccentSoft=4A2E44 OnAccent=24121E
            Gold=FFC764 GoldSoft=3A2F17 Green=5FEDAA GreenSoft=15412F Red=FF8A9A RedSoft=4D1D27 RedLine=7F3340
            Hero=5A2F52 Hero2=3E3470 Thumb=5A4E78 Glow=FFFFFF Halo=EBBCBA
            """),
        Make("code", "نیمه‌شب کد", "Midnight code", false, """
            Bg=0D1117 Bg2=161B22 Side=0A0E13 Side2=11161D Panel=161B22 Panel2=1F2630 Panel3=2A323D Line=30363D
            Text=E6EDF3 Muted=B1BAC4 Faint=929BA5
            Accent=58A6FF Accent1=388BFD Accent2=1F6FEB AccentSoft=15305A OnAccent=0A1420
            Gold=FFC764 GoldSoft=3A2F17 Green=56D364 GreenSoft=12361B Red=FF7B72 RedSoft=45201C RedLine=73352E
            Hero=15305A Hero2=1B2F4F Thumb=484F58 Glow=FFFFFF Halo=388BFD
            """),
        Make("latte", "لاته", "Latte", true, """
            Bg=EFF1F5 Bg2=E6E9EF Side=FFFFFF Side2=F4F5F9 Panel=FFFFFF Panel2=EEF0F6 Panel3=DDE1EC Line=CCD0DA
            Text=1E2030 Muted=4C4F69 Faint=6C6F85
            Accent=7C30E0 Accent1=8839EF Accent2=1E66F5 AccentSoft=E6DCFB OnAccent=FFFFFF
            Gold=865600 GoldSoft=FFEFC7 Green=0A7349 GreenSoft=D8F4E7 Red=BE2340 RedSoft=FFDFE5 RedLine=F0A8B4
            Hero=E4DAFB Hero2=DCE6FD Thumb=B8BDD0 Glow=000000 Halo=8839EF
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
