using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>"Rate Shabpa": one to five stars. Nothing is sent anywhere: the choice is kept on this PC, and the card can
/// open the project's GitHub page (to leave a real star) or its issue page (for a written opinion).</summary>
public sealed class RatingCard : GlassCard
{
    private const string RepoUrl = "https://github.com/" + GitHubIssue.Repo;

    private readonly List<System.Windows.Shapes.Path> _stars = [];
    private readonly TextBlock _state = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly WrapPanel _actions = new() { Margin = new Thickness(0, 10, 0, 0) };

    /// <summary>The line shown in the confirmation: playful, never rude, and for a low score it nudges toward a higher one.</summary>
    internal static string Joke(int stars) => stars switch
    {
        5 => L.T("پنج ستاره؟! دمت گرم. یه قهوه‌ی مجازی برای سازنده حساب شد ☕", "Five stars?! You're the best. That's a virtual coffee for the developer ☕"),
        4 => L.T("چهار ستاره، تقریباً عالی! یه ستاره‌ی گمشده چی کم داشت؟ بگو تا درستش کنم 😄", "Four stars, almost perfect! What was the missing star's problem? Tell me and I'll fix it 😄"),
        3 => L.T("سه ستاره؟ یعنی «بد نبود»؟ 😅 یه نگاه دیگه به این همه تم و گزینه بنداز، شاید نظرت عوض شد.", "Three stars? As in “not bad”? 😅 Take another look at all those themes and options, maybe you'll change your mind."),
        2 => L.T("دو ستاره؟ این بود جواب این همه گزینه و تم و انیمیشن؟ 😄 من که باور نمی‌کنم، یه فرصت دیگه بده!", "Two stars? That's the answer to all these options, themes and animations? 😄 I refuse to believe it, give me another chance!"),
        _ => L.T("یه ستاره؟! شب‌پا رفت یه گوشه نشست و داره با خودش حرف می‌زنه 🥲 اگه بگی چی اذیتت کرد، قول می‌دم درستش کنم.", "One star? Shabpa went to sit in a corner and is talking to itself 🥲 Tell me what bothered you and I promise to fix it."),
    };

    internal static string Reply(int stars) => stars >= 4
        ? L.T("ممنون! اگه دوست داشتی، یه ستاره‌ی واقعی هم توی گیت‌هاب بده.", "Thank you! If you like, leave a real star on GitHub too.")
        : stars == 3
            ? L.T("ممنون از صداقتت. بگو چی رو بهتر کنم؟", "Thanks for being honest. What should I improve?")
            : L.T("ممنون که رک گفتی. اگه بگی چی خراب بود، درستش می‌کنم.", "Thanks for saying it straight. Tell me what was broken and I'll fix it.");

