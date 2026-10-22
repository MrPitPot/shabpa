using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Shabpa.Tests;

/// <summary>Plain fake of the internet: URL to bytes, optional Range support, per-URL overrides.</summary>
internal sealed class FakeNet : HttpMessageHandler
{
    public readonly Dictionary<string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> Routes = new();
    public readonly List<HttpRequestMessage> Requests = [];

    public FakeNet Serve(string url, byte[] body, bool honorRange = true)
    {
        Routes[url] = (req, _) =>
        {
            if (honorRange && req.Headers.Range?.Ranges.FirstOrDefault()?.From is { } from)
            {
                var part = body[(int)from..];
                var r = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(part) };
                r.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, body.Length - 1, body.Length);
                return Task.FromResult(r);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        };
        return this;
    }

    public FakeNet Status(string url, HttpStatusCode code)
    {
        Routes[url] = (_, _) => Task.FromResult(new HttpResponseMessage(code));
        return this;
    }

    public FakeNet Hang(string url)
    {
        Routes[url] = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); };
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        lock (Requests) Requests.Add(request);
        return Routes.TryGetValue(request.RequestUri!.AbsoluteUri, out var route)
            ? route(request, ct)
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    public HttpClient Client() => new(this) { Timeout = Timeout.InfiniteTimeSpan };
}

/// <summary>Answers some bytes, then never again (a mirror that stalls mid-download).</summary>
internal sealed class StallStream(byte[] first) : Stream
{
    private int _pos;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _pos; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_pos >= first.Length) { await Task.Delay(Timeout.Infinite, ct); return 0; }
        var n = Math.Min(buffer.Length, first.Length - _pos);
        first.AsMemory(_pos, n).CopyTo(buffer);
        _pos += n;
        return n;
    }
}

