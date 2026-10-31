using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>«مدال‌ها و مأموریت‌ها»: Bidar's level and feathers, today's quests, the night streak and the 30 medals.</summary>
public sealed class RewardsPage : PageBase
{
    private enum Filter { All, Earned, Locked }

    private TextBlock _shopFeathers = new();

    private readonly StackPanel _root = new();
    private readonly StackPanel _hero = new(), _quests = new();
    private readonly WrapPanel _medals = new() { Margin = new Thickness(-6, 8, -6, 0) };
    private readonly StackPanel _filterHost = new();
    private readonly TextBlock _count = new() { Style = (Style)Application.Current.Resources["Label"] };
    private Filter _filter = Filter.All;
    private readonly HashSet<string> _seen = [];
    private bool _primed;

    public override string PageTitle => L.T("مدال‌ها و مأموریت‌ها", "Medals & quests");
    public override string PageSubtitle => L.T("هر کاری که با شبپا می‌کنی پَر و مدال می‌ده. همه‌چیزش فقط روی همین کامپیوتره.",
        "Everything you do with Shabpa earns feathers and medals. It all stays on this PC.");

    public RewardsPage()
    {
        Content = _root;
        // two tabs: the cabinet (level, quests, medals) and the shop, which is not open yet
        var tabs = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        _root.Children.Add(tabs);
        var (heroCard, heroBody) = K.Card(null, null);
        heroBody.Children.Add(_hero);
        _root.Children.Add(heroCard);

        var (qCard, qBody) = K.Card(L.T("مأموریت‌ها", "Quests"),
            L.T("هر روز سه تا مأموریت کوچیک و هر هفته یه مأموریت بزرگ. پَرها وقتی کار تموم شد خودکار می‌رسن.",
                "Three small quests a day and one big one a week. Feathers arrive by themselves when the job is done."));
        qCard.Margin = new Thickness(0, 16, 0, 0);
        qBody.Children.Add(_quests);
        _root.Children.Add(qCard);

        var (mCard, mBody) = K.Card(L.T("مدال‌ها", "Medals"),
            L.T("از برنزی تا افسانه‌ای، هر رده سخت‌تر از قبلی. چند مدال مخفی هم هست که تا نگیری معلوم نمی‌شن.", "From bronze to legendary, each rung harder than the last. Some hidden medals stay a secret until you earn them."));
        mCard.Margin = new Thickness(0, 16, 0, 0);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        _count.VerticalAlignment = VerticalAlignment.Center;
        _count.Margin = new Thickness(14, 0, 0, 0);
        bar.Children.Add(_filterHost);
        bar.Children.Add(_count);
        mBody.Children.Add(bar);
        mBody.Children.Add(_medals);

        _root.Children.Add(mCard);

        var (shopCard, shop) = K.Card(null, null);
        shopCard.Padding = new Thickness(30, 44, 30, 44);
        shop.HorizontalAlignment = HorizontalAlignment.Center;
        shop.Children.Add(new IconBadge("bag", "HueC", 72, 36, 22) { HorizontalAlignment = HorizontalAlignment.Center });
        shop.Children.Add(new TextBlock { Text = L.T("فروشگاه پَر", "Feather shop"), Style = St("H1"), FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) });
        shop.Children.Add(new TextBlock { Text = L.T("به‌زودی...", "Coming soon..."), Style = St("H2"), FontSize = 18, Foreground = Res("Gold"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
        shop.Children.Add(new TextBlock
        {
            Text = L.T("پَرهایی که جمع می‌کنی اینجا خرج می‌شن: کلاه و لباس برای بیدار، تم‌ها و طرح‌های تازه‌ی اورلی. فعلاً فقط جمعشون کن.",
                "The feathers you collect will be spent here: hats and outfits for Bidar, new themes and overlay looks. For now, just keep collecting."),
            Style = St("Body"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 440, Margin = new Thickness(0, 12, 0, 0),
        });
        shop.Children.Add(new TextBlock
        {
            Text = L.T($"الان {Fa.N(Hub.Rewards.State.Feathers)} پَر داری.", $"You have {Hub.Rewards.State.Feathers} feathers."), Style = St("Label"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0),
        });
        shopCard.Visibility = Visibility.Collapsed;
        _root.Children.Add(shopCard);
        _shopFeathers = (TextBlock)shop.Children[^1];

        var cabinet = new FrameworkElement[] { heroCard, qCard, mCard };
        void ShowTab(bool shopTab)
        {
            foreach (var c in cabinet) c.Visibility = shopTab ? Visibility.Collapsed : Visibility.Visible;
            shopCard.Visibility = shopTab ? Visibility.Visible : Visibility.Collapsed;
            if (shopTab) Motion.Pop(shopCard);
        }
        Chips(tabs, new[] { (false, L.T("مدال‌ها و مأموریت‌ها", "Medals & quests")), (true, L.T("فروشگاه", "Shop")) }, false, ShowTab);

        // test aid (screenshots of the medal grid on a short window): SHABPA_RW_ONLY=medals hides the two cards above it
        if (Environment.GetEnvironmentVariable("SHABPA_RW_ONLY") == "medals") { heroCard.Visibility = Visibility.Collapsed; qCard.Visibility = Visibility.Collapsed; }

        Hub.Rewards.Changed += () => Dispatcher.BeginInvoke(() => { if (IsVisible) Refresh(); });
    }

    // ---- drawing -----------------------------------------------------------------------------------------------

    public override void Refresh()
    {
        var engine = Hub.Rewards;
        BuildHero(engine);
        BuildQuests(engine);
        BuildFilter();
        BuildMedals(engine);
        _primed = true;
    }

    private void BuildHero(RewardsEngine engine)
    {
        var r = engine.State;
        var level = engine.Level;
        var next = engine.NextLevel;
        _hero.Children.Clear();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var ringHost = new Grid { Width = 104, Height = 104 };
        var ring = new RingGauge { Width = 104, Height = 104, ViewSize = 120, RingRadius = 48, Thickness = 8, Ticks = false, Knob = false };
        var fraction = next is null ? 1 : (double)(r.TotalFeathers - level.From) / (next.From - level.From);
        ring.AnimateTo(Math.Clamp(fraction, 0, 1), 900);
        ringHost.Children.Add(ring);
        ringHost.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new TextBlock { Text = Fa.N(level.Level), FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Res("Text"), HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = L.T("سطح", "level"), Style = St("Tip"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -4, 0, 0) },
            },
        });
        grid.Children.Add(ringHost);

        var text = new StackPanel { Margin = new Thickness(22, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        text.Children.Add(new TextBlock { Text = L.T($"بیدار «{level.Name}»", $"Bidar the “{level.Name}”"), Style = St("H1"), FontSize = 22 });
        text.Children.Add(new TextBlock
        {
            Text = next is null
                ? L.T("به بالاترین سطح رسیدی. دمت گرم!", "You reached the top level. Well done!")
                : L.T($"{Fa.N(next.From - r.TotalFeathers)} پَر دیگه تا سطح «{next.Name}»", $"{Fa.N(next.From - r.TotalFeathers)} more feathers to “{next.Name}”"),
            Style = St("Body"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
        });

        var chips = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        chips.Children.Add(Pill("feather", "Accent", $"{Fa.N(r.Feathers)} {L.T("پَر", "feathers")}"));
        chips.Children.Add(Pill("flame", "HueC", L.T($"{Fa.N(engine.Streak)} شب پیاپی", $"{Fa.N(engine.Streak)}-night streak")));
        chips.Children.Add(Pill("medal", "Gold", L.T($"{Fa.N(r.Medals.Count)} از {Fa.N(RewardsCatalog.Medals.Count)} مدال", $"{r.Medals.Count} of {RewardsCatalog.Medals.Count} medals")));
        text.Children.Add(chips);
        grid.Children.Add(text);
        _hero.Children.Add(grid);

        if (engine.Streak >= 1 && engine.Streak < 7)
            _hero.Children.Add(new TextBlock
            {
                Text = L.T($"شب‌های پیاپی: {Fa.N(engine.Streak)} از ۷. با هفت شب پیاپی ۱۰ پَر هدیه می‌گیری.", $"Streak: {engine.Streak} of 7. Seven nights in a row gives 10 feathers."),
                Style = St("Tip"), Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap,
            });
    }

    private static Border Pill(string icon, string tint, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Icon(icon, 16, K.Brush(tint), 1.8) { VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, Style = St("Label"), FontWeight = FontWeights.SemiBold, Foreground = K.Brush("Text"), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return new Border
        {
            Background = K.Brush(tint + "Soft"), CornerRadius = new CornerRadius(14), Padding = new Thickness(11, 6, 12, 6), Margin = new Thickness(0, 0, 8, 6), Child = row,
        };
    }

    private void BuildQuests(RewardsEngine engine)
    {
        var ctx = engine.Context;
        var now = ctx.Now;
        _quests.Children.Clear();
        _quests.Margin = new Thickness(0, 10, 0, 0);
        void Section(string title)
        {
            _quests.Children.Add(new TextBlock { Text = title, Style = St("Section"), Margin = new Thickness(0, _quests.Children.Count == 0 ? 0 : 14, 0, 4) });
        }
        Section(L.T("امروز", "Today"));
        foreach (var q in RewardsCatalog.DailyFor(now)) _quests.Children.Add(QuestRow(q, ctx));
        Section(L.T("این هفته", "This week"));
        _quests.Children.Add(QuestRow(RewardsCatalog.WeeklyFor(now), ctx));
        var open = RewardsCatalog.Once.Where(q => !ctx.R.Claimed.Contains(q.Id)).ToList();
        if (open.Count > 0)
        {
            Section(L.T("یه‌بار برای همیشه", "One-time"));
            foreach (var q in open) _quests.Children.Add(QuestRow(q, ctx));
        }
    }

    private static Grid QuestRow(QuestDef q, RewardsContext ctx)
    {
        var claimed = ctx.R.Claimed.Contains(RewardsCatalog.ClaimKey(q, ctx.Now));
        var value = Math.Min(q.Value(ctx), q.Target);
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Badge(claimed ? "chk" : q.Kind == QuestKind.Weekly ? "medal" : "feather", claimed ? "Green" : "Accent", 38));
        var mid = new StackPanel { Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock { Text = q.Text, Style = St("H2"), FontSize = 14, TextWrapping = TextWrapping.Wrap });
        var bar = new ProgressBar { Style = St("Bar"), Minimum = 0, Maximum = q.Target, Value = claimed ? q.Target : value, Height = 6, Margin = new Thickness(0, 7, 0, 0) };
        mid.Children.Add(bar);
        Grid.SetColumn(mid, 1);
        grid.Children.Add(mid);
        var reward = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        reward.Children.Add(new TextBlock
        {
            Text = claimed ? L.T("انجام شد", "Done") : $"{Fa.N(value, 0)} / {Fa.N(q.Target, 0)}", Style = St("Label"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
            Foreground = claimed ? Res("Green") : Res("Muted"),
        });
        reward.Children.Add(new TextBlock { Text = $"+{Fa.N(q.Reward)}", FontWeight = FontWeights.Bold, Foreground = Res("Accent"), VerticalAlignment = VerticalAlignment.Center });
        reward.Children.Add(new Icon("feather", 16, Res("Accent"), 1.8) { Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(reward, 2);
        grid.Children.Add(reward);
        return grid;
    }

    private void BuildFilter()
    {
        Chips(_filterHost, new[] { (Filter.All, L.T("همه", "All")), (Filter.Earned, L.T("گرفته‌شده", "Earned")), (Filter.Locked, L.T("قفل‌شده", "Locked")) }, _filter, f =>
        {
            _filter = f;
            BuildMedals(Hub.Rewards);
        });
    }

    private void BuildMedals(RewardsEngine engine)
    {
        var ctx = engine.Context;
        _medals.Children.Clear();
        _count.Text = L.T($"{Fa.N(ctx.R.Medals.Count)} از {Fa.N(RewardsCatalog.Medals.Count)}", $"{ctx.R.Medals.Count} of {RewardsCatalog.Medals.Count}");
        _shopFeathers.Text = L.T($"الان {Fa.N(ctx.R.Feathers)} پَر داری.", $"You have {ctx.R.Feathers} feathers.");
        var fresh = new List<FrameworkElement>();
        foreach (var m in RewardsCatalog.Medals.OrderBy(m => m.Tier == MedalTier.Special ? 99 : (int)m.Tier))
        {
            var earned = ctx.R.Medals.ContainsKey(m.Id);
            if (_filter == Filter.Earned && !earned || _filter == Filter.Locked && earned) continue;
            var tile = Tile(m, earned, ctx);
            _medals.Children.Add(tile);
            if (_primed && _seen.Add(m.Id)) fresh.Add(tile);
            else _seen.Add(m.Id);
        }
        foreach (var t in fresh) Motion.Pop(t);
    }

    private FrameworkElement Tile(MedalDef m, bool earned, RewardsContext ctx)
    {
        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var glyph = new MedalGlyph(m, earned, 78) { HorizontalAlignment = HorizontalAlignment.Center, Opacity = earned ? 1 : 0.7 };
        body.Children.Add(glyph);
        var name = new TextBlock
        {
            Text = (!earned && m.Hidden ? "🔒 " : "") + m.Name, Style = St("H2"), FontSize = 14.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0),
            TextAlignment = TextAlignment.Center,
        };
        body.Children.Add(name);
        body.Children.Add(new TextBlock
        {
            Text = m.Desc, Style = St("Tip"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), MinHeight = 34,
        });
        var footer = new Grid { Margin = new Thickness(0, 8, 0, 0), Height = 22 };
        if (earned)
        {
            footer.Children.Add(new TextBlock { Text = m.TierName, Style = St("Tip"), FontWeight = FontWeights.SemiBold, Foreground = TierBrush(m.Tier), HorizontalAlignment = HorizontalAlignment.Center });
        }
        else
        {
            var (cur, target) = m.Progress(ctx);
            if (target > 1 && !m.Hidden)
            {
                footer.Children.Add(new ProgressBar { Style = St("Bar"), Minimum = 0, Maximum = target, Value = cur, Height = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0) });
            }
            else footer.Children.Add(new TextBlock { Text = m.TierName, Style = St("Tip"), Foreground = TierBrush(m.Tier), HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.7 });
        }
        body.Children.Add(footer);

        var card = new Border
        {
            Width = 172, Margin = new Thickness(6), Padding = new Thickness(12, 16, 12, 12), CornerRadius = new CornerRadius(18), Background = Res("Panel2"),
            BorderBrush = earned ? TierBrush(m.Tier) : Res("Line"), BorderThickness = new Thickness(1), Child = body, Opacity = earned ? 1 : 0.85, Cursor = earned ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
            ToolTip = earned ? L.T("بزن تا از این مدال یه تصویر برای اشتراک بسازی", "Click to make an image of this medal to share") : null,
        };
        if (earned) card.MouseLeftButtonUp += (_, _) => MakeCard(m, ctx.R.Medals[m.Id]);
        return card;
    }

    private static Brush TierBrush(MedalTier tier) => tier switch
    {
        MedalTier.Bronze => new SolidColorBrush(Color.FromRgb(0xF0, 0xA3, 0x5E)),
        MedalTier.Silver => new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xF0)),
        MedalTier.Gold => new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x6E)),
        MedalTier.Platinum => new SolidColorBrush(Color.FromRgb(0x9F, 0xF3, 0xFF)),
        MedalTier.Diamond => new SolidColorBrush(Color.FromRgb(0x8F, 0xB4, 0xFF)),
        MedalTier.Legendary => new SolidColorBrush(Color.FromRgb(0xFF, 0x9E, 0xE6)),
        _ => new SolidColorBrush(Color.FromRgb(0xBB, 0xA3, 0xFF)),
    };

