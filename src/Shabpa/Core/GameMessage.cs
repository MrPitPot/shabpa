using System.Text;

namespace Shabpa.Core;

/// <summary>Builds the message sent to Telegram when a game ends: sections with an emoji heading, one bullet per fact.
/// <see cref="Html"/> uses Telegram's HTML subset (b, i, code; everything else escaped) and never exceeds
/// <see cref="MaxLength"/>; <see cref="Plain"/> is the same layout without tags, for the app's own notice.</summary>
public static class GameMessage
{
    public const int MaxLength = 4096;
    private const string Rule = "──────────────";

    private sealed record Section(string Emoji, string Title, List<string> Items);

    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static string Html(FinishedSession f) => Fit(Render(f, html: true));
    public static string Plain(FinishedSession f) => Render(f, html: false);

    private static string Render(FinishedSession f, bool html)
    {
        var r = f.Record;
        var name = f.Name.Length > 80 ? f.Name[..80] + "…" : f.Name;
        var sections = Build(r);
        var sb = new StringBuilder();
        sb.Append("🎮 ").Append(html ? "<b>" + Esc(name) + "</b>" : name).Append('\n');
        sb.Append("⏱ ").Append(L.T("مدت بازی: ", "Played: ")).Append(Fa.Duration(r.Length)).Append('\n');
        foreach (var s in sections)
        {
            sb.Append(Rule).Append('\n');
            var title = s.Emoji + " " + s.Title;
            sb.Append(html ? "<b>" + Esc(title) + "</b>" : title).Append('\n');
            foreach (var item in s.Items) sb.Append("  • ").Append(html ? Esc(item) : item).Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    private static List<Section> Build(GameSessionRecord r)
    {
        var list = new List<Section>();
        if (r.Stat(GameMetric.Fps) is { } fps)
            list.Add(new("🎞", L.T("فریم بر ثانیه", "Frame rate"), [L.T($"میانگین: {Fa.N(fps.Avg, 0)}", $"Average: {Fa.N(fps.Avg, 0)}")]));
        var temps = new List<string>();
        if (r.Stat(GameMetric.GpuTemp) is { } g) temps.Add(L.T($"گرافیک: میانگین {Fa.N(g.Avg, 0)}° · تا {Fa.N(g.Max, 0)}°", $"GPU: avg {Fa.N(g.Avg, 0)}° · peak {Fa.N(g.Max, 0)}°"));
        if (r.Stat(GameMetric.CpuTemp) is { } c) temps.Add(L.T($"پردازنده: میانگین {Fa.N(c.Avg, 0)}° · تا {Fa.N(c.Max, 0)}°", $"CPU: avg {Fa.N(c.Avg, 0)}° · peak {Fa.N(c.Max, 0)}°"));
        if (temps.Count > 0) list.Add(new("🌡", L.T("دما", "Temperature"), temps));
        var load = new List<string>();
        if (r.Stat(GameMetric.GpuLoad) is { } gl) load.Add(L.T($"گرافیک: تا {Fa.N(gl.Max, 0)}٪", $"GPU: up to {Fa.N(gl.Max, 0)}%"));
        if (r.Stat(GameMetric.CpuLoad) is { } cl) load.Add(L.T($"پردازنده: تا {Fa.N(cl.Max, 0)}٪", $"CPU: up to {Fa.N(cl.Max, 0)}%"));
        if (r.Stat(GameMetric.Ram) is { } rm) load.Add(L.T($"رم: تا {Fa.N(rm.Max, 0)}٪", $"RAM: up to {Fa.N(rm.Max, 0)}%"));
        if (load.Count > 0) list.Add(new("⚡", L.T("بیشترین مصرف", "Peak use"), load));
        if (r.DownBytes + r.UpBytes > 1_000_000)
            list.Add(new("📶", L.T("اینترنت", "Internet"),
            [
                L.T($"دانلود: {Fa.Bytes(r.DownBytes)}", $"Download: {Fa.Bytes(r.DownBytes)}"),
                L.T($"آپلود: {Fa.Bytes(r.UpBytes)}", $"Upload: {Fa.Bytes(r.UpBytes)}") + (r.NetApprox ? L.T(" (کل سیستم)", " (whole PC)") : ""),
            ]));
        return list;
    }

    /// <summary>Telegram refuses anything over 4096 characters; cut at a line end so no tag is left open.</summary>
    internal static string Fit(string text)
    {
        if (text.Length <= MaxLength) return text;
        var cut = text.LastIndexOf('\n', MaxLength - 2);
        return (cut > 0 ? text[..cut] : text[..(MaxLength - 1)]) + "…";
    }
}
