using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shabpa.Core;

namespace Shabpa;

/// <summary>The typing part of the command box (hint, text field, result card), shared by the stand-alone window and the
/// speech bubble next to Bidar in the main window. The parser runs offline; nothing runs until the card with the exact time
/// is confirmed, and unclear text gets choices instead of a guess. Bidar's mood is reported through <see cref="Mood"/>.</summary>
public sealed class AssistantPanel : StackPanel
{
    private readonly TextBox _input;
    private readonly StackPanel _result = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap };

    /// <summary>Bidar should look like this now.</summary>
    public event Action<BidarMood>? Mood;
    /// <summary>A reply was written (Bidar can talk).</summary>
    public event Action? Spoke;
    /// <summary>The person typed something or pressed a button (Bidar is not asleep).</summary>
    public event Action? Activity;

    public AssistantPanel()
    {
        FlowDirection = Ui.Flow;
        _hint.Style = (Style)Application.Current.Resources["Tip"];
        _input = new TextBox { Style = (Style)Application.Current.Resources["Input"], FontSize = 15, MaxLength = CommandText.MaxLength, Margin = new Thickness(0, 10, 0, 0) };
        _input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Understand();
        };
        _input.TextChanged += (_, _) => Activity?.Invoke();
        Children.Add(_hint);
        Children.Add(_input);
        Children.Add(_result);
        Idle();
    }

    public void FocusInput() => _input.Focus();

    /// <summary>Test aid: types <paramref name="text"/> and presses Enter.</summary>
    internal void Submit(string text)
    {
        _input.Text = text;
        Understand();
    }

    /// <summary>Back to the empty state (hint only).</summary>
    public void Reset()
    {
        _input.Clear();
        Idle();
    }

    private void Idle()
    {
        Mood?.Invoke(BidarMood.Idle);
        _hint.Text = L.T("مثلاً «ساعت ۲ شب خاموش کن»، «وقتی دانلود تموم شد بخوابون»، «نیم ساعت دیگه یادم بنداز چای رو بردارم». Enter برای فهمیدن.",
            "For example “shut down at 2am”, “sleep when the download is done”, “remind me in 30 min to check the tea”. Enter to read it.");
        _result.Children.Clear();
    }

    private static TextBlock Line(string text, string brush = "Text", double size = 14, FontWeight? weight = null)
    {
        var t = new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size, FontWeight = weight ?? FontWeights.Normal,
            LineHeight = 24, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Margin = new Thickness(0, 4, 0, 4),
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush); // follows a theme change while the box is open
        return t;
    }

    private Button Btn(string text, string style, Action click)
    {
        var b = new Button { Content = text, Style = (Style)Application.Current.Resources[style], Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        b.Click += (_, _) => { Activity?.Invoke(); click(); };
        return b;
    }

    private void Understand()
    {
        var text = _input.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        var result = CommandParser.Parse(text, DateTime.Now);
        Log.Write($"assistant parse: {result.Status} {result.Command?.Intent} conf={result.Confidence:0.00}"); // never the text itself: it can be personal
        _result.Children.Clear();
        switch (result.Status)
        {
            case CommandStatus.Ready when result.Command is { Intent: CommandIntent.ListSchedules }:
                // read-only: nothing to confirm
                Mood?.Invoke(BidarMood.Happy);
                _result.Children.Add(Line(Hub.I.ScheduleText(DateTime.Now)));
                Spoke?.Invoke();
                break;
            case CommandStatus.Ready when result.Command is { } command:
                Confirm(command);
                break;
            default:
                Mood?.Invoke(result.Status == CommandStatus.Unknown ? BidarMood.Confused : BidarMood.Think);
                _result.Children.Add(Line(result.Question, "Text", 14.5, FontWeights.SemiBold));
                foreach (var choice in result.Choices)
                {
                    var c = choice;
                    _result.Children.Add(Btn(c.Summary(DateTime.Now), "Btn", () => Confirm(c)));
                }
                Spoke?.Invoke();
                break;
        }
    }

    /// <summary>The confirmation card: the exact time in words, then a confirm button. Power actions use the danger style.</summary>
    private void Confirm(AssistantCommand command)
    {
        Mood?.Invoke(BidarMood.Think);
        _result.Children.Clear();
        var now = DateTime.Now;
        var danger = command.Intent == CommandIntent.Power && command.Action is not PowerAction.Lock;
        var box = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 12),
            Child = Line(command.Summary(now), danger ? "Red" : "Accent", 14.5, FontWeights.SemiBold),
        };
        box.SetResourceReference(Border.BackgroundProperty, danger ? "HueESoft" : "HueASoft");
        _result.Children.Add(box);
        if (command.Defaulted)
            _result.Children.Add(Line(L.T("ساعت دقیق رو نگفتی؛ این ساعت رو گذاشتم. اگه درست نیست، دقیق‌ترش رو بنویس.", "You did not give an exact time, so I picked this one. If it is wrong, type it more exactly."), "Muted", 12.5));
        if (command.Intent == CommandIntent.Wake)
            _result.Children.Add(Line(L.T("سیستم فقط از حالت خواب یا هایبرنیت بیدار می‌شه، نه از خاموشی کامل.", "The PC can only wake from sleep or hibernation, not from fully off."), "Muted", 12.5));
        _result.Children.Add(Btn(L.T("تأیید", "Confirm"), danger ? "BtnDanger" : "BtnPrimary", () => Run(command)));
        _result.Children.Add(Btn(L.T("نه، بی‌خیال", "No, never mind"), "Btn", Idle));
        Spoke?.Invoke();
    }

    private void Run(AssistantCommand command)
    {
        var reply = Hub.I.RunAssistant(command, "box");
        _result.Children.Clear();
        Mood?.Invoke(reply.Ok ? BidarMood.Happy : BidarMood.Worried);
        _result.Children.Add(Line(L.En ? reply.En : reply.Fa, reply.Ok ? "Text" : "Red"));
        if (reply.Ok) _input.Clear();
        _input.Focus();
        Spoke?.Invoke();
    }
}
