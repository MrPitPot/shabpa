using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shabpa.Core;

/// <summary>JSON persistence under %AppData%\Shabpa (override with SHABPA_DATA).</summary>
public sealed class Store
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly string _file;

    public string Folder { get; }
    public AppState State { get; private set; } = new();
    /// <summary>True when <see cref="Load"/> found no data file: this is the first run on this PC (used by the guided tour).</summary>
    public bool FreshInstall { get; private set; }
    /// <summary>When true nothing is written to disk (demo / screenshot runs).</summary>
    public bool ReadOnly { get; set; }

    public Store(string? folder = null)
    {
        Folder = folder
                 ?? Environment.GetEnvironmentVariable("SHABPA_DATA")
                 ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shabpa");
        _file = Path.Combine(Folder, "state.json");
    }

    public void Load()
    {
        try
        {
            FreshInstall = !File.Exists(_file);
            if (!FreshInstall)
                State = JsonSerializer.Deserialize<AppState>(File.ReadAllText(_file), Json) ?? new AppState();
            Log.Write($"state loaded: {State.Macros.Count} taught launchers, {State.Tickets.Count} tickets, {State.Tasks.Count} tasks, {State.GameHistory.Count} games");
            Backup();
        }
        catch (Exception ex)
        {
            // A corrupt file must never stop the app from starting; keep it aside for inspection.
            Log.Write($"state load failed: {ex.Message}");
            try { File.Copy(_file, _file + ".broken", true); } catch { /* best effort */ }
            State = TryBackup() ?? new AppState();
        }
    }

    /// <summary>One copy of the good file per day (the last seven are kept), so a bad write or a wrong turn can be undone.</summary>
    private void Backup()
    {
        if (ReadOnly || !File.Exists(_file)) return;
        try
        {
            var folder = Path.Combine(Folder, "backups");
            Directory.CreateDirectory(folder);
            var today = Path.Combine(folder, $"state-{DateTime.Now:yyyyMMdd}.json");
            if (!File.Exists(today)) File.Copy(_file, today);
            foreach (var old in Directory.GetFiles(folder, "state-*.json").OrderByDescending(f => f).Skip(7)) File.Delete(old);
        }
        catch (Exception ex)
        {
            Log.Write($"state backup failed: {ex.Message}");
        }
    }

    /// <summary>The newest daily copy that still reads, used when the main file cannot be read.</summary>
    private AppState? TryBackup()
    {
        try
        {
            var folder = Path.Combine(Folder, "backups");
            if (!Directory.Exists(folder)) return null;
            foreach (var file in Directory.GetFiles(folder, "state-*.json").OrderByDescending(f => f))
            {
                try
                {
                    if (JsonSerializer.Deserialize<AppState>(File.ReadAllText(file), Json) is { } state)
                    {
                        Log.Write($"state restored from {Path.GetFileName(file)}");
                        return state;
                    }
                }
                catch { /* try the next one */ }
            }
        }
        catch { /* nothing to restore from */ }
        return null;
    }

    private bool TryWrite()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(State, Json));
            File.Move(tmp, _file, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Write($"state save failed: {ex.Message}");
            return true; // not something waiting would fix
        }
    }

    private int _retrying;

    // Saves made on the window's thread: the text is made there, the disk is touched by a worker. The text waits under its own small lock (never under _gate, which the worker holds while it
    // writes: the window would then wait for the disk after all). Every save gets a number, and an older text never overwrites a newer file.
    private readonly object _pendingGate = new();
    private string? _pendingJson;
    private long _seq, _writtenSeq;
    private int _writing;

    /// <summary>Waits for a write that was handed to the background (called when the app exits so the last change is on disk).</summary>
    public void Flush()
    {
        for (var i = 0; i < 100 && Volatile.Read(ref _writing) == 1; i++) Thread.Sleep(50);
        string? text;
        long seq;
        lock (_pendingGate) { text = _pendingJson; _pendingJson = null; seq = Interlocked.Read(ref _seq); }
        if (text is null) return;
        try
        {
            Directory.CreateDirectory(Folder);
            lock (_gate)
            {
                if (seq < _writtenSeq) return;
                File.WriteAllText(_file, text);
                _writtenSeq = seq;
            }
        }
        catch (Exception ex) { Log.Write($"state save failed: {ex.Message}"); }
    }

    private void StartWriter()
    {
        if (Interlocked.Exchange(ref _writing, 1) == 1) return;      // the running writer picks the newest text up
        _ = Task.Run(() =>
        {
            try
            {
                while (true)
                {
                    string? text;
                    long seq;
                    lock (_pendingGate) { text = _pendingJson; _pendingJson = null; seq = Interlocked.Read(ref _seq); }
                    if (text is null) break;
                    for (var attempt = 1; attempt <= 7; attempt++)
                    {
                        try
                        {
                            Directory.CreateDirectory(Folder);
                            var tmp = _file + ".tmp";
                            lock (_gate)
                            {
                                if (seq < _writtenSeq) break;                       // a newer save has been written meanwhile
                                File.WriteAllText(tmp, text);
                                File.Move(tmp, _file, true);
                                _writtenSeq = seq;
                            }
                            break;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            if (attempt == 7) Log.Write($"state save failed: {ex.Message}");
                            else Thread.Sleep(150 * attempt);
                        }
                        catch (Exception ex) { Log.Write($"state save failed: {ex.Message}"); break; }
                    }
                }
            }
            finally { Interlocked.Exchange(ref _writing, 0); }
            // a save that arrived after the last look and before the flag was cleared would wait for the next save: look once more
            bool more;
            lock (_pendingGate) more = _pendingJson is not null;
            if (more) StartWriter();
        });
    }

    public void Save()
    {
        if (ReadOnly) return;
        // On the window's own thread the text is made here (a consistent copy of the state, a few milliseconds) but the disk is
        // touched in the background: a slow disk or a scanning antivirus used to freeze the window for seconds on every click.
        if (System.Windows.Application.Current?.Dispatcher is { } ui && ui.CheckAccess())
        {
            string json;
            try { json = JsonSerializer.Serialize(State, Json); }
            catch (Exception ex) { Log.Write($"state save failed: {ex.Message}"); return; }      // the state changed under the serializer (another thread): the next save will do it
            lock (_pendingGate) { _pendingJson = json; Interlocked.Increment(ref _seq); }
            StartWriter();
            return;
        }
        lock (_gate)
        {
            var mine = Interlocked.Increment(ref _seq);
            if (TryWrite()) { _writtenSeq = mine; return; }
        }
        // Something else (an antivirus scan, a backup program, a second Shabpa) holds the file for a moment. The window must
        // not wait for it, so the writing is tried again in the background, a few times, then straight over the file.
        if (Interlocked.Exchange(ref _retrying, 1) == 1) return;
        _ = Task.Run(() =>
        {
            try
            {
                for (var attempt = 1; attempt <= 6; attempt++)
                {
                    Thread.Sleep(150 * attempt);
                    lock (_gate)
                    {
                        var mine = Interlocked.Increment(ref _seq);
                        if (TryWrite()) { _writtenSeq = mine; return; }
                    }
                }
                lock (_gate)
                {
                    try { File.WriteAllText(_file, JsonSerializer.Serialize(State, Json)); _writtenSeq = Interlocked.Increment(ref _seq); }
                    catch (Exception ex) { Log.Write($"state save failed: {ex.Message}"); }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _retrying, 0);
            }
        });
    }
}

public static class Log
{
    private static readonly object Gate = new();
    public static string? File { get; set; }

    public static void Write(string message)
    {
        if (File is null) return;
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File)!);
                var info = new FileInfo(File);
                if (info.Exists && info.Length > 512 * 1024) System.IO.File.Move(File, File + ".old", overwrite: true); // keeps the previous log too, so a stall from last week is still there
                System.IO.File.AppendAllText(File, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + $" {message}{Environment.NewLine}");
            }
            catch { /* logging must never throw */ }
        }
    }
}
