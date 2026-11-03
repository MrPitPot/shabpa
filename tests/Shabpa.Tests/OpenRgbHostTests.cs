using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Shabpa.Core;

namespace Shabpa.Tests;

/// <summary>The one-click lighting server: only the exact official file is accepted, it is unpacked inside Shabpa's own folder only, and it is
/// only ever started on this PC's loopback. Nothing here touches the network or runs OpenRGB.</summary>
[Collection("OpenRgbHost")]
public class OpenRgbHostTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shabpa-orgb-" + Guid.NewGuid().ToString("N"));
    private readonly string _saved = OpenRgbHost.Folder;

    public OpenRgbHostTests() => OpenRgbHost.Folder = Path.Combine(_root, "openrgb");

    public void Dispose()
    {
        OpenRgbHost.Folder = _saved;
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void ServerIsBlockedWhenTheVisualCppRuntimeIsMissingOrWindowsIsTooOld()
    {
        var system = Path.Combine(_root, "system32");
        Directory.CreateDirectory(system);
        Assert.Equal("runtime", OpenRgbHost.Blocked(system, 19045));            // an empty "Windows" folder: nothing there
        foreach (var f in OpenRgbHost.RuntimeFiles) File.WriteAllText(Path.Combine(system, f), "x");
        Assert.Equal("", OpenRgbHost.Blocked(system, 19045));                   // Windows 10 22H2 with the runtime
        Assert.Equal("windows", OpenRgbHost.Blocked(system, 17134));            // Windows 10 1803: too old for Qt 6
        File.Delete(Path.Combine(system, "vcruntime140_1.dll"));
        Assert.Equal("runtime", OpenRgbHost.Blocked(system, 22631));            // the newer runtime file is the one that is often missing
    }

    [Fact]
    public void A_server_that_keeps_ending_right_away_is_not_started_again_for_five_minutes_until_the_person_asks()
    {
        OpenRgbHost.Start(force: true);                                               // clears any state from other tests
        var now = Environment.TickCount64;
        OpenRgbHost.NoteEnded(2_000, now);
        OpenRgbHost.NoteEnded(3_000, now);
        Assert.False(OpenRgbHost.Paused);                                              // two quick ends are bad luck
        OpenRgbHost.NoteEnded(1_000, now);
        Assert.True(OpenRgbHost.Paused);                                               // the third in a row pauses it
        Assert.False(OpenRgbHost.Start());                                             // nothing is started while paused (and nothing is installed here anyway)
        OpenRgbHost.Start(force: true);
        Assert.False(OpenRgbHost.Paused);                                              // «start again» ends the pause
        OpenRgbHost.NoteEnded(1_000, now);
        OpenRgbHost.NoteEnded(60_000, now);                                            // a server that ran for a minute resets the count
        OpenRgbHost.NoteEnded(1_000, now);
        OpenRgbHost.NoteEnded(1_000, now);
        Assert.False(OpenRgbHost.Paused);
    }

    [Fact]
    public void A_replaced_openrgb_exe_is_never_started_and_an_older_install_is_recorded_as_it_is()
    {
        Directory.CreateDirectory(OpenRgbHost.Folder);
        File.WriteAllText(OpenRgbHost.ExePath, "the real one");
        var marker = Path.Combine(OpenRgbHost.Folder, "shabpa-openrgb.txt");
        File.WriteAllText(marker, OpenRgbHost.Release.Sha256);                          // an install from before the check: one line only
        Assert.True(OpenRgbHost.ExeIntact());                                           // taken as it is, and recorded
        Assert.Equal(2, File.ReadAllText(marker).Split('\n').Length);
        Assert.True(OpenRgbHost.Installed);                                             // the first line still decides «installed»
        Assert.True(OpenRgbHost.ExeIntact());
        File.WriteAllText(OpenRgbHost.ExePath, "something else put there");
        Assert.False(OpenRgbHost.ExeIntact());                                          // replaced: refused
    }

    private sealed class Serve(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls;
        public string? Url;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Url = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) });
        }
    }

    private static byte[] Zip(params (string name, string text)[] files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, text) in files)
            {
                var e = zip.CreateEntry(name);
                using var w = new StreamWriter(e.Open());
                w.Write(text);
            }
        return ms.ToArray();
    }

    private static OpenRgbRelease For(byte[] zip, string? hash = null) =>
        new("test", "https://example.invalid/OpenRGB.zip", hash ?? Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant());

    [Fact]
    public void The_release_is_the_fixed_official_file_with_a_sha256()
    {
        Assert.StartsWith("https://codeberg.org/OpenRGB/OpenRGB/releases/download/", OpenRgbHost.Release.Url);
        Assert.EndsWith("Windows_64_8e16de2.zip", OpenRgbHost.Release.Url);
        Assert.Matches("^[0-9a-f]{64}$", OpenRgbHost.Release.Sha256);
    }

    [Fact]
    public async Task The_right_file_is_unpacked_without_its_top_folder_and_marked_installed()
    {
        var zip = Zip(("OpenRGB Windows 64-bit/OpenRGB.exe", "exe"), ("OpenRGB Windows 64-bit/platforms/qwindows.dll", "dll"), ("OpenRGB Windows 64-bit/Qt6Core.dll", "core"));
        var seen = new List<int>();
        var result = await OpenRgbHost.InstallAsync(new Progress<int>(seen.Add), handler: new Serve(zip), release: For(zip));
        Assert.True(result.Ok, result.Error);
        Assert.Equal("exe", File.ReadAllText(OpenRgbHost.ExePath));
        Assert.True(File.Exists(Path.Combine(OpenRgbHost.Folder, "platforms", "qwindows.dll")));
        Assert.False(File.Exists(OpenRgbHost.Folder + ".download"));
        Assert.False(Directory.Exists(OpenRgbHost.Folder + ".new"));
        Assert.Equal(100, seen.Last());
    }

    private sealed class ByHost(Dictionary<string, byte[]?> answers) : HttpMessageHandler
    {
        public readonly List<string> Asked = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri!.Host;
            Asked.Add(host);
            if (answers.TryGetValue(host, out var bytes) && bytes is not null) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            if (answers.ContainsKey(host)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            throw new HttpRequestException("blocked");                                   // the official host cannot be reached
        }
    }

    [Fact]
    public async Task A_blocked_official_host_falls_back_to_a_mirror_with_the_same_file()
    {
        var zip = Zip(("OpenRGB.exe", "exe"));
        var release = For(zip) with { Url = "https://official.invalid/a.zip", Mirrors = ["https://down.invalid/a.zip", "https://mirror.invalid/a.zip"] };
        var handler = new ByHost(new() { ["down.invalid"] = null, ["mirror.invalid"] = zip });
        var result = await OpenRgbHost.InstallAsync(handler: handler, release: release);
        Assert.True(result.Ok, result.Error);
        Assert.Equal(["official.invalid", "down.invalid", "mirror.invalid"], handler.Asked);
        Assert.Equal("exe", File.ReadAllText(OpenRgbHost.ExePath));
    }

    [Fact]
    public async Task A_mirror_with_a_different_file_is_refused_like_any_other()
    {
        var zip = Zip(("OpenRGB.exe", "exe"));
        var release = For(zip) with { Url = "https://official.invalid/a.zip", Mirrors = ["https://mirror.invalid/a.zip"] };
        var result = await OpenRgbHost.InstallAsync(handler: new ByHost(new() { ["mirror.invalid"] = Zip(("OpenRGB.exe", "evil")) }), release: release);
        Assert.False(result.Ok);
        Assert.Equal("hash", result.Error);
        Assert.False(OpenRgbHost.Installed);
    }

    [Fact]
    public async Task A_file_with_another_hash_is_refused_and_nothing_is_installed()
    {
        var zip = Zip(("OpenRGB.exe", "evil"));
        var result = await OpenRgbHost.InstallAsync(handler: new Serve(zip), release: For(zip, new string('0', 64)));
        Assert.False(result.Ok);
        Assert.Equal("hash", result.Error);
        Assert.False(OpenRgbHost.Installed);
        Assert.False(Directory.Exists(OpenRgbHost.Folder));
        Assert.False(File.Exists(OpenRgbHost.Folder + ".download"));
    }

    [Fact]
    public async Task A_failed_download_installs_nothing()
    {
        var result = await OpenRgbHost.InstallAsync(handler: new Serve([], HttpStatusCode.NotFound), release: For([1, 2, 3]));
        Assert.Equal(("download", false), (result.Error, result.Ok));
        Assert.False(OpenRgbHost.Installed);
    }

    [Fact]
    public async Task A_zip_entry_that_climbs_out_of_the_folder_is_refused()
    {
        var zip = Zip(("OpenRGB.exe", "exe"), ("../outside.txt", "bad"));
        var result = await OpenRgbHost.InstallAsync(handler: new Serve(zip), release: For(zip));
        Assert.False(result.Ok);
        Assert.False(File.Exists(Path.Combine(_root, "outside.txt")));
        Assert.False(OpenRgbHost.Installed);
    }

    [Fact]
    public async Task A_zip_without_the_program_is_not_an_install()
    {
        var zip = Zip(("readme.txt", "hello"));
        var result = await OpenRgbHost.InstallAsync(handler: new Serve(zip), release: For(zip));
        Assert.Equal("unpack", result.Error);
        Assert.False(OpenRgbHost.Installed);
    }

    [Fact]
    public async Task Installing_again_replaces_only_shabpas_own_copy()
    {
        var first = Zip(("OpenRGB.exe", "one"));
        Assert.True((await OpenRgbHost.InstallAsync(handler: new Serve(first), release: For(first))).Ok);
        var other = Path.Combine(_root, "keep.txt");
        File.WriteAllText(other, "mine");
        var second = Zip(("OpenRGB.exe", "two"));
        Assert.True((await OpenRgbHost.InstallAsync(handler: new Serve(second), release: For(second))).Ok);
        Assert.Equal("two", File.ReadAllText(OpenRgbHost.ExePath));
        Assert.Equal("mine", File.ReadAllText(other));
    }

    [Fact]
    public void An_install_of_another_release_is_not_taken_for_the_current_one()
    {
        Directory.CreateDirectory(OpenRgbHost.Folder);
        File.WriteAllText(OpenRgbHost.ExePath, "x");
        Assert.False(OpenRgbHost.Installed);                                   // no marker
        File.WriteAllText(Path.Combine(OpenRgbHost.Folder, "shabpa-openrgb.txt"), "something else");
        Assert.False(OpenRgbHost.Installed);
        File.WriteAllText(Path.Combine(OpenRgbHost.Folder, "shabpa-openrgb.txt"), OpenRgbHost.Release.Sha256);
        Assert.True(OpenRgbHost.Installed);
    }

    [Fact]
    public void The_server_is_started_on_this_pc_only_with_its_own_config_and_no_window_options()
    {
        var args = OpenRgbHost.Arguments(6742);
        Assert.Contains("--server", args);
        Assert.Contains("--server-host 127.0.0.1", args);
        Assert.Contains("--server-port 6742", args);
        Assert.Contains("--localconfig", args);
        Assert.DoesNotContain("--gui", args);
        Assert.DoesNotContain("0.0.0.0", args);
    }

    [Fact]
    public void Nothing_is_started_when_nothing_is_installed()
    {
        Assert.False(OpenRgbHost.Start(46742));
        OpenRgbHost.Stop();                                                    // and stopping nothing is harmless
    }
}