    // ---- the share card -------------------------------------------------------------------------------------------

    private void MakeCard(MedalDef m, DateTime when)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Shabpa");
        var file = Path.Combine(folder, $"shabpa-medal-{m.Id}.png");
        if (!Ask(L.T("تصویر مدال ساخته بشه؟", "Make the medal image?"),
                L.T($"یه تصویر از مدال «{m.Name}» با آدرس shabpa.ir توی پوشه‌ی تصاویر، زیر Shabpa ذخیره می‌شه. چیزی جایی فرستاده نمی‌شه.",
                    $"An image of the “{m.Name}” medal with shabpa.ir is saved in your Pictures folder under Shabpa. Nothing is sent anywhere."),
                L.T("بساز", "Make it"), detail: file))
            return;
        try
        {
            Directory.CreateDirectory(folder);
            RenderShareCard(m, when, file);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
            Hub.Rewards.Count("shared");
            Hub.Notify(L.T("تصویر ساخته شد", "Image made"), file);
        }
        catch (Exception ex)
        {
            Log.Write($"medal card failed: {ex.Message}");
            Hub.Notify(L.T("تصویر ساخته نشد", "Could not make the image"), L.T("پوشه‌ی تصاویر قابل نوشتن نبود.", "The Pictures folder could not be written."));
        }
    }

    /// <summary>A 1080x1350 card (story / Telegram size) drawn off screen: the medal, its name, the date and the site.</summary>
    public static void RenderShareCard(MedalDef m, DateTime when, string path)
    {
        const double w = 540, h = 675;
        var bg = new LinearGradientBrush(Color.FromRgb(0x1B, 0x1E, 0x58), Color.FromRgb(0x0B, 0x0D, 0x26), new Point(0.2, 0), new Point(0.8, 1));
        var root = new Grid { Width = w, Height = h, Background = bg, FlowDirection = Ui.Flow };
        TextElement.SetFontFamily(root, (FontFamily)Application.Current.Resources["Ui"]);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = L.T("مدال جدید", "New medal"), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x6E)), HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new MedalGlyph(m, true, 250) { Margin = new Thickness(0, 22, 0, 22), HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = m.Name, FontSize = 40, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock
        {
            Text = m.Desc, FontSize = 18, Foreground = new SolidColorBrush(Color.FromRgb(0xB6, 0xBA, 0xE6)), Margin = new Thickness(40, 10, 40, 0), TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock { Text = $"{m.TierName} · {Fa.Date(when)}", FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x93, 0xC9)), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        root.Children.Add(stack);
        root.Children.Add(new TextBlock
        {
            Text = L.T("شبپا · shabpa.ir", "Shabpa · shabpa.ir"), FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Opacity = 0.9,
            VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 34),
        });
        root.Measure(new Size(w, h));
        root.Arrange(new Rect(0, 0, w, h));
        root.UpdateLayout();
        var bmp = new RenderTargetBitmap(1080, 1350, 192, 192, PixelFormats.Pbgra32);
        bmp.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private bool Ask(string title, string message, string yes, string? detail = null) => ConfirmWindow.Ask(title, message, yes, detail: detail);
}
