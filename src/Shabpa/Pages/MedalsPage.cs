using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>The medal collection: every medal with how to earn it, the earned ones in colour with their date, locked ones
/// grey, secret ones a "?" until found. Everything is judged on this PC; nothing is sent.</summary>
public sealed class MedalsPage : PageBase
{
    private readonly StackPanel _root = new();
    private readonly TextBlock _count = new() { FontSize = 30, FontWeight = FontWeights.Bold };
    private readonly TextBlock _countSub = new() { Style = St("Label"), Margin = new Thickness(0, 2, 0, 0) };
    private readonly Border _progressFill = new() { Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Grid _progress = new() { Height = 8, Margin = new Thickness(0, 12, 0, 0) };
    private readonly WrapPanel _grid = new() { Margin = new Thickness(-6, 4, -6, 0) };
    private readonly WrapPanel _filter = new() { Margin = new Thickness(0, 14, 0, 4) };
    private string _show = "all";

    public override string PageTitle => L.T("مدال‌ها", "Medals");
    public override string PageSubtitle => L.T("با کار کردن با شبپا مدال جمع کن. همه‌چی همین‌جا روی کامپیوترت حساب می‌شه.", "Collect medals as you use Shabpa. Everything is counted right here on your PC.");

    public MedalsPage()
    {
        var (card, body) = K.Card();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        _count.Foreground = Res("Text");
        left.Children.Add(_count);
        left.Children.Add(_countSub);
        top.Children.Add(left);
        var preview = new Button { Style = St("Btn"), Content = L.T("نمایش نمونه‌ی پاپ‌آپ", "Preview the pop-up"), VerticalAlignment = VerticalAlignment.Center };
        preview.Click += (_, _) =>
        {
            var earned = Medals.All.Where(m => Hub.MedalBook.Earned.ContainsKey(m.Id)).ToList();
            MedalToast.Enqueue(earned.Count > 0 ? earned[^1] : Medals.All[0]);
        };
        Grid.SetColumn(preview, 1);
        top.Children.Add(preview);
        body.Children.Add(top);
        _progress.Children.Add(new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = Res("Line") });
        _progressFill.Background = new LinearGradientBrush(Color.FromRgb(0xFF, 0xE2, 0xA0), Color.FromRgb(0xF7, 0xA9, 0x3B), 0);
        _progress.Children.Add(_progressFill);
        body.Children.Add(_progress);
        body.Children.Add(_filter);
        _root.Children.Add(card);
        _root.Children.Add(_grid);
        Content = _root;
        While<Action>(h => Hub.MedalsChanged += h, h => Hub.MedalsChanged -= h, Refresh);
        _progress.SizeChanged += (_, _) => SizeBar();
    }

    private void SizeBar()
    {
        var total = Medals.All.Count;
        var got = Medals.All.Count(m => Hub.MedalBook.Earned.ContainsKey(m.Id));
        _progressFill.Width = total == 0 ? 0 : _progress.ActualWidth * got / total;
    }

    public override void Refresh()
    {
        var earned = Hub.MedalBook.Earned;
        var total = Medals.All.Count;
        var got = Medals.All.Count(m => earned.ContainsKey(m.Id));
        _count.Text = L.T($"{Fa.Digits(got.ToString())} از {Fa.Digits(total.ToString())}", $"{got} of {total}");
        var gold = Medals.All.Count(m => earned.ContainsKey(m.Id) && m.Tier is MedalTier.Gold or MedalTier.Special);
        _countSub.Text = L.T($"مدال گرفتی · {Fa.Digits(gold.ToString())} طلایی و ویژه · {Fa.Digits(Hub.MedalBook.Days.ToString())} روز با شبپا",
            $"medals earned · {gold} gold and special · {Hub.MedalBook.Days} days with Shabpa");
        SizeBar();
        Chips(_filter, [("all", L.T("همه", "All")), ("got", L.T("گرفته‌شده", "Earned")), ("locked", L.T("قفل", "Locked"))], _show, v => { _show = v; Refresh(); });

        _grid.Children.Clear();
        // earned first (newest first), then the locked ones in catalogue order
        var list = Medals.All
            .Select((m, i) => (m, i, at: earned.TryGetValue(m.Id, out var t) ? t : (DateTime?)null))
            .Where(x => _show == "all" || (_show == "got") == x.at.HasValue)
            .OrderBy(x => x.at.HasValue ? 0 : 1).ThenByDescending(x => x.at).ThenBy(x => x.i);
        foreach (var (m, _, at) in list) _grid.Children.Add(Tile(m, at));
    }

    private static FrameworkElement Tile(Medal m, DateTime? at)
    {
        var got = at.HasValue;
        var hidden = m.Secret && !got;
        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        body.Children.Add(new MedalArt(m, got, 76) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 8) });
        body.Children.Add(new TextBlock
        {
            Text = hidden ? L.T("مدال مخفی", "Secret medal") : m.Name, FontSize = 14, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center,
            Foreground = got ? Res("Text") : Res("Muted"), TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock
        {
            Text = hidden ? L.T("وقتی پیداش کنی معلوم می‌شه.", "Revealed when you find it.") : m.How, Style = St("Label"), FontSize = 11.5,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), MinHeight = 32,
        });
        var foot = new TextBlock
        {
            FontSize = 11, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0),
            Text = got ? Fa.Date(at!.Value) : TierName(m.Tier),
            Foreground = got ? new SolidColorBrush(MedalArt.TierColor(m.Tier)) : Res("Muted"),
        };
        body.Children.Add(foot);
        return new GlassCard { Width = 168, Margin = new Thickness(6), Child = body, Opacity = got ? 1 : 0.8 };
    }

    private static string TierName(MedalTier t) => t switch
    {
        MedalTier.Bronze => L.T("برنزی", "Bronze"),
        MedalTier.Silver => L.T("نقره‌ای", "Silver"),
        MedalTier.Gold => L.T("طلایی", "Gold"),
        _ => L.T("ویژه", "Special"),
    };
}
