using Shabpa.Core;

namespace Shabpa.Core;

/// <summary>The animated backdrop a premium theme draws behind the pages (see Kit/ThemeScene.cs).</summary>
public enum Scene { None, Lanterns, Rain, Dunes, Snow, Embers, Tiles, Ink, Prism, Mist, Meteors }

/// <summary>A theme bought with feathers: its own palette, a slow animated scene behind the pages and a price.
/// None of them looks like a free colour theme; they are cosmetic only.</summary>
public sealed record PremiumTheme(string Key, string Fa, string En, string NoteFa, string NoteEn, int Price, Scene Scene, Palette Palette)
{
    public string Name => L.T(Fa, En);
    public string Note => L.T(NoteFa, NoteEn);
    public string OwnedId => Prefix + Key;

    /// <summary>Premium themes sit in the same list as wardrobe parts (RewardsState.Owned) under this prefix.</summary>
    public const string Prefix = "theme:";

    private static PremiumTheme New(string key, string fa, string en, string noteFa, string noteEn, int price, Scene scene, bool light, string spec) =>
        new(key, fa, en, noteFa, noteEn, price, scene, Palette.Build(key, fa, en, light, spec));

    public static readonly IReadOnlyList<PremiumTheme> All =
    [
        New("lanternfest", "جشن فانوس", "Lantern festival", "فانوس‌های کاغذیِ گرم که آرام بالا می‌روند", "Warm paper lanterns drifting slowly upward", 900, Scene.Lanterns, false, """
            Bg=1A0B0B Bg2=33100F Side=210E0D Side2=3D1512 Panel=2B1412 Panel2=371A16 Panel3=482320 Line=5E2F28 Text=FFF1E3 Muted=EBC9B0 Faint=C79F88 Accent=FFB347 Accent1=FF9A2E Accent2=FF5A36 AccentSoft=5A2A12 OnAccent=1F0C00 Gold=FFD27A GoldSoft=45331A Green=7BE0A0 GreenSoft=153E2A Red=FF8A8A RedSoft=4D1D1D RedLine=7F3333 Hero=7A2412 Hero2=8A1F2E Thumb=6B3A30 Glow=FFFFFF Halo=FF7A1F
            """),
        New("rainglass", "پشت شیشه‌ی بارانی", "Rainy window", "باران آرام روی شیشه و شبِ خاکستری‌آبی", "Quiet rain on the glass, a blue-grey night", 700, Scene.Rain, false, """
            Bg=0B1015 Bg2=131C25 Side=0E141A Side2=17222D Panel=141C24 Panel2=1B2530 Panel3=24313F Line=2F3F50 Text=E6EEF5 Muted=A9BACB Faint=8B9DB0 Accent=7FB4D6 Accent1=6AA5CC Accent2=8E9CE0 AccentSoft=1D3A52 OnAccent=06141D Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=1E3A52 Hero2=2A3358 Thumb=3A4D60 Glow=FFFFFF Halo=6AA5CC
            """),
        New("dunes", "کویر شب", "Desert night", "تپه‌های ماسه‌ای زیر آسمان پرستاره", "Sand dunes under a field of stars", 900, Scene.Dunes, false, """
            Bg=16100B Bg2=2A1A10 Side=1B130D Side2=33200F Panel=221811 Panel2=2D2016 Panel3=3B2B1D Line=51392A Text=F8EBD8 Muted=D8C0A0 Faint=B49E82 Accent=E8B86B Accent1=E0A04F Accent2=C8744A AccentSoft=51361A OnAccent=1E1204 Gold=FFD27A GoldSoft=45331A Green=8FDB8F GreenSoft=173A1E Red=FF8A7A RedSoft=48201A RedLine=7A362C Hero=6A4220 Hero2=6A2E22 Thumb=6A5036 Glow=FFFFFF Halo=E0A04F
            """),
        New("snowfall", "برف‌بارش", "Snowfall", "برفِ نرم روی شبِ یخی", "Soft snow falling over an icy night", 1100, Scene.Snow, false, """
            Bg=08121C Bg2=12263A Side=0B1724 Side2=163049 Panel=101E2D Panel2=172A3E Panel3=20384F Line=2C4A66 Text=EEF7FF Muted=B3CBE0 Faint=94AFC8 Accent=CFE8FF Accent1=A8D4FF Accent2=8FB0F0 AccentSoft=24445F OnAccent=06141F Gold=FFC764 GoldSoft=3A2F17 Green=7DF0C2 GreenSoft=124A38 Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=2A5078 Hero2=34407A Thumb=45617D Glow=FFFFFF Halo=A8D4FF
            """),
        New("embers", "اخگر", "Embers", "جرقه‌های آتش که از تاریکی بالا می‌آیند", "Sparks of fire rising out of the dark", 1200, Scene.Embers, false, """
            Bg=0F0706 Bg2=22100A Side=130A07 Side2=2B140B Panel=1A0E0B Panel2=251510 Panel3=331E17 Line=4A2A1F Text=FFEFE6 Muted=E3BFAE Faint=BE9686 Accent=FF6A2B Accent1=FF5A1F Accent2=FFB020 AccentSoft=55220E OnAccent=1A0600 Gold=FFD27A GoldSoft=45331A Green=7BE0A0 GreenSoft=153E2A Red=FF8A8A RedSoft=4D1D1D RedLine=7F3333 Hero=7A2208 Hero2=663C0A Thumb=61382A Glow=FFFFFF Halo=FF5A1F
            """),
        New("tilework", "کاشی‌کاری", "Tilework", "نقش کاشیِ فیروزه‌ای و زر که آرام می‌چرخد", "Turquoise and gold tile geometry, slowly turning", 1800, Scene.Tiles, false, """
            Bg=041A24 Bg2=082B3A Side=051E2A Side2=0A3446 Panel=092633 Panel2=0F3243 Panel3=164256 Line=205870 Text=EAF6F4 Muted=A9D0CE Faint=8CB6B6 Accent=E3BE62 Accent1=D9B04A Accent2=2BB5B0 AccentSoft=3F3A18 OnAccent=1A1203 Gold=FFD27A GoldSoft=45331A Green=7DF0C2 GreenSoft=124A38 Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=0C4A5E Hero2=0F3F6A Thumb=2E6F85 Glow=FFFFFF Halo=2BB5B0
            """),
        New("inkwash", "آب‌ومرکب", "Ink wash", "کاغذ کاهی و لکه‌های مرکب که پخش می‌شوند", "Parchment paper with ink blooming across it", 1500, Scene.Ink, true, """
            Bg=F2EDE3 Bg2=E8E1D2 Side=FAF6EE Side2=F0E9DA Panel=FBF8F1 Panel2=EFE9DB Panel3=DED5C2 Line=CFC5AF Text=1F1B16 Muted=51483C Faint=6F6455 Accent=8C2F2B Accent1=A63A34 Accent2=3A3A3A AccentSoft=F0D7D2 OnAccent=FFFFFF Gold=7A5200 GoldSoft=F6E7BE Green=0A6B45 GreenSoft=D5EFE1 Red=B0203A RedSoft=F8DCE1 RedLine=E5A5B0 Hero=E7D6C8 Hero2=D9D6CC Thumb=B9AE98 Glow=000000 Halo=8C2F2B
            """),
        New("prism", "منشور", "Prism", "وجه‌های شیشه‌ایِ رنگین‌کمانی که برق می‌زنند", "Glassy iridescent facets that shimmer", 2200, Scene.Prism, false, """
            Bg=0A0C12 Bg2=14162A Side=0D1017 Side2=191C33 Panel=12151F Panel2=1A1E2C Panel3=252A3C Line=343A52 Text=EFF2FA Muted=B4BCD4 Faint=96A0BC Accent=A78BFA Accent1=8B7CF6 Accent2=5EEAD4 AccentSoft=2C2658 OnAccent=0E0A24 Gold=FFC764 GoldSoft=3A2F17 Green=5EEAD4 GreenSoft=0F3A36 Red=FF8AA6 RedSoft=45182E RedLine=73283F Hero=3A2E7A Hero2=0F4F4D Thumb=474E6C Glow=FFFFFF Halo=8B7CF6
            """),
        New("mistmorn", "مه صبحگاهی", "Misty morning", "مهِ نرمِ صبح روی سبزِ تازه", "Soft morning mist over fresh green", 800, Scene.Mist, true, """
            Bg=E9EFEA Bg2=DCE7E0 Side=F6FAF7 Side2=E7F0EA Panel=FBFDFB Panel2=EAF1EC Panel3=D5E1D9 Line=C2D2C8 Text=17231D Muted=435649 Faint=62766A Accent=2F6F5C Accent1=3F7D6B Accent2=B8683F AccentSoft=D3EBE1 OnAccent=FFFFFF Gold=7A5200 GoldSoft=F6E7BE Green=0A6B45 GreenSoft=D5EFE1 Red=B0203A RedSoft=F8DCE1 RedLine=E5A5B0 Hero=CFE6DA Hero2=F1DCCB Thumb=A9BDB0 Glow=000000 Halo=3F7D6B
            """),
        New("meteors", "شهاب‌باران", "Meteor shower", "ستاره‌های دنباله‌دار که آسمان را می‌شکافند", "Shooting stars streaking across the sky", 2000, Scene.Meteors, false, """
            Bg=060812 Bg2=10142E Side=090C1A Side2=151A3C Panel=0F1324 Panel2=161B33 Panel3=20274A Line=2C3560 Text=EEF0FF Muted=B0B8E8 Faint=939CD0 Accent=FFE08A Accent1=FFD460 Accent2=7C8CFF AccentSoft=3F3A20 OnAccent=1A1400 Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=1F2A78 Hero2=3A2A78 Thumb=3A4478 Glow=FFFFFF Halo=7C8CFF
            """),
    ];

    public static PremiumTheme? Find(string? key) => All.FirstOrDefault(t => t.Key == key);
}

public static class PremiumThemes
{
    public static Palette? FindPalette(string? key) => PremiumTheme.Find(key)?.Palette;
}
