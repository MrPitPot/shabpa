using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shabpa.Kit;
using Xunit;

namespace Shabpa.Tests;

public class AppIconsTests
{
    private static ImageSource Pixel()
    {
        var b = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        b.Freeze();
        return b;
    }

    [Fact]
    public void SlowReadNeverBlocksTheCaller()
    {
        var gate = new ManualResetEventSlim();
        var queue = new Queue<Action>();
        var icons = new AppIcons(_ => { gate.Wait(5000); return Pixel(); }, a => { lock (queue) queue.Enqueue(a); });
        var got = new List<ImageSource?>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(icons.Get("a.exe", got.Add, out _));
        Assert.True(watch.ElapsedMilliseconds < 500, "Get waited for the read");
        gate.Set();
        SpinWait.SpinUntil(() => { lock (queue) return queue.Count > 0; }, 5000);
        lock (queue) queue.Dequeue()();
        Assert.Single(got);
        Assert.NotNull(got[0]);
        Assert.True(icons.Get("a.exe", _ => { }, out var again)); // now cached
        Assert.NotNull(again);
    }

    [Fact]
    public void TwoAskersOneRead()
    {
        var reads = 0;
        var queue = new Queue<Action>();
        var icons = new AppIcons(_ => { Interlocked.Increment(ref reads); return Pixel(); }, a => { lock (queue) queue.Enqueue(a); });
        var calls = 0;
        icons.Get("b.exe", _ => calls++, out _);
        icons.Get("b.exe", _ => calls++, out _);
        SpinWait.SpinUntil(() => { lock (queue) return queue.Count > 0; }, 5000);
        lock (queue) queue.Dequeue()();
        Assert.Equal(1, reads);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void FailedReadGivesNoIconAndIsRemembered()
    {
        var reads = 0;
        var queue = new Queue<Action>();
        var icons = new AppIcons(_ => { reads++; throw new IOException(); }, a => { lock (queue) queue.Enqueue(a); });
        ImageSource? result = Pixel();
        icons.Get("c.exe", i => result = i, out _);
        SpinWait.SpinUntil(() => { lock (queue) return queue.Count > 0; }, 5000);
        lock (queue) queue.Dequeue()();
        Assert.Null(result);
        Assert.True(icons.Get("c.exe", _ => { }, out var icon));
        Assert.Null(icon);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void EmptyPathIsAnswerEmpty()
    {
        var icons = new AppIcons(_ => Pixel(), a => a());
        Assert.True(icons.Get("", _ => { }, out var icon));
        Assert.Null(icon);
    }
}
