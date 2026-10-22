using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Tests;

[Collection("Wpf")]
public class SliderDragTests
{
    [Fact]
    public void Slide_style_enables_drag_from_anywhere_and_maps_points_to_values()
    {
        double? low = null, high = null;
        bool enabled = false;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                // A private dictionary, so no Application is created on this thread (other tests share Application.Current).
                _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; // registers the pack:// scheme
                var res = new ResourceDictionary();
                res.MergedDictionaries.Add(Ui.Resources(Palette.Find("night")));
                res.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Shabpa;component/Theme.xaml", UriKind.Absolute) });
                var s = new Slider { Style = (Style)res["Slide"], Minimum = 0, Maximum = 100, Width = 300 };
                s.Resources.MergedDictionaries.Add(res);
                s.ApplyTemplate();
                s.Measure(new Size(300, 28));
                s.Arrange(new Rect(0, 0, 300, 28));
                s.UpdateLayout();
                enabled = SliderDrag.GetEnabled(s);
                low = SliderDrag.ValueAt(s, new Point(20, 14));
                high = SliderDrag.ValueAt(s, new Point(280, 14));
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("STA test failed", error);
        Assert.True(enabled);
        Assert.NotNull(low);
        Assert.NotNull(high);
        Assert.True(low < 20, $"left end of the track should be a low value, got {low}");
        Assert.True(high > 80, $"right end of the track should be a high value, got {high}");
    }
}
