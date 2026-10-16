using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>"Rate Shabpa": one to five stars. Nothing is sent anywhere: the choice is kept on this PC, and the card can
/// open the project's GitHub page (to leave a real star) or its issue page (for a written opinion).</summary>
public sealed class RatingCard : Border
{
    private const string RepoUrl = "https://github.com/" + GitHubIssue.Repo;

    private readonly List<TextBlock> _stars = [];
    private readonly TextBlock _state = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
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
        Style = (Style)Application.Current.Resources["Card"];
        Margin = new Thickness(0, 12, 0, 0);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = L.T("به شب‌پا امتیاز بده", "Rate Shabpa"), Style = (Style)Application.Current.Resources["H2"] });
        body.Children.Add(new TextBlock
        {
            Text = L.T("از یک تا پنج ستاره. امتیازت فقط روی همین کامپیوتر ذخیره می‌شه و چیزی جایی فرستاده نمی‌شه. برای ثبت واقعی می‌تونی توی گیت‌هاب ستاره بدی.",
                "One to five stars. Your choice stays on this PC and nothing is sent. For a public one you can star the project on GitHub."),
            Style = (Style)Application.Current.Resources["Body"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        for (var i = 1; i <= 5; i++)
        {
            var value = i;
            var star = new TextBlock
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 30, Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
            };
            star.MouseEnter += (_, _) => { Paint(value); Grow(star, 1.18); };
            star.MouseLeave += (_, _) => { Paint(Hub.I.Settings.Rating); Grow(star, 1); };
            star.MouseLeftButtonUp += (_, _) => Choose(value);
            _stars.Add(star);
            row.Children.Add(star);
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

    private static void Grow(TextBlock star, double to)
    {
        if (!Motion.Enabled || star.RenderTransform is not ScaleTransform scale) return;
        var animation = new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(130));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private void Paint(int filled)
    {
        for (var i = 0; i < _stars.Count; i++)
        {
            _stars[i].Text = i < filled ? "" : ""; // filled star, outline star
            _stars[i].Foreground = (Brush)Application.Current.Resources[i < filled ? "Gold" : "Faint"];
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
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write($"open url: {ex.GetType().Name}"); }
    }
}