    public RatingCard()
    {
        Margin = new Thickness(0);
        Padding = new Thickness(22, 20, 22, 18);
        var body = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.Children.Add(new IconBadge("star", "HueC", 38, 19, 12) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 14, 0) });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = L.T("به شب‌پا امتیاز بده", "Rate Shabpa"), Style = (Style)Application.Current.Resources["H2"] });
        titles.Children.Add(new TextBlock
        {
            Text = L.T("از یک تا پنج ستاره. امتیازت فقط روی همین کامپیوتر ذخیره می‌شه و چیزی جایی فرستاده نمی‌شه. برای ثبت واقعی می‌تونی توی گیت‌هاب ستاره بدی.",
                "One to five stars. Your choice stays on this PC and nothing is sent. For a public one you can star the project on GitHub."),
            Style = (Style)Application.Current.Resources["Body"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
        });
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);
        body.Children.Add(head);
        var row = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        var geometry = (Geometry)Application.Current.Resources["I.star"];
        for (var i = 1; i <= 5; i++)
        {
            var value = i;
            var star = new System.Windows.Shapes.Path
            {
                Data = geometry, Width = 32, Height = 32, Stretch = Stretch.Uniform, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Margin = new Thickness(4), IsHitTestVisible = false,
            };
            // a transparent hit box around the star, so the hover is steady and the spring grows the whole thing
            var host = new Border { Background = Application.Current.Resources["Transparent"] as Brush, Cursor = Cursors.Hand, Child = star, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 4, 0) };
            Fx.Attach(host, grow: 1.18, press: 0.9);
            host.MouseEnter += (_, _) => Paint(value);
            host.MouseLeave += (_, _) => Paint(Hub.I.Settings.Rating);
            host.MouseLeftButtonUp += (_, _) => Choose(value);
            _stars.Add(star);
            row.Children.Add(host);
        }
        body.Children.Add(row);
        _state.Style = (Style)Application.Current.Resources["Label"];
        body.Children.Add(_state);

        var github = new Button { Style = (Style)Application.Current.Resources["Btn"], Content = L.T("ستاره در گیتهاب ⭐", "Star on GitHub ⭐"), Margin = new Thickness(0, 0, 8, 6) };
        github.Click += (_, _) => Open(RepoUrl);
        var feedback = new Button { Style = (Style)Application.Current.Resources["Btn"], Content = L.T("نظر یا پیشنهاد بنویس", "Write feedback"), Margin = new Thickness(0, 0, 8, 6) };
        feedback.Click += (_, _) => Open(GitHubIssue.NewIssueUrl(L.T("نظر درباره‌ی شب‌پا", "Feedback about Shabpa"), Hub.I.Settings.Rating > 0 ? $"{Hub.I.Settings.Rating}/5\n\n" : "", "idea"));
        _actions.Children.Add(github);
        _actions.Children.Add(feedback);
        body.Children.Add(_actions);
        Child = body;
        Refresh();
    }

    private void Paint(int filled)
    {
        for (var i = 0; i < _stars.Count; i++)
        {
            var on = i < filled;
            var brush = (Brush)Application.Current.Resources[on ? "Gold" : "Faint"];
            _stars[i].Stroke = brush;
            _stars[i].Fill = on ? brush : null;
        }
    }

    public void Refresh()
    {
        Paint(Hub.I.Settings.Rating);
        _state.Text = Hub.I.Settings.Rating > 0
            ? L.T($"امتیاز تو: {Fa.Digits(Hub.I.Settings.Rating.ToString())} از ۵. برای عوض کردنش روی یه ستاره‌ی دیگه بزن.", $"Your rating: {Hub.I.Settings.Rating} of 5. Click another star to change it.")
            : L.T("هنوز امتیازی ندادی.", "You have not rated yet.");
    }

    private void Choose(int stars)
    {
        var title = L.T($"{Fa.Digits(stars.ToString())} ستاره ثبت بشه؟", $"Give {stars} star{(stars == 1 ? "" : "s")}?");
        if (!ConfirmWindow.AskAlways(title, Joke(stars), L.T("آره، همینو ثبت کن", "Yes, rate it"), stars >= 4 ? L.T("نه، صبر کن", "No, wait") : L.T("نه، یه بار دیگه فکر می‌کنم", "No, let me think again")))
        {
            Paint(Hub.I.Settings.Rating);
            return;
        }
        Hub.I.Settings.Rating = stars;
        Hub.I.Settings.RatedAt = DateTime.Now;
        Hub.I.Store.Save();
        Refresh();
        _state.Text += "  " + Reply(stars);
        Celebrate(stars);
    }

    /// <summary>The chosen stars spring in one after another, then the reply line rises in.</summary>
    private void Celebrate(int stars)
    {
        if (!Motion.Enabled) return;
        for (var i = 0; i < stars && i < _stars.Count; i++)
        {
            var star = _stars[i];
            var scale = new ScaleTransform(0.5, 0.5);
            star.RenderTransformOrigin = new Point(0.5, 0.5);
            star.RenderTransform = scale;
            var grow = new System.Windows.Media.Animation.DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(650))
            {
                EasingFunction = SpringEase.I, BeginTime = TimeSpan.FromMilliseconds(i * 70), FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd,
            };
            grow.Completed += (_, _) =>
            {
                // settled: drop the clock and the transform
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                star.RenderTransform = Transform.Identity;
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
        Motion.Rise(_state, 0, 8, 0.45);
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write($"open url: {ex.GetType().Name}"); }
    }
}
