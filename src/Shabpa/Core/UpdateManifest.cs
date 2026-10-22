using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Shabpa.Core;

/// <summary>The small signed file (latest.json) every update mirror serves next to the installer.</summary>
public sealed record UpdateManifest(
    Version Version, Version MinApp, DateTime Published, string Sha256, long Size,
    IReadOnlyList<string> Urls, string NotesFa, string NotesEn)
{
    public string Notes => L.En ? (NotesEn.Length > 0 ? NotesEn : NotesFa) : (NotesFa.Length > 0 ? NotesFa : NotesEn);
}

/// <summary>Why a manifest or installer was refused.</summary>
public sealed class UpdateRejectedException(string reason, string message) : Exception(message)
{
    /// <summary>Short machine-readable code: signature, parse, sha, size, scheme, downgrade, min-app, no-key.</summary>
    public string Reason { get; } = reason;

    /// <summary>The reason in the app's language, for the screen.</summary>
    public string Friendly => Reason switch
    {
        "signature" => L.T("امضای فایل به‌روزرسانی معتبر نیست؛ نصب نشد.", "The update's signature is not valid; nothing was installed."),
        "no-key" => L.T("کلید امضای نسخه‌ها هنوز در این نسخه نیست؛ به‌روزرسانی از آینه ممکن نیست.", "This build has no release key yet, so mirror updates cannot be verified."),
        "sha" => L.T("فایل با چک‌سام منتشرشده یکی نیست.", "The file does not match the published checksum."),
        "downgrade" => L.T("این نسخه از نسخه‌ی نصب‌شده جدیدتر نیست.", "This is not newer than the installed version."),
        "min-app" => L.T("برای این نسخه اول باید یک نسخه‌ی میانی نصب بشه.", "An intermediate version must be installed first."),
        "size" => L.T("اندازه‌ی فایل درست نیست.", "The file size is not acceptable."),
        "scheme" => L.T("آدرس باید HTTPS باشه.", "The address must be HTTPS."),
        _ => L.T("فایل به‌روزرسانی معتبر نیست.", "The update file is not valid."),
    };
}

/// <summary>The public half of the release-signing key. The private half is never in the repository
/// (see docs/UPDATES.md). Mirrors are untrusted: a manifest only counts if this key signed its exact bytes.</summary>
public static class UpdateKeys
{
    /// <summary>Base64 of the DER SubjectPublicKeyInfo of the ECDSA P-256 release key.
    /// Empty until the owner generates the key; while empty every mirror manifest is rejected (fails closed).</summary>
    public const string PublicKeyBase64 = "";
}

public static class UpdateManifestReader
{
    public const int MaxManifestBytes = 128 * 1024;
    public const long MaxSetupBytes = 200L << 20;

    /// <summary>True when <paramref name="signature"/> (base64 DER ECDSA over SHA-256) was made over exactly these bytes.</summary>
    public static bool VerifySignature(byte[] manifest, string signature, string publicKeyBase64)
    {
        if (publicKeyBase64.Length == 0) return false;
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            var sig = Convert.FromBase64String(string.Concat(signature.Where(c => !char.IsWhiteSpace(c))));
            return key.VerifyData(manifest, sig, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Checks the signature, then parses. Throws <see cref="UpdateRejectedException"/> for anything unacceptable.</summary>
    public static UpdateManifest ReadSigned(byte[] manifest, string signature, string publicKeyBase64, Uri? baseUri, bool allowLocal, bool requireUrls = true)
    {
        if (manifest.Length is 0 or > MaxManifestBytes) throw new UpdateRejectedException("size", "The update manifest has an unreasonable size.");
        if (publicKeyBase64.Length == 0) throw new UpdateRejectedException("no-key", "No release key is configured, so mirror updates cannot be verified.");
        if (!VerifySignature(manifest, signature, publicKeyBase64)) throw new UpdateRejectedException("signature", "The update manifest signature is not valid.");
        return Parse(manifest, baseUri, allowLocal, requireUrls);
    }

    /// <summary>Parses a manifest (no signature check; callers must have verified or trust the origin).</summary>
    public static UpdateManifest Parse(byte[] manifest, Uri? baseUri, bool allowLocal, bool requireUrls = true)
    {
        try
        {
            using var doc = JsonDocument.Parse(manifest);
            var root = doc.RootElement;
            var version = Updater.ParseVersion(root.GetProperty("version").GetString() ?? "")
                ?? throw new UpdateRejectedException("parse", "The manifest has no valid version.");
            var min = root.TryGetProperty("minAppVersion", out var m) && Updater.ParseVersion(m.GetString() ?? "") is { } mv ? mv : new Version(0, 0, 0);
            var sha = (root.GetProperty("sha256").GetString() ?? "").Trim().ToLowerInvariant();
            if (sha.Length != 64 || !sha.All(Uri.IsHexDigit)) throw new UpdateRejectedException("sha", "The manifest checksum is not a SHA-256.");
            var size = root.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            if (size < 0 || size > MaxSetupBytes) throw new UpdateRejectedException("size", "The installer size in the manifest is not acceptable.");
            var published = root.TryGetProperty("published", out var p) && p.TryGetDateTime(out var at) ? at.ToLocalTime() : DateTime.Now;

            var urls = new List<string>();
            if (root.TryGetProperty("urls", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var item in list.EnumerateArray())
                    if (Resolve(item.GetString() ?? "", baseUri, allowLocal) is { } u && !urls.Contains(u)) urls.Add(u);
            if (requireUrls && urls.Count == 0) throw new UpdateRejectedException("scheme", "The manifest lists no usable HTTPS installer address.");

            string fa = "", en = "";
            if (root.TryGetProperty("notes", out var n))
            {
                if (n.ValueKind == JsonValueKind.String) fa = en = n.GetString() ?? "";
                else if (n.ValueKind == JsonValueKind.Object)
                {
                    fa = n.TryGetProperty("fa", out var f) ? f.GetString() ?? "" : "";
                    en = n.TryGetProperty("en", out var e) ? e.GetString() ?? "" : "";
                }
            }
            return new UpdateManifest(version, min, published, sha, size, urls, fa, en);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new UpdateRejectedException("parse", "The update manifest is malformed.");
        }
    }

    /// <summary>Absolute https URL (relative ones are taken against the manifest's own address). http is refused;
    /// file/UNC only when the caller allows it (tests, USB folders).</summary>
    public static string? Resolve(string url, Uri? baseUri, bool allowLocal)
    {
        if (url.Length == 0 || url.Length > 2048) return null;
        if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri)) return null;
        if (!uri.IsAbsoluteUri)
        {
            if (baseUri is null || !Uri.TryCreate(baseUri, uri, out uri)) return null;
        }
        if (uri.Scheme == Uri.UriSchemeHttps) return uri.AbsoluteUri;
        if (allowLocal && uri.Scheme == Uri.UriSchemeFile) return uri.AbsoluteUri;
        return null;
    }

    public static string Sha256Hex(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string FormatVersion(Version v) => v.ToString(3);
}
