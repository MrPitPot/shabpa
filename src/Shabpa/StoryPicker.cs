using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa;

/// <summary>«با کدوم طرح ذخیره بشه؟»: the ten story designs of a medal side by side. Pick one, then save the 1080×1920 picture
/// into Pictures\Shabpa (and show it in Explorer) or copy it. Nothing is sent anywhere, and nothing is written until the
/// person presses the button.</summary>
public sealed class StoryPicker : Window
{
    public static void Open(MedalDef medal, DateTime when)
    {
        var picker = new StoryPicker(medal, when);
        if (Application.Current?.MainWindow is { IsVisible: true } main) picker.Owner = main;
        picker.Show();
    }

    /// <summary>Test aid (--picker-test): the window as it looks, drawn off screen into a PNG; nothing is shown.</summary>
    internal static void Preview(MedalDef medal, string path)
    {
        var picker = new StoryPicker(medal, DateTime.Now);
        var content = (FrameworkElement)picker.Content;
        picker.Content = null;
        var holder = new Border { Width = 940, Height = 740, Background = (Brush)Application.Current.Resources["Bg"], Child = content, FlowDirection = Ui.Flow };
        TextElement.SetFontFamily(holder, (FontFamily)Application.Current.Resources["Ui"]);
        holder.Measure(new Size(940, 740));
        holder.Arrange(new Rect(0, 0, 940, 740));
        holder.UpdateLayout();
        var bmp = new RenderTargetBitmap(940, 740, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(holder);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private readonly MedalDef _medal;
    private readonly DateTime _when;
    private readonly List<(StoryDesign Design, Border Frame)> _tiles = [];
    private readonly TextBlock _status = new() { Style = (Style)Application.Current.Resources["Tip"], TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private StoryDesign _selected = MedalStory.Designs[0];

    private StoryPicker(MedalDef medal, DateTime when)
    {
        _medal = medal;
        _when = when;
        FlowDirection = Ui.Flow;
        Title = L.T("شب‌پا", "Shabpa");
        Width = 960;
        Height = 780;
        MinWidth = 640;
        MinHeight = 520;
        ShowInTaskbar = false;
        WindowStartupLocation = Application.Current?.MainWindow is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        Background = (Brush)Application.Current!.Resources["Bg"];
        FontFamily = (FontFamily)Application.Current.Resources["Ui"];
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        SourceInitialized += (_, _) => Ui.Chrome(this);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var root = new Grid { Margin = new Thickness(26, 22, 26, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = L.T("با کدوم طرح ذخیره بشه؟", "Which design do you want?"), FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Res("Text") });
        head.Children.Add(new TextBlock
        {
            Text = L.T($"مدال «{medal.Name}» رو به‌شکل استوری (۱۰۸۰×۱۹۲۰) می‌سازم. یکی از طرح‌ها رو انتخاب کن.", $"The “{medal.Name}” medal becomes a story picture (1080×1920). Pick one of the designs."),
            Style = (Style)Application.Current.Resources["Body"], Margin = new Thickness(0, 4, 0, 14), TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(head);

        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var data = MedalStory.DataFor(medal, when);
        foreach (var design in MedalStory.Designs) wrap.Children.Add(Tile(design, data));
        var scroll = new ScrollViewer { Content = wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var bar = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = true };
        var save = new Button { Content = L.T("ذخیره‌ی تصویر", "Save the picture"), Style = (Style)Application.Current.Resources["BtnPrimary"], MinWidth = 150, Margin = new Thickness(0, 0, 0, 0) };
        var copy = new Button { Content = L.T("کپی", "Copy"), Style = (Style)Application.Current.Resources["Btn"], Margin = new Thickness(10, 0, 0, 0) };
        var close = new Button { Content = L.T("بستن", "Close"), Style = (Style)Application.Current.Resources["Btn"], Margin = new Thickness(10, 0, 0, 0) };
        save.Click += (_, _) => Save();
        copy.Click += (_, _) => Copy();
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(save);
        buttons.Children.Add(copy);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        bar.Children.Add(_status);
        Grid.SetRow(bar, 2);
        root.Children.Add(bar);
        Content = root;
        Select(MedalStory.Designs[0]);
    }

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private FrameworkElement Tile(StoryDesign design, StoryData data)
    {
        FrameworkElement card;
        try { card = MedalStory.Build(design.Key, data); }
        catch (Exception ex)
        {
            Log.Write($"story {design.Key}: {ex.Message}");
            card = new Border { Width = MedalStory.W, Height = MedalStory.H, Background = Res("Panel") };
        }
        var view = new Viewbox { Width = 150, Height = 150 * MedalStory.H / MedalStory.W, Child = card, Stretch = Stretch.Uniform };
        var frame = new Border
        {
            Child = view, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(2.5), BorderBrush = Brushes.Transparent, Padding = new Thickness(3), Cursor = Cursors.Hand, Background = Brushes.Transparent,
            ToolTip = design.Hint,
        };
        frame.MouseLeftButtonUp += (_, _) => Select(design);
        frame.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { Select(design); Save(); } };
        _tiles.Add((design, frame));
        var label = new TextBlock { Text = $"{Fa.N(Array.IndexOf(MedalStory.Designs, design) + 1)} · {design.Name}", Style = (Style)Application.Current.Resources["Label"], HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), TextAlignment = TextAlignment.Center };
        var cell = new StackPanel { Margin = new Thickness(8, 6, 8, 10), Children = { frame, label } };
        return cell;
    }

    private void Select(StoryDesign design)
    {
        _selected = design;
        foreach (var (d, frame) in _tiles)
        {
            var on = d == design;
            frame.BorderBrush = on ? Res("Accent") : Brushes.Transparent;
            frame.Background = on ? Res("AccentSoft") : Brushes.Transparent;
        }
        _status.Text = design.Hint;
    }

    private BitmapSource Render()
    {
        var card = MedalStory.Build(_selected.Key, MedalStory.DataFor(_medal, _when));
        return MedalStory.Render(card);
    }

    private void Save()
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Shabpa");
            var file = Path.Combine(folder, $"shabpa-story-{_medal.Id}-{_selected.Key}.png");
            Directory.CreateDirectory(folder);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(Render()));
            using (var fs = File.Create(file)) enc.Save(fs);
            Hub.I.Rewards.Count("shared");
            Hub.I.Notify(L.T("تصویر ساخته شد", "Picture saved"), file);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
            Close();
        }
        catch (Exception ex)
        {
            Log.Write($"story save failed: {ex.Message}");
            _status.Text = L.T("ذخیره نشد؛ پوشه‌ی تصاویر قابل نوشتن نبود.", "Could not save: the Pictures folder could not be written.");
        }
    }

    private void Copy()
    {
        try
        {
            Clipboard.SetImage(Render());
            Hub.I.Rewards.Count("shared");
            _status.Text = L.T("کپی شد؛ توی تلگرام یا اینستاگرام Paste کن.", "Copied: paste it in Telegram or Instagram.");
        }
        catch (Exception ex)
        {
            Log.Write($"story copy failed: {ex.Message}");
            _status.Text = L.T("کپی نشد؛ دوباره امتحان کن.", "Could not copy; try again.");
        }
    }
}
