using Shabpa.Core;

namespace Shabpa.Core;

/// <summary>The animated backdrop a premium theme draws behind the pages (see Kit/ThemeScene.cs).</summary>
public enum Scene { None, Lanterns, Rain, Dunes, Snow, Embers, Tiles, Ink, Prism, Mist, Meteors,
    Persepolis, Cyrus, Christmas, Tactical, Smoke, Shards, Pixels, Invaders, Synth, Petals, Fireflies, Deep, Storm, Galaxy, Orbit, Steam, Yalda, Ripples, Matrix, Shapes }

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
        New("persepolis", "تخت جمشید", "Persepolis", "ستون‌های سنگیِ پلکان آپادانا زیر خورشید زرین و غبار", "Sandstone columns of the Apadana under a golden sun and drifting dust", 2400, Scene.Persepolis, false, """
            Bg=1B140D Bg2=3A2812 Side=17110B Side2=342411 Panel=322617 Panel2=3C3123 Panel3=463C2E Line=5B5145 Text=FFF4E0 Muted=B6AC9C Faint=988F81 Accent=E2B04A Accent1=D9A441 Accent2=3E8FB0 AccentSoft=503C1C OnAccent=0E0A06 Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=634B21 Hero2=28434B Thumb=5C5347 Glow=FFFFFF Halo=D9A441
            """),
        New("cyrus", "کوروش بزرگ", "Cyrus the Great", "لاجوردِ شاهانه و قرص بالدارِ زرین که آرام نفس می‌کشد", "Royal lapis and a golden winged disc that slowly breathes", 2800, Scene.Cyrus, false, """
            Bg=070E24 Bg2=14235A Side=060C1F Side2=111F4F Panel=152148 Panel2=212C51 Panel3=2C375A Line=444D6D Text=F2F4FF Muted=A7AAB9 Faint=888C9C Accent=F0C75E Accent1=E3B64A Accent2=3D6BE0 AccentSoft=453D2F OnAccent=040712 Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=5B4E32 Hero2=1C316B Thumb=454F6E Glow=FFFFFF Halo=E3B64A
            """),
        New("christmas", "کریسمس", "Christmas", "برف، درخت‌های کاج و رشته‌چراغ‌هایی که یکی‌یکی روشن می‌شوند", "Snow, pines and string lights that glow one after another", 1600, Scene.Christmas, false, """
            Bg=0A1A12 Bg2=12301E Side=08160F Side2=102C1C Panel=152D20 Panel2=21382B Panel3=2C4236 Line=44574D Text=FFF5F0 Muted=B1AFA9 Faint=91928C Accent=FF7070 Accent1=E5383B Accent2=2FA866 AccentSoft=47221D OnAccent=050D09 Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=5D2522 Hero2=185032 Thumb=45594E Glow=FFFFFF Halo=E5383B
            """),
        New("warzone", "میدان نبرد", "Warzone", "رنگ‌بندی زیتونی و نارنجیِ تاکتیکی با اسکن و ردِ گلوله", "Tactical olive and amber with a scanner sweep and tracer fire", 2200, Scene.Tactical, false, """
            Bg=0D0F0B Bg2=1E2416 Side=0B0D09 Side2=1B2014 Panel=1D2218 Panel2=282D24 Panel3=34382F Line=4A4E46 Text=EEF0E6 Muted=A6A8A0 Faint=898B83 Accent=F2A33A Accent1=E8932A Accent2=9DB04A AccentSoft=4A3414 OnAccent=060806 Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=604117 Hero2=444C23 Thumb=4C5048 Glow=FFFFFF Halo=E8932A
            """),
        New("battlefield", "خط مقدم", "Frontline", "دودِ جنگ و نورافکن‌هایی که آسمان را جارو می‌کنند", "War smoke and searchlights sweeping the sky", 2200, Scene.Smoke, false, """
            Bg=0C0E12 Bg2=1C2230 Side=0A0C0F Side2=191E2A Panel=1C2029 Panel2=272B34 Panel3=33363E Line=494D54 Text=EDF0F4 Muted=A5A8AC Faint=888A8E Accent=FF6B2C Accent1=FF5A1F Accent2=7C8EA6 AccentSoft=502316 OnAccent=060709 Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=682B17 Hero2=373F4A Thumb=4B4E55 Glow=FFFFFF Halo=FF5A1F
            """),
        New("radiant", "رادیانت", "Radiant", "برش‌های تیز سرخ و فیروزه‌ای مثل تیراندازی‌های رقابتی", "Sharp red and teal slashes like a competitive shooter", 2000, Scene.Shards, false, """
            Bg=0B1016 Bg2=2A1018 Side=090E13 Side2=241018 Panel=23171E Panel2=2E2329 Panel3=392E34 Line=4F454B Text=F5F1F2 Muted=AAA9AC Faint=8C8C8F Accent=FF4D5A Accent1=FF4655 Accent2=3DE0C0 AccentSoft=3B171E OnAccent=06080B Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=68252E Hero2=1B534D Thumb=51474C Glow=FFFFFF Halo=FF4655
            """),
        New("blocky", "دنیای بلوکی", "Blocky world", "بلوک‌های پیکسلی سبز و خاکی که فرود می‌آیند", "Pixel blocks of grass and earth drifting down", 1500, Scene.Pixels, false, """
            Bg=12140F Bg2=2B2418 Side=0F110D Side2=262116 Panel=27241B Panel2=322F26 Panel3=3D3A32 Line=525049 Text=F0F5E8 Muted=A9ADA3 Faint=8C9086 Accent=6FD14F Accent1=5CBF3F Accent2=B07A3E AccentSoft=27441C OnAccent=090A08 Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=2E5521 Hero2=4E3B21 Thumb=54524B Glow=FFFFFF Halo=5CBF3F
            """),
        New("arcade", "آرکید", "Arcade", "ردیف مهاجمان فضایی که روی صفحه‌ی بنفش رژه می‌روند", "Rows of space invaders marching across a purple screen", 1800, Scene.Invaders, false, """
            Bg=0A0414 Bg2=2A0F4A Side=080311 Side2=240D3F Panel=231138 Panel2=2E1D42 Panel3=39294C Line=4F4160 Text=F7EEFF Muted=ABA3B4 Faint=8C8595 Accent=FF4FD8 Accent1=E83CC0 Accent2=38E8FF AccentSoft=481444 OnAccent=05020A Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=5E1955 Hero2=184E60 Thumb=514361 Glow=FFFFFF Halo=E83CC0
            """),
        New("synthwave", "سینت‌ویو", "Synthwave", "خورشیدِ راه‌راه و شبکه‌ی در حال حرکت به سمت افق", "A striped sun and a grid rushing toward the horizon", 2000, Scene.Synth, false, """
            Bg=12062A Bg2=3D0F56 Side=0F0524 Side2=340D4D Panel=301248 Panel2=3A1E51 Panel3=452A5A Line=59416D Text=FFF0FA Muted=B3A5B7 Faint=94879C Accent=FF6EC7 Accent1=FF4FA8 Accent2=FFB02E AccentSoft=541A4D OnAccent=090315 Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=6C225A Hero2=6C472C Thumb=5B446E Glow=FFFFFF Halo=FF4FA8
            """),
        New("sakura", "شکوفه‌ی گیلاس", "Cherry blossom", "گلبرگ‌هایی که نرم و چرخان فرود می‌آیند", "Petals that tumble down softly", 1400, Scene.Petals, false, """
            Bg=1A1018 Bg2=3A1B2E Side=160E14 Side2=34192A Panel=321D2B Panel2=3C2836 Panel3=463440 Line=5B4A55 Text=FFF2F6 Muted=B6AAAF Faint=988C92 Accent=FFB7C9 Accent1=F59AB4 Accent2=C9A0E8 AccentSoft=573744 OnAccent=0D080C Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=6D4453 Hero2=5C4767 Thumb=5C4C57 Glow=FFFFFF Halo=F59AB4
            """),
        New("fireflies", "کرم شب‌تاب", "Fireflies", "شب جنگلی و کرم‌های شب‌تابِ سرگردان", "A forest night full of wandering fireflies", 1300, Scene.Fireflies, false, """
            Bg=060D0A Bg2=0F2418 Side=050B08 Side2=0D1F15 Panel=122119 Panel2=1E2C24 Panel3=2A3730 Line=414D47 Text=EAF5E4 Muted=A1AB9E Faint=838D82 Accent=D9F25E Accent1=C6E04A Accent2=4FD08A AccentSoft=3C481C OnAccent=030605 Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=44511E Hero2=22573B Thumb=444F49 Glow=FFFFFF Halo=C6E04A
            """),
        New("deep", "اعماق", "The deep", "حباب‌ها، پرتوهای نور و ماهی‌های سایه‌وار در آبی عمیق", "Bubbles, light rays and shadowy fish in deep blue", 1700, Scene.Deep, false, """
            Bg=02101B Bg2=04304A Side=020E17 Side2=042A41 Panel=0B293B Panel2=173445 Panel3=233E4F Line=3C5462 Text=E6F8FF Muted=9DAEB6 Faint=7F9098 Accent=4FD1E8 Accent1=2BB8D8 Accent2=3D7BFF AccentSoft=0D3F50 OnAccent=01080E Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=125063 Hero2=183972 Thumb=3D5564 Glow=FFFFFF Halo=2BB8D8
            """),
        New("storm", "طوفان", "Storm", "باران تند و رعدوبرقی که شب را روشن می‌کند", "Hard rain and lightning that lights up the night", 1900, Scene.Storm, false, """
            Bg=0B0E15 Bg2=1B2133 Side=090C12 Side2=181D2D Panel=1B1F2D Panel2=262A38 Panel3=323542 Line=494C57 Text=EEF1FA Muted=A5A8B1 Faint=888B93 Accent=B7C7FF Accent1=9DB2FF Accent2=F5E27A AccentSoft=343C57 OnAccent=06070A Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=424C6E Hero2=48452F Thumb=4B4D59 Glow=FFFFFF Halo=9DB2FF
            """),
        New("galaxy", "کهکشان", "Galaxy", "کهکشانِ مارپیچی که آرام می‌چرخد", "A spiral galaxy turning slowly", 2600, Scene.Galaxy, false, """
            Bg=07040F Bg2=241049 Side=06030D Side2=1E0E3D Panel=1E1235 Panel2=291E3F Panel3=342A49 Line=4B415D Text=F3EEFF Muted=A7A3B2 Faint=898593 Accent=C9A0FF Accent1=A97BFF Accent2=FF7AD0 AccentSoft=342552 OnAccent=040208 Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=45316A Hero2=653158 Thumb=4C445F Glow=FFFFFF Halo=A97BFF
            """),
        New("orbit", "ایستگاه فضایی", "Space station", "حلقه‌های مداری و رادارِ در حال چرخش", "Orbit rings and a turning radar sweep", 2000, Scene.Orbit, false, """
            Bg=070B10 Bg2=12202E Side=06090E Side2=101C28 Panel=141E27 Panel2=202932 Panel3=2C343D Line=434B52 Text=ECF4FA Muted=A3A9AF Faint=858B91 Accent=7FE3FF Accent1=52D0F5 Accent2=8FA8FF AccentSoft=1C4250 OnAccent=040608 Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=245667 Hero2=3B476B Thumb=454C54 Glow=FFFFFF Halo=52D0F5
            """),
        New("teahouse", "قهوه‌خانه", "Teahouse", "گرمای چراغ‌های آویزان و بخارِ چای", "The warmth of hanging lamps and rising tea steam", 1300, Scene.Steam, false, """
            Bg=140D08 Bg2=2E1B0F Side=110B07 Side2=29180E Panel=291C13 Panel2=34271F Panel3=3E332B Line=544942 Text=FAEEDD Muted=B0A699 Faint=92897D Accent=E8A25A Accent1=D68A3C Accent2=A8503A AccentSoft=4A3017 OnAccent=0A0604 Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=5E3C1C Hero2=4C261B Thumb=554B44 Glow=FFFFFF Halo=D68A3C
            """),
        New("yalda", "شب یلدا", "Yalda night", "دانه‌های انار، شمع و ستاره در بلندترین شب سال", "Pomegranate seeds, candlelight and stars on the longest night", 1700, Scene.Yalda, false, """
            Bg=14060B Bg2=3A0D1A Side=110509 Side2=320C17 Panel=2F111A Panel2=391D25 Panel3=442931 Line=594148 Text=FFEFEA Muted=B4A4A3 Faint=958686 Accent=FF4D6A Accent1=E8284A Accent2=FFB347 AccentSoft=4F101D OnAccent=0A0306 Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=651323 Hero2=6D4822 Thumb=5A434A Glow=FFFFFF Halo=E8284A
            """),
        New("koi", "حوض ماهی قرمز", "Koi pond", "موج‌های حوض و ماهی‌های قرمز و زرین", "Pond ripples and red-gold fish", 1900, Scene.Ripples, false, """
            Bg=04141A Bg2=0B3A44 Side=031116 Side2=0A323C Panel=0F2F37 Panel2=1B3941 Panel3=27444B Line=3F595F Text=EAF7F5 Muted=A0AEAF Faint=829192 Accent=FF7A45 Accent1=FF6A33 Accent2=FFD27A AccentSoft=4A2C21 OnAccent=020A0D Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=633524 Hero2=454533 Thumb=415A61 Glow=FFFFFF Halo=FF6A33
            """),
        New("matrix", "باران کد", "Code rain", "ستون‌های کدِ سبزِ باران‌گونه", "Falling columns of green code", 1500, Scene.Matrix, false, """
            Bg=000A04 Bg2=02240F Side=000803 Side2=021F0D Panel=091F11 Panel2=152A1D Panel3=223529 Line=3A4C41 Text=E4FFEC Muted=9BB1A2 Faint=7D9184 Accent=3DFF7A Accent1=22E866 Accent2=00B85A AccentSoft=0A481F OnAccent=000502 Gold=FFC764 GoldSoft=3A2F17 Green=6EE7B7 GreenSoft=0E3A2D Red=FF8A9A RedSoft=45182A RedLine=73283A Hero=0D5E29 Hero2=004C25 Thumb=3D4D43 Glow=FFFFFF Halo=22E866
            """),
        New("bauhaus", "باهاوس", "Bauhaus", "شکل‌های هندسیِ رنگیِ شناور روی کاغذِ روشن", "Floating primary-colour shapes on pale paper", 1400, Scene.Shapes, true, """
            Bg=F4EFE6 Bg2=E9E2D3 Side=FBF8F1 Side2=F0E9DA Panel=FFFFFF Panel2=F1ECE0 Panel3=E0D8C6 Line=D2C9B4 Text=171717 Muted=4A4640 Faint=6B665E Accent=B52418 Accent1=D93A2B Accent2=2A56C6 AccentSoft=FBEDEA OnAccent=FFFFFF Gold=7A5200 GoldSoft=F6E7BE Green=0A6B45 GreenSoft=D5EFE1 Red=B0203A RedSoft=F8DCE1 RedLine=E5A5B0 Hero=F3D9A8 Hero2=C9D8F5 Thumb=BDB39D Glow=000000 Halo=2A56C6
            """),
    ];

    public static PremiumTheme? Find(string? key) => All.FirstOrDefault(t => t.Key == key);
}

public static class PremiumThemes
{
    public static Palette? FindPalette(string? key) => PremiumTheme.Find(key)?.Palette;
}