public sealed class UpdateMirrorTests : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _pub;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shabpa-mirror-test-" + Guid.NewGuid().ToString("N"));
    private static readonly Version Now = new(3, 10, 0);
    private static readonly byte[] Installer = Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray();

    public UpdateMirrorTests()
    {
        _pub = Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        _key.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static string Hex(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private string Json(string version = "3.11.0", byte[]? file = null, string urls = "\"https://a.example/ShabpaSetup.exe\"", string min = "0.0.0", long? size = null)
    {
        file ??= Installer;
        return $$$"""{"schema":1,"version":"{{{version}}}","minAppVersion":"{{{min}}}","published":"2026-11-01T10:00:00Z","sha256":"{{{Hex(file)}}}","size":{{{size ?? file.Length}}},"urls":[{{{urls}}}],"notes":{"fa":"تغییرات","en":"changes"}}""";
    }

    private string Sign(string json) => Convert.ToBase64String(_key.SignData(Encoding.UTF8.GetBytes(json), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

    private FakeNet Mirror(FakeNet net, string manifestUrl, string json, string? sig = null)
    {
        net.Serve(manifestUrl, Encoding.UTF8.GetBytes(json));
        net.Serve(manifestUrl + ".sig", Encoding.ASCII.GetBytes(sig ?? Sign(json)));
        return net;
    }

    private const string Gh = "https://github.com/MrPitPot/shabpa/releases/latest/download/latest.json";
    private const string Site = "https://shabpa.ir/update/latest.json";
    private const string Dl = "https://dl.shabpa.ir/update/latest.json";

    private UpdateSources Sources(FakeNet net, int timeoutMs = 6000) =>
        new(net.Client(), _pub) { ManifestTimeout = TimeSpan.FromMilliseconds(timeoutMs) };

    // ---- signature and manifest --------------------------------------------------------------

    // Produced by `openssl dgst -sha256 -sign` with a throwaway key: the release script's exact output format.
    private const string OpenSslPub = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEEYsMo5dURH8qgXApRiQmmVp6t4SEmOYCwmTRwWoSAWBvtReEuhoD9h5oxUVnHbN9jtZwoegDWgq3gnbsEjEe3A==";
    private const string OpenSslSig = "MEUCIQCLBvaE3bqtA7xAm2pLYrYqORf901tgCjtykM+E8RoQDgIgfHbMPVz6PZjLS7h3L2rNt2lN6hioDzQjmlj5cPu7tOM=";
    private const string OpenSslBody = """{"version":"9.9.9","sha256":"ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb","urls":["https://x/y.exe"]}""";

    [Fact]
    public void A_signature_made_by_the_openssl_release_script_verifies()
    {
        var body = Encoding.UTF8.GetBytes(OpenSslBody);
        Assert.True(UpdateManifestReader.VerifySignature(body, OpenSslSig, OpenSslPub));
        Assert.False(UpdateManifestReader.VerifySignature(Encoding.UTF8.GetBytes(OpenSslBody.Replace("9.9.9", "9.9.8")), OpenSslSig, OpenSslPub));
    }

    [Fact]
    public void Signatures_fail_for_a_wrong_key_garbage_or_no_key()
    {
        var body = Encoding.UTF8.GetBytes(Json());
        var sig = Sign(Json());
        Assert.True(UpdateManifestReader.VerifySignature(body, sig, _pub));
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.False(UpdateManifestReader.VerifySignature(body, sig, Convert.ToBase64String(other.ExportSubjectPublicKeyInfo())));
        Assert.False(UpdateManifestReader.VerifySignature(body, "not base64!!", _pub));
        Assert.False(UpdateManifestReader.VerifySignature(body, sig, "garbage"));
        Assert.False(UpdateManifestReader.VerifySignature(body, sig, ""));
    }

    [Fact]
    public void A_signed_manifest_is_read_with_both_languages_of_notes()
    {
        var json = Json();
        var m = UpdateManifestReader.ReadSigned(Encoding.UTF8.GetBytes(json), Sign(json), _pub, null, false);
        Assert.Equal(new Version(3, 11, 0), m.Version);
        Assert.Equal(Installer.Length, m.Size);
        Assert.Equal(Hex(Installer), m.Sha256);
        Assert.Equal("changes", m.NotesEn);
        Assert.Equal("تغییرات", m.NotesFa);
        Assert.Single(m.Urls);
    }

    [Fact]
    public void A_build_without_a_release_key_refuses_every_mirror_manifest()
    {
        var json = Json();
        var ex = Assert.Throws<UpdateRejectedException>(() => UpdateManifestReader.ReadSigned(Encoding.UTF8.GetBytes(json), Sign(json), "", null, false));
        Assert.Equal("no-key", ex.Reason);
    }

    [Fact]
    public void A_tampered_manifest_is_rejected()
    {
        var json = Json();
        var evil = json.Replace("a.example", "evil.example");
        var ex = Assert.Throws<UpdateRejectedException>(() => UpdateManifestReader.ReadSigned(Encoding.UTF8.GetBytes(evil), Sign(json), _pub, null, false));
        Assert.Equal("signature", ex.Reason);
    }

    [Theory]
    [InlineData("\"http://a.example/s.exe\"")]
    [InlineData("\"ftp://a.example/s.exe\"")]
    [InlineData("\"file:///C:/x/s.exe\"")]
    [InlineData("\"\\\\\\\\server\\\\share\\\\s.exe\"")]
    public void Non_https_installer_addresses_are_refused(string urls)
    {
        var json = Json(urls: urls);
        var ex = Assert.Throws<UpdateRejectedException>(() => UpdateManifestReader.ReadSigned(Encoding.UTF8.GetBytes(json), Sign(json), _pub, null, false));
        Assert.Equal("scheme", ex.Reason);
    }

    [Fact]
    public void Local_files_are_only_accepted_when_the_caller_allows_them()
    {
        var json = Json(urls: "\"file:///C:/x/s.exe\"");
        var m = UpdateManifestReader.Parse(Encoding.UTF8.GetBytes(json), null, allowLocal: true);
        Assert.StartsWith("file:", m.Urls[0]);
    }

    [Fact]
    public void Relative_installer_addresses_follow_the_manifest_address()
    {
        var json = Json(urls: "\"ShabpaSetup.exe\"");
        var m = UpdateManifestReader.Parse(Encoding.UTF8.GetBytes(json), new Uri("https://dl.shabpa.ir/update/latest.json"), false);
        Assert.Equal("https://dl.shabpa.ir/update/ShabpaSetup.exe", m.Urls[0]);
        Assert.Throws<UpdateRejectedException>(() => UpdateManifestReader.Parse(Encoding.UTF8.GetBytes(json), null, false));
    }

    [Theory]
    [InlineData("{\"version\":\"3.11.0\"}")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void Malformed_manifests_are_rejected_not_thrown_raw(string json)
    {
        var ex = Assert.Throws<UpdateRejectedException>(() => UpdateManifestReader.Parse(Encoding.UTF8.GetBytes(json), null, false));
        Assert.Equal("parse", ex.Reason);
    }

    [Fact]
    public void A_manifest_with_a_bad_checksum_or_absurd_size_is_rejected()
    {
        var bad = Json().Replace(Hex(Installer), "abc");
        Assert.Equal("sha", Assert.Throws<UpdateRejectedException>(() => UpdateManifestReader.Parse(Encoding.UTF8.GetBytes(bad), null, false)).Reason);
        var huge = Json(size: 500L << 20);
        Assert.Equal("size", Assert.Throws<UpdateRejectedException>(() => UpdateManifestReader.Parse(Encoding.UTF8.GetBytes(huge), null, false)).Reason);
        var big = new byte[UpdateManifestReader.MaxManifestBytes + 1];
        Assert.Equal("size", Assert.Throws<UpdateRejectedException>(() => UpdateManifestReader.ReadSigned(big, "x", _pub, null, false)).Reason);
    }

    // ---- the source list ---------------------------------------------------------------------

    [Fact]
    public void Sources_are_ordered_github_site_arvan_domestic_custom_then_the_legacy_api()
    {
        var list = UpdateSources.Build("https://mirror.example.ir/shabpa", "");
        Assert.Equal(["github", "shabpa.ir", "arvan", "dl.shabpa.ir", "custom", "github-api"], list.Select(s => s.Id));
        Assert.Equal("https://mirror.example.ir/shabpa/latest.json", list[4].ManifestUrl);
        Assert.True(list[^1].Legacy);
    }

    [Fact]
    public void The_last_working_source_is_tried_first_but_the_legacy_api_stays_last()
    {
        Assert.Equal("dl.shabpa.ir", UpdateSources.Build("", "dl.shabpa.ir")[0].Id);
        Assert.Equal("github-api", UpdateSources.Build("", "github-api")[^1].Id);
        Assert.Equal("github", UpdateSources.Build("", "github-api")[0].Id);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("http://mirror.ir/x", null)]
    [InlineData("ftp://mirror.ir/x", null)]
    [InlineData("not a url", null)]
    [InlineData("https://m.ir/app/latest.json", "https://m.ir/app/latest.json")]
    [InlineData("https://m.ir/app/", "https://m.ir/app/latest.json")]
    public void The_extra_mirror_must_be_https(string text, string? expected) => Assert.Equal(expected, UpdateSources.NormalizeMirror(text));

    // ---- walking the sources -----------------------------------------------------------------

    [Fact]
    public async Task The_first_source_that_answers_wins_and_the_rest_are_not_asked()
    {
        var net = Mirror(new FakeNet(), Gh, Json());
        var r = await Sources(net).CheckAsync(UpdateSources.Build("", ""), Now);
        Assert.Equal("github", r.Source!.Id);
        Assert.Equal(new Version(3, 11, 0), r.Manifest!.Version);
        Assert.Empty(r.Failures);
        Assert.DoesNotContain(net.Requests, q => q.RequestUri!.Host == "shabpa.ir");
    }

    [Fact]
    public async Task A_404_or_a_dead_source_falls_through_to_the_next_in_order()
    {
        var net = Mirror(new FakeNet().Status(Gh, HttpStatusCode.NotFound).Status(Gh + ".sig", HttpStatusCode.NotFound), Site, Json());
        var r = await Sources(net).CheckAsync(UpdateSources.Build("", ""), Now);
        Assert.Equal("shabpa.ir", r.Source!.Id);
        Assert.Equal([new SourceFailure("github", "404")], r.Failures);
    }

    [Fact]
    public async Task A_source_that_hangs_costs_only_the_manifest_timeout()
    {
        var net = Mirror(new FakeNet().Hang(Gh).Hang(Gh + ".sig"), Site, Json());
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var r = await Sources(net, timeoutMs: 150).CheckAsync(UpdateSources.Build("", ""), Now);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
        Assert.Equal("shabpa.ir", r.Source!.Id);
        Assert.Equal("timeout", r.Failures[0].Reason);
    }

    [Fact]
    public async Task A_hostile_mirror_with_a_bad_signature_is_skipped()
    {
        var net = Mirror(new FakeNet(), Gh, Json(urls: "\"https://evil.example/ShabpaSetup.exe\""), sig: Convert.ToBase64String(new byte[70]));
        Mirror(net, Site, Json());
        var r = await Sources(net).CheckAsync(UpdateSources.Build("", ""), Now);
        Assert.Equal("signature", r.Failures[0].Reason);
        Assert.Equal("shabpa.ir", r.Source!.Id);
        Assert.DoesNotContain("evil", r.Manifest!.Urls[0]);
    }

    [Fact]
    public async Task A_mirror_signed_by_another_key_is_skipped()
    {
        using var rogue = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var json = Json();
        var rogueSig = Convert.ToBase64String(rogue.SignData(Encoding.UTF8.GetBytes(json), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        var r = await Sources(Mirror(new FakeNet(), Gh, json, rogueSig)).CheckAsync(UpdateSources.Build("", ""), Now);
        Assert.True(r.AllFailed);
        Assert.Equal("signature", r.Failures[0].Reason);
    }

    [Fact]
    public async Task An_older_manifest_is_a_downgrade_and_is_skipped_for_the_next_source()
    {
        var net = Mirror(new FakeNet(), Gh, Json("3.9.0"));
        Mirror(net, Site, Json("3.11.0"));
        var r = await Sources(net).CheckAsync(UpdateSources.Build("", ""), Now);
        Assert.Equal("stale", r.Failures[0].Reason);
        Assert.Equal("shabpa.ir", r.Source!.Id);
    }

    [Fact]
    public async Task The_same_version_means_up_to_date_and_stops_the_walk()
    {
        var net = Mirror(new FakeNet(), Gh, Json("3.10.0"));
        var r = await Sources(net).CheckAsync(UpdateSources.Build("", ""), Now);
        Assert.True(r.UpToDate);
        Assert.Null(r.Manifest);
        Assert.False(r.AllFailed);
    }

    [Fact]
    public async Task An_update_that_needs_a_newer_base_version_is_not_offered()
    {
        var r = await Sources(Mirror(new FakeNet(), Gh, Json("4.0.0", min: "3.10.5"))).CheckAsync(UpdateSources.Build("", ""), Now);
        Assert.True(r.AllFailed);
        Assert.Equal("min-app", r.Failures[0].Reason);
    }

    [Fact]
    public async Task Nothing_reachable_reports_every_failure()
    {
        var r = await Sources(new FakeNet(), 100).CheckAsync(UpdateSources.Build("", ""), Now);
        Assert.True(r.AllFailed);
        Assert.Equal(5, r.Failures.Count);
    }

    [Fact]
    public async Task A_plain_http_source_is_never_contacted()
    {
        var net = new FakeNet();
        var r = await Sources(net).CheckAsync([new UpdateSource("custom", "x", "http://mirror.ir/latest.json")], Now);
        Assert.Equal("scheme", r.Failures[0].Reason);
        Assert.Empty(net.Requests);
    }

    [Fact]
    public async Task An_oversized_manifest_is_cut_off()
    {
        var net = new FakeNet().Serve(Gh, new byte[UpdateManifestReader.MaxManifestBytes + 10]).Serve(Gh + ".sig", [1, 2]);
        var r = await Sources(net).CheckAsync([UpdateSources.Build("", "")[0]], Now);
        Assert.Equal("size", r.Failures[0].Reason);
    }

    [Fact]
    public async Task The_legacy_github_api_still_works_when_no_signed_mirror_does()
    {
        var release = $$"""{"tag_name":"v3.11.0","draft":false,"prerelease":false,"published_at":"2026-11-01T10:00:00Z","body":"notes","assets":[{"name":"ShabpaSetup.exe","browser_download_url":"https://github.com/MrPitPot/shabpa/releases/download/v3.11.0/ShabpaSetup.exe","size":5000,"digest":"sha256:{{Hex(Installer)}}"}]}""";
        var net = new FakeNet().Serve(UpdateSources.LegacyApi, Encoding.UTF8.GetBytes(release));
        var r = await Sources(net).CheckAsync(UpdateSources.Build("", ""), Now);
        Assert.Equal("github-api", r.Source!.Id);
        Assert.Equal(Hex(Installer), r.Manifest!.Sha256);
        Assert.Equal(4, r.Failures.Count);
    }

    [Fact]
    public async Task The_legacy_path_refuses_installers_hosted_outside_github()
    {
        var release = $$"""{"tag_name":"v3.11.0","assets":[{"name":"ShabpaSetup.exe","browser_download_url":"https://evil.example/ShabpaSetup.exe","size":5,"digest":"sha256:{{Hex(Installer)}}"}]}""";
        var net = new FakeNet().Serve(UpdateSources.LegacyApi, Encoding.UTF8.GetBytes(release));
        var r = await Sources(net).CheckAsync([new UpdateSource("github-api", "x", UpdateSources.LegacyApi, true)], Now);
        Assert.True(r.AllFailed);
        Assert.Equal("scheme", r.Failures[0].Reason);
    }

    [Fact]
    public async Task Local_folders_work_as_a_source_only_when_allowed()
    {
        var json = Json(urls: "\"ShabpaSetup.exe\"");
        File.WriteAllText(Path.Combine(_dir, "latest.json"), json);
        File.WriteAllText(Path.Combine(_dir, "latest.json.sig"), Sign(json));
        var src = new UpdateSource("custom", "usb", new Uri(Path.Combine(_dir, "latest.json")).AbsoluteUri);
        var denied = await new UpdateSources(new HttpClient(), _pub).CheckAsync([src], Now);
        Assert.Equal("scheme", denied.Failures[0].Reason);
        var ok = await new UpdateSources(new HttpClient(), _pub, allowLocal: true).CheckAsync([src], Now);
        Assert.Equal(new Version(3, 11, 0), ok.Manifest!.Version);
    }

    [Fact]
    public void Installer_addresses_on_the_serving_mirrors_host_come_first()
    {
        var m = new UpdateManifest(new Version(3, 11, 0), new Version(0, 0, 0), DateTime.Now, Hex(Installer), 1,
            ["https://github.com/a.exe", "https://dl.shabpa.ir/a.exe", "https://shabpa.ir/a.exe"], "", "");
        var order = UpdateSources.OrderUrls(m, new UpdateSource("dl.shabpa.ir", "dl", Dl));
        Assert.Equal("https://dl.shabpa.ir/a.exe", order[0]);
        Assert.Equal(3, order.Count);
    }

    // ---- downloading --------------------------------------------------------------------------

    private UpdateManifest ManifestFor(byte[] file, params string[] urls) =>
        new(new Version(3, 11, 0), new Version(0, 0, 0), DateTime.Now, Hex(file), file.Length, urls, "", "");

    private static UpdateDownloader Loader(FakeNet net) => new(net.Client())
    {
        HeaderTimeout = TimeSpan.FromMilliseconds(300), StallTimeout = TimeSpan.FromMilliseconds(300),
    };

    [Fact]
    public async Task A_download_is_verified_and_moved_into_place()
    {
        var net = new FakeNet().Serve("https://a.example/s.exe", Installer);
        var file = await Loader(net).DownloadAsync(ManifestFor(Installer, "https://a.example/s.exe"), ["https://a.example/s.exe"], _dir, null, default);
        Assert.Equal(Installer, File.ReadAllBytes(file));
        Assert.False(File.Exists(file + ".part"));
    }

    [Fact]
    public async Task A_mirror_serving_other_bytes_is_discarded_and_the_next_address_is_used()
    {
        var evil = Installer.Reverse().ToArray();
        var net = new FakeNet().Serve("https://evil.example/s.exe", evil).Serve("https://good.example/s.exe", Installer);
        var file = await Loader(net).DownloadAsync(ManifestFor(Installer, "x"), ["https://evil.example/s.exe", "https://good.example/s.exe"], _dir, null, default);
        Assert.Equal(Installer, File.ReadAllBytes(file));
    }

    [Fact]
    public async Task If_every_address_serves_wrong_bytes_nothing_is_left_behind()
    {
        var net = new FakeNet().Serve("https://evil.example/s.exe", Installer.Reverse().ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(() => Loader(net).DownloadAsync(ManifestFor(Installer, "x"), ["https://evil.example/s.exe"], _dir, null, default));
        Assert.Empty(Directory.GetFiles(_dir, "ShabpaSetup-*"));
    }

    [Fact]
    public async Task A_404_on_the_first_address_moves_on()
    {
        var net = new FakeNet().Status("https://a.example/s.exe", HttpStatusCode.NotFound).Serve("https://b.example/s.exe", Installer);
        var file = await Loader(net).DownloadAsync(ManifestFor(Installer, "x"), ["https://a.example/s.exe", "https://b.example/s.exe"], _dir, null, default);
        Assert.Equal(Installer.Length, new FileInfo(file).Length);
    }

    [Fact]
    public async Task A_hanging_server_is_abandoned_after_the_header_timeout()
    {
        var net = new FakeNet().Hang("https://a.example/s.exe").Serve("https://b.example/s.exe", Installer);
        var file = await Loader(net).DownloadAsync(ManifestFor(Installer, "x"), ["https://a.example/s.exe", "https://b.example/s.exe"], _dir, null, default);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task A_partial_file_is_resumed_with_a_range_request()
    {
        var net = new FakeNet().Serve("https://a.example/s.exe", Installer);
        var m = ManifestFor(Installer, "x");
        File.WriteAllBytes(Path.Combine(_dir, "ShabpaSetup-3.11.0.exe.part"), Installer[..2000]);
        var file = await Loader(net).DownloadAsync(m, ["https://a.example/s.exe"], _dir, null, default);
        Assert.Equal(Installer, File.ReadAllBytes(file));
        Assert.Equal(2000, net.Requests.Single().Headers.Range!.Ranges.Single().From);
    }

    [Fact]
    public async Task A_server_that_ignores_range_restarts_cleanly()
    {
        var net = new FakeNet().Serve("https://a.example/s.exe", Installer, honorRange: false);
        File.WriteAllBytes(Path.Combine(_dir, "ShabpaSetup-3.11.0.exe.part"), Installer[..2000]);
        var file = await Loader(net).DownloadAsync(ManifestFor(Installer, "x"), ["https://a.example/s.exe"], _dir, null, default);
        Assert.Equal(Installer, File.ReadAllBytes(file));
    }

    [Fact]
    public async Task A_stalled_mirror_leaves_a_partial_file_that_the_next_mirror_resumes()
    {
        var net = new FakeNet();
        net.Routes["https://a.example/s.exe"] = (_, _) =>
        {
            var content = new StreamContent(new StallStream(Installer[..1500]));
            content.Headers.ContentLength = Installer.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        };
        net.Serve("https://b.example/s.exe", Installer);
        var progress = new List<double>();
        var file = await Loader(net).DownloadAsync(ManifestFor(Installer, "x"), ["https://a.example/s.exe", "https://b.example/s.exe"], _dir, progress.Add, default);
        Assert.Equal(Installer, File.ReadAllBytes(file));
        Assert.Equal(1500, net.Requests.Last().Headers.Range!.Ranges.Single().From);
        Assert.Equal(1.0, progress.Last());
    }

    [Fact]
    public async Task A_file_bigger_than_the_signed_size_is_refused()
    {
        var bigger = Installer.Concat(new byte[500]).ToArray();
        var net = new FakeNet().Serve("https://a.example/s.exe", bigger);
        await Assert.ThrowsAsync<InvalidDataException>(() => Loader(net).DownloadAsync(ManifestFor(Installer, "x"), ["https://a.example/s.exe"], _dir, null, default));
        Assert.Empty(Directory.GetFiles(_dir, "ShabpaSetup-*.exe"));
    }

    [Fact]
    public async Task Plain_http_download_addresses_are_never_fetched()
    {
        var net = new FakeNet().Serve("http://a.example/s.exe", Installer);
        await Assert.ThrowsAsync<InvalidDataException>(() => Loader(net).DownloadAsync(ManifestFor(Installer, "x"), ["http://a.example/s.exe"], _dir, null, default));
        Assert.Empty(net.Requests);
    }

    [Fact]
    public async Task Cancelling_stops_the_download_with_a_cancellation_not_a_failure()
    {
        var net = new FakeNet().Hang("https://a.example/s.exe");
        using var cts = new CancellationTokenSource(50);
        var loader = new UpdateDownloader(net.Client()) { HeaderTimeout = TimeSpan.FromSeconds(30) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loader.DownloadAsync(ManifestFor(Installer, "x"), ["https://a.example/s.exe"], _dir, null, cts.Token));
    }

    // ---- offline bundles ----------------------------------------------------------------------

    private string MakeFolder(string version = "3.11.0", byte[]? installer = null, string? sig = null)
    {
        var folder = Path.Combine(_dir, "bundle-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        var json = Json(version, urls: "\"https://a.example/s.exe\"");
        File.WriteAllText(Path.Combine(folder, "latest.json"), json);
        File.WriteAllText(Path.Combine(folder, "latest.json.sig"), sig ?? Sign(json));
        File.WriteAllBytes(Path.Combine(folder, "ShabpaSetup.exe"), installer ?? Installer);
        return folder;
    }

    [Fact]
    public void A_good_bundle_folder_is_accepted()
    {
        var p = UpdateBundle.Open(MakeFolder(), Path.Combine(_dir, "w"), Now, _pub);
        Assert.Equal(new Version(3, 11, 0), p.Manifest.Version);
        Assert.EndsWith("ShabpaSetup.exe", p.InstallerPath);
    }

    [Fact]
    public void A_swapped_installer_in_a_bundle_fails_the_checksum()
    {
        var ex = Assert.Throws<UpdateRejectedException>(() => UpdateBundle.Open(MakeFolder(installer: Installer.Reverse().ToArray()), "w", Now, _pub));
        Assert.Equal("sha", ex.Reason);
    }

    [Fact]
    public void A_bundle_with_a_forged_signature_is_refused()
    {
        var ex = Assert.Throws<UpdateRejectedException>(() => UpdateBundle.Open(MakeFolder(sig: Convert.ToBase64String(new byte[70])), "w", Now, _pub));
        Assert.Equal("signature", ex.Reason);
    }

    [Fact]
    public void An_old_or_equal_bundle_is_a_downgrade()
    {
        Assert.Equal("downgrade", Assert.Throws<UpdateRejectedException>(() => UpdateBundle.Open(MakeFolder("3.9.0"), "w", Now, _pub)).Reason);
        Assert.Equal("downgrade", Assert.Throws<UpdateRejectedException>(() => UpdateBundle.Open(MakeFolder("3.10.0"), "w", Now, _pub)).Reason);
    }

    [Fact]
    public void A_zip_bundle_is_extracted_and_checked_ignoring_other_entries_and_paths()
    {
        var folder = MakeFolder();
        var zip = Path.Combine(_dir, "b.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var f in new[] { "latest.json", "latest.json.sig", "ShabpaSetup.exe" }) z.CreateEntryFromFile(Path.Combine(folder, f), "sub/" + f);
            z.CreateEntryFromFile(Path.Combine(folder, "latest.json"), "../../escape.txt");
        }
        var work = Path.Combine(_dir, "extract");
        var p = UpdateBundle.Open(zip, work, Now, _pub);
        Assert.StartsWith(work, p.InstallerPath);
        Assert.False(File.Exists(Path.Combine(_dir, "..", "escape.txt")));
        Assert.Equal(3, Directory.GetFiles(work).Length);
    }

    [Fact]
    public void An_incomplete_bundle_is_refused()
    {
        var folder = MakeFolder();
        File.Delete(Path.Combine(folder, "latest.json.sig"));
        Assert.Equal("parse", Assert.Throws<UpdateRejectedException>(() => UpdateBundle.Open(folder, "w", Now, _pub)).Reason);
        Assert.Equal("parse", Assert.Throws<UpdateRejectedException>(() => UpdateBundle.Open(Path.Combine(_dir, "nothing.txt"), "w", Now, _pub)).Reason);
    }

    [Fact]
    public void An_installer_next_to_a_signed_manifest_is_verified_and_alone_it_is_not()
    {
        var folder = MakeFolder();
        Assert.NotNull(UpdateBundle.FromInstallerWithSiblingManifest(Path.Combine(folder, "ShabpaSetup.exe"), Now, _pub));
        var lone = Path.Combine(_dir, "lone");
        Directory.CreateDirectory(lone);
        File.WriteAllBytes(Path.Combine(lone, "ShabpaSetup.exe"), Installer);
        Assert.Null(UpdateBundle.FromInstallerWithSiblingManifest(Path.Combine(lone, "ShabpaSetup.exe"), Now, _pub));
    }

    [Theory]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789", true)]
    [InlineData("  abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789  ShabpaSetup.exe\r\n", true)]
    [InlineData("abcdef", false)]
    [InlineData("", false)]
    [InlineData("zzzdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", false)]
    public void Typed_checksums_are_normalised(string text, bool valid) => Assert.Equal(valid, UpdateBundle.NormalizeHash(text) is not null);

    [Fact]
    public void A_file_is_compared_with_a_typed_checksum()
    {
        var file = Path.Combine(_dir, "ShabpaSetup.exe");
        File.WriteAllBytes(file, Installer);
        Assert.True(UpdateBundle.MatchesHash(file, Hex(Installer).ToUpperInvariant() + "  ShabpaSetup.exe"));
        Assert.False(UpdateBundle.MatchesHash(file, new string('0', 64)));
        Assert.False(UpdateBundle.MatchesHash(file, "nope"));
        Assert.False(UpdateBundle.MatchesHash(file + ".missing", Hex(Installer)));
    }

    // ---- the Updater on top -------------------------------------------------------------------

    [Fact]
    public async Task The_updater_offers_a_verified_update_and_remembers_the_working_source()
    {
        var json = Json("99.0.0");
        var net = Mirror(new FakeNet(), Site, json);
        var settings = new Settings();
        var saved = 0;
        var updater = new Updater(() => settings, () => new HttpClient(net) { Timeout = Timeout.InfiniteTimeSpan }, _pub) { Persist = () => saved++ };
        var info = await updater.CheckAsync();
        Assert.Equal(new Version(99, 0, 0), info!.Version);
        Assert.Equal(UpdateState.Available, updater.State);
        Assert.Equal("shabpa.ir", settings.LastUpdateSource);
        Assert.Equal(1, saved);
        Assert.False(updater.AllSourcesFailed);
        Assert.Equal(Hex(Installer), info.Sha256);
    }

    [Fact]
    public async Task When_every_source_fails_the_updater_shows_the_offline_state()
    {
        var updater = new Updater(() => new Settings(), () => new HttpClient(new FakeNet()) { Timeout = Timeout.InfiniteTimeSpan }, _pub);
        Assert.Null(await updater.CheckAsync());
        Assert.Equal(UpdateState.Error, updater.State);
        Assert.True(updater.AllSourcesFailed);
        Assert.NotEmpty(updater.Error);
    }

    [Fact]
    public async Task The_extra_mirror_from_settings_is_part_of_the_walk()
    {
        var net = Mirror(new FakeNet(), "https://mirror.example.ir/shabpa/latest.json", Json("99.0.0"));
        var settings = new Settings { ExtraMirrorUrl = "https://mirror.example.ir/shabpa" };
        var updater = new Updater(() => settings, () => new HttpClient(net) { Timeout = Timeout.InfiniteTimeSpan }, _pub);
        Assert.NotNull(await updater.CheckAsync());
        Assert.Equal("custom", settings.LastUpdateSource);
    }

    [Fact]
    public void Install_from_file_refuses_a_file_that_is_not_the_shabpa_installer()
    {
        var file = Path.Combine(_dir, "ShabpaSetup.exe");
        File.WriteAllBytes(file, Installer);
        var updater = new Updater(() => new Settings(), null, _pub);
        Assert.NotNull(updater.InstallLocal(file, Hex(Installer), elevated: true));
        Assert.True(File.Exists(file)); // a mismatch never deletes the person's file
    }

    [Fact]
    public void Rejections_have_readable_messages_in_both_languages()
    {
        foreach (var reason in new[] { "signature", "no-key", "sha", "downgrade", "min-app", "size", "scheme", "parse" })
        {
            var ex = new UpdateRejectedException(reason, "x");
            Assert.NotEmpty(ex.Friendly);
        }
    }
}
