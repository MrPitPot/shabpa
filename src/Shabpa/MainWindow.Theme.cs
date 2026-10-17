using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Shabpa.Core;

namespace Shabpa;

/// <summary>Live colour-theme change with the "Telegram" reveal: the old look is frozen into a picture that lies over the
/// window while the theme underneath is swapped; a circle grows from the theme button and uncovers the new look.
/// Only transform/clip work, a transient bitmap, no restart.</summary>
public partial class MainWindow
{
    private Image? _reveal;
    private EllipseGeometry? _hole;
    private int _revealTicket;

    /// <summary>Test aid: true while the old-look picture is still over the window.</summary>
    public bool RevealRunning => _reveal is not null;

    /// <summary>Test aid: the radius of the growing circle (0 when none).</summary>
    public double RevealRadius => _hole?.RadiusX ?? 0;

    /// <summary>Switches to theme <paramref name="key"/> right now and saves the choice. The new look grows out of
    /// <paramref name="origin"/> (the theme button) unless animations are off.</summary>
    public void SwitchTheme(string key, FrameworkElement? origin = null)
    {
        var next = Palette.Find(key);
        if (next.Key == Ui.Theme.Key) return;
        FinishReveal();
        Hub.I.Settings.Theme = next.Key;
        Hub.I.Store.Save();

        var animate = Motion.Enabled && IsLoaded && Shown && Root.ActualWidth > 40 && Root.ActualHeight > 40;
        Image? picture = null;
        if (animate)
        {
            try { picture = Snapshot(); }
            catch (Exception ex) { Log.Write($"theme snapshot: {ex.Message}"); }
        }
        if (picture is not null)
        {
            // shown in the same dispatcher frame as the swap below, so the window never flashes the new look early
            Panel.SetZIndex(picture, 1000);
            Root.Children.Add(picture);
            _reveal = picture;
        }

        Ui.SetTheme(next);
        ThemeButton.Tag = next.Light ? "sun" : "moon";
        foreach (var key2 in _built.Keys.ToList())
            if (!ReferenceEquals(_built[key2], _current) && !KeepBuilt.Contains(key2)) _built.Remove(key2); // rebuilt in the new colours when opened

        if (picture is not null) Play(picture, origin);
    }

    private Image Snapshot()
    {
        var dpi = VisualTreeHelper.GetDpi(Root);
        var w = Root.ActualWidth;
        var h = Root.ActualHeight;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        if (Ui.IsGlass)
        {
            // glass is see-through (the system draws the material behind it): paint a stand-in so the old look is opaque
            var backing = new DrawingVisual();
            using (var dc = backing.RenderOpen())
                dc.DrawRectangle(new SolidColorBrush(Ui.Theme.Light ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x20)), null, new Rect(0, 0, w, h));
            bitmap.Render(backing);
        }
        bitmap.Render(Root);
        // an off-screen render of a right-to-left tree skips the mirroring the window applies, so undo it here
        BitmapSource source = Root.FlowDirection == FlowDirection.RightToLeft ? new TransformedBitmap(bitmap, new ScaleTransform(-1, 1)) : bitmap;
        source.Freeze();
        return new Image
        {
            Source = source, Width = w, Height = h, Stretch = Stretch.Fill, FlowDirection = FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
        };
    }

    private void Play(Image picture, FrameworkElement? origin)
    {
        var w = picture.Width;
        var h = picture.Height;
        Point center;
        try { center = origin is { IsLoaded: true } ? picture.PointFromScreen(origin.PointToScreen(new Point(origin.ActualWidth / 2, origin.ActualHeight / 2))) : new Point(w / 2, h / 2); }
        catch { center = new Point(w / 2, h / 2); }
        // the picture lives in a mirrored (right-to-left) tree: its clip is mirrored as well
        if (Root.FlowDirection == FlowDirection.RightToLeft) center.X = w - center.X;
        var reach = new[] { new Point(0, 0), new Point(w, 0), new Point(0, h), new Point(w, h) }.Max(c => (c - center).Length) + 2;

        _hole = new EllipseGeometry(center, 0, 0);
        picture.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, w, h)), _hole);

        var ticket = ++_revealTicket;
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
        var grow = new DoubleAnimation(0, reach, TimeSpan.FromMilliseconds(680)) { EasingFunction = ease };
        grow.Completed += (_, _) => { if (ticket == _revealTicket) FinishReveal(); };
        _hole.BeginAnimation(EllipseGeometry.RadiusXProperty, grow);
        _hole.BeginAnimation(EllipseGeometry.RadiusYProperty, grow.Clone());
        SizeChanged += CancelReveal;
        StateChanged += CancelReveal;
        IsVisibleChanged += CancelRevealV;
    }

    private void CancelReveal(object? sender, EventArgs e) => FinishReveal();
    private void CancelRevealV(object sender, DependencyPropertyChangedEventArgs e) => FinishReveal();

    /// <summary>Ends the reveal at once (also when the window is resized or hidden): the overlay and its bitmap are released.</summary>
    private void FinishReveal()
    {
        _revealTicket++;
        SizeChanged -= CancelReveal;
        StateChanged -= CancelReveal;
        IsVisibleChanged -= CancelRevealV;
        if (_hole is { } hole)
        {
            hole.BeginAnimation(EllipseGeometry.RadiusXProperty, null);
            hole.BeginAnimation(EllipseGeometry.RadiusYProperty, null);
            _hole = null;
        }
        if (_reveal is { } picture)
        {
            _reveal = null;
            picture.Clip = null;
            picture.Source = null;
            Root.Children.Remove(picture);
        }
    }
}
