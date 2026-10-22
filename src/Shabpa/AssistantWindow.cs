using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shabpa.Core;

namespace Shabpa;

/// <summary>The command box: type «یه ساعت دیگه خاموش کن» or "remind me at 5pm to call mom". The parser runs offline;
/// nothing runs until the card with the exact time is confirmed, and unclear text gets choices instead of a guess.
/// Bidar the owl shows what is going on (thinking, a question, done).</summary>
public sealed class AssistantWindow : Window
{
    private static AssistantWindow? _open;

    private readonly TextBox _input;
    private readonly StackPanel _result = new() { Margin = new Thickness(0, 14, 0, 0) };
    private readonly Image _owl = new() { Width = 64, Height = 64, Margin = new Thickness(0, 0, 12, 0) };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

    /// <summary>Opens the box, or brings the open one to the front.</summary>
    public static void Open()
    {
        if (_open is { } w)
        {
            w.Activate();
            w._input.Focus();
            return;
        }
        _open = new AssistantWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    private AssistantWindow()
    {
        FlowDirection = Ui.Flow;
        Title = L.T("فرمان متنی شب‌پا", "Shabpa command box");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.SetResourceReference(BackgroundProperty, "Bg");
        FontFamily = (FontFamily)Application.Current.Resources["Ui"];
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        UseLayoutRounding = true;
        SourceInitialized += (_, _) => Ui.Chrome(this);

        _input = new TextBox { Style = (Style)Application.Current.Resources["Input"], FontSize = 15, MaxLength = CommandText.MaxLength, Margin = new Thickness(0, 14, 0, 0) };
        _input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Understand();
        };

        var header = new DockPanel();
        DockPanel.SetDock(_owl, Dock.Left);
        header.Children.Add(_owl);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Themed(new TextBlock { Text = L.T("چی کار کنم؟", "What should I do?"), FontSize = 19, FontWeight = FontWeights.Bold }, "Text"));
        _hint.Style = (Style)Application.Current.Resources["Tip"];
        titles.Children.Add(_hint);
        header.Children.Add(titles);

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(_input);
        panel.Children.Add(_result);

        var sheet = new Kit.GlassCard
        {
            CornerRadius = new CornerRadius(22), Padding = new Thickness(24, 22, 24, 22),
            Margin = new Thickness(18), Child = panel,
        };
        sheet.SetResourceReference(Control.BackgroundProperty, "FlyoutFill");
        var wall = new Border();
        wall.SetResourceReference(Border.BackgroundProperty, "Wallpaper");
        var veil = new Border { Opacity = 0.45 };
        veil.SetResourceReference(Border.BackgroundProperty, "Bg");
        var root = new Grid();
        root.Children.Add(wall);
        root.Children.Add(veil);
        root.Children.Add(sheet);
        Content = root;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) => _input.Focus();
        Idle();
    }

    private static readonly Dictionary<string, ImageSource?> OwlCache = [];

    private void Owl(string mood)
    {
        if (!OwlCache.TryGetValue(mood, out var img))
        {
            try
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource = new Uri($"pack://application:,,,/Assets/bidar/{mood}.png");
                b.DecodePixelWidth = 128; // shown at 64 DIP: keep the decoded picture small
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.EndInit();
                b.Freeze();
                img = b;
            }
            catch { img = null; } // the box works without the picture
            OwlCache[mood] = img;
        }
        _owl.Source = img;
    }

    private void Idle()
    {
        Owl("idle");
        _hint.Text = L.T("مثلاً «ساعت ۲ شب خاموش کن»، «وقتی دانلود تموم شد بخوابون»، «نیم ساعت دیگه یادم بنداز چای رو بردارم». Enter برای فهمیدن، Esc برای بستن.",
            "For example “shut down at 2am”, “sleep when the download is done”, “remind me in 30 min to check the tea”. Enter to read it, Esc to close.");
        _result.Children.Clear();
    }

    private TextBlock Line(string text, string brush = "Text", double size = 14, FontWeight? weight = null) => Themed(new TextBlock()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size, FontWeight = weight ?? FontWeights.Normal,
        LineHeight = 24, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Margin = new Thickness(0, 4, 0, 4),
    }, brush);

    private TextBlock Themed(TextBlock t, string brush)
    {
        t.SetResourceReference(TextBlock.ForegroundProperty, brush); // follows a theme change while the box is open
        return t;
    }

    private Button Btn(string text, string style, Action click)
    {
        var b = new Button { Content = text, Style = (Style)Application.Current.Resources[style], Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        b.Click += (_, _) => click();
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
                Owl("happy");
                _result.Children.Add(Line(Hub.I.ScheduleText(DateTime.Now)));
                break;
            case CommandStatus.Ready when result.Command is { } command:
                Confirm(command);
                break;
            default:
                Owl(result.Status == CommandStatus.Unknown ? "confused" : "think");
                _result.Children.Add(Line(result.Question, "Text", 14.5, FontWeights.SemiBold));
                foreach (var choice in result.Choices)
                {
                    var c = choice;
                    _result.Children.Add(Btn(c.Summary(DateTime.Now), "Btn", () => Confirm(c)));
                }
                break;
        }
    }

    /// <summary>The confirmation card: the exact time in words, then a confirm button. Power actions use the danger style.</summary>
    private void Confirm(AssistantCommand command)
    {
        Owl("think");
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
    }

    private void Run(AssistantCommand command)
    {
        var reply = Hub.I.RunAssistant(command, "box");
        _result.Children.Clear();
        Owl(reply.Ok ? "happy" : "worried");
        _result.Children.Add(Line(L.En ? reply.En : reply.Fa, reply.Ok ? "Text" : "Red"));
        if (reply.Ok) _input.Clear();
        _input.Focus();
    }
}

/// <summary>The global shortcut that opens the command box from anywhere (Ctrl+Alt+Space unless changed in Settings).</summary>
public sealed class AssistantHotkey : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private const int Id = 0xC101;
    private const uint ModNoRepeat = 0x4000;
    private readonly HwndSource? _sink;
    private string _key = "";
    private bool _registered;

    public string? Failed { get; private set; }

    public AssistantHotkey()
    {
        try
        {
            _sink = new HwndSource(new HwndSourceParameters("ShabpaAssistant") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
            _sink.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == 0x0312 && wParam.ToInt32() == Id)
                {
                    handled = true;
                    AssistantWindow.Open();
                }
                return IntPtr.Zero;
            });
        }
        catch { /* no shortcut; the tray item still opens the box */ }
    }

    /// <summary>Registers <paramref name="text"/> (empty = no shortcut); a no-op when it did not change.</summary>
    public void Apply(string text)
    {
        if (_sink is null || (_registered && text == _key)) return;
        if (_registered) UnregisterHotKey(_sink.Handle, Id);
        _key = text;
        _registered = true;
        Failed = null;
        if (Hotkey.Parse(text) is { } key && !RegisterHotKey(_sink.Handle, Id, key.Modifiers | ModNoRepeat, key.VirtualKey))
            Failed = key.ToString();
    }

    public void Dispose()
    {
        if (_sink is null) return;
        if (_registered) UnregisterHotKey(_sink.Handle, Id);
        _sink.Dispose();
    }
}
