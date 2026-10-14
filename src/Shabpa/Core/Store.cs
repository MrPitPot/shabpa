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
            if (File.Exists(_file))
                State = JsonSerializer.Deserialize<AppState>(File.ReadAllText(_file), Json) ?? new AppState();
        }
        catch (Exception ex)
        {
            // A corrupt file must never stop the app from starting; keep it aside for inspection.
            Log.Write($"state load failed: {ex.Message}");
            try { File.Copy(_file, _file + ".broken", true); } catch { /* best effort */ }
            State = new AppState();
        }
    }

    public void Save()
    {
        if (ReadOnly) return;
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var tmp = _file + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(State, Json));
                File.Move(tmp, _file, true);
            }
            catch (Exception ex)
            {
                Log.Write($"state save failed: {ex.Message}");
            }
        }
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
                if (info.Exists && info.Length > 512 * 1024) info.Delete();
                System.IO.File.AppendAllText(File, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + $" {message}{Environment.NewLine}");
            }
            catch { /* logging must never throw */ }
        }
    }
}
