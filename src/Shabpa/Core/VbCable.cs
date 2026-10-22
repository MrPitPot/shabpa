using System.Diagnostics;

namespace Shabpa.Core;

/// <summary>Links to VB-Audio's own VB-Cable pages. VB-Cable is donationware and its licence lets other programs bundle it only under
/// set conditions, so Shabpa never downloads or installs it: these only open the official page (or its direct package link) in the
/// user's browser, where they choose to download it.</summary>
public static class VbCable
{
    public const string OfficialPage = "https://vb-audio.com/Cable/";
    public const string DirectPackage = "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip";

    public static bool IsOfficial(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps && (u.Host == "vb-audio.com" || u.Host.EndsWith(".vb-audio.com", StringComparison.Ordinal));

    public static void Open(string url)
    {
        if (!IsOfficial(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write($"open vb-cable page: {ex.GetType().Name}"); }
    }
}
