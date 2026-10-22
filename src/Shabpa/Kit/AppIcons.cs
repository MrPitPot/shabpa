using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shabpa.Kit;

/// <summary>Program icons from the exe files. Reading one touches the disk and the shell (slow behind a virus scanner or on a
/// busy PC: tens of milliseconds each, hundreds for a list), so it never happens on the window thread: <see cref="Get"/> answers
/// at once from the cache or starts the read on a worker and calls back on the window thread when it is done.</summary>
public sealed class AppIcons
{
    public static readonly AppIcons Shared = new();

    private readonly Dictionary<string, ImageSource?> _cache = [];
    private readonly Dictionary<string, List<Action<ImageSource?>>> _waiting = [];
    private readonly Func<string, ImageSource?> _read;
    private readonly Action<Action> _post;

    public AppIcons() : this(Read, a => (Application.Current?.Dispatcher)?.BeginInvoke(a)) { }

    /// <summary>Test seam: how an icon is read and how a callback gets back to the window thread.</summary>
    internal AppIcons(Func<string, ImageSource?> read, Action<Action> post) { _read = read; _post = post; }

    /// <summary>Returns true with the icon (null = this file has none) when it is known. Otherwise returns false and
    /// <paramref name="done"/> is called later, once, on the window thread. Call from the window thread.</summary>
    public bool Get(string path, Action<ImageSource?> done, out ImageSource? icon)
    {
        icon = null;
        if (path.Length == 0) return true;
        if (_cache.TryGetValue(path, out icon)) return true;
        if (_waiting.TryGetValue(path, out var list)) { list.Add(done); return false; }
        _waiting[path] = [done];
        _ = Task.Run(() =>
        {
            ImageSource? read = null;
            try { read = _read(path); } catch { /* no icon: the generic badge stays */ }
            _post(() =>
            {
                _cache[path] = read;
                if (!_waiting.Remove(path, out var callbacks)) return;
                foreach (var callback in callbacks) callback(read);
            });
        });
        return false;
    }

    private static ImageSource? Read(string path)
    {
        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
        if (icon is null) return null;
        var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }
}
