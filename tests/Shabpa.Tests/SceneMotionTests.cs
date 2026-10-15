using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Shabpa.Core;
using Shabpa.Pages;
using Xunit;

namespace Shabpa.Tests;

/// <summary>The weather backdrop must really move: two renders a moment apart differ.</summary>
public class SceneMotionTests
{
    private static void OnSta(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("STA test failed", error);
    }

    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static byte[] Render(FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[(int)element.ActualWidth * (int)element.ActualHeight * 4];
        bitmap.CopyPixels(pixels, (int)element.ActualWidth * 4, 0);
        return pixels;
    }

    [Theory]
    [InlineData(Sky.Rain, true)]
    [InlineData(Sky.Snow, true)]
    [InlineData(Sky.Thunder, false)]
    [InlineData(Sky.Cloudy, true)]
    public void Scene_changes_over_time(Sky sky, bool day)
    {
        OnSta(() =>
        {
            _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var scene = new WeatherScene { Width = 600, Height = 260 };
            var window = new Window { Content = scene, Width = 640, Height = 320, WindowStyle = WindowStyle.None };
            window.Show();
            scene.Set(sky, day);
            scene.UpdateLayout();
            try
            {
                Pump(250);
                var first = Render(scene);
                Pump(350);
                var second = Render(scene);
                var changed = 0;
                for (var i = 0; i < first.Length; i += 4)
                    if (first[i] != second[i] || first[i + 1] != second[i + 1] || first[i + 2] != second[i + 2]) changed++;
                Assert.True(changed > 200, $"{sky}: only {changed} pixels changed between frames");
            }
            finally
            {
                window.Close();
            }
        });
    }
}
