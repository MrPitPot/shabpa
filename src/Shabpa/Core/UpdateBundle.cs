using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Shabpa.Core;

/// <summary>Offline updates: an installer plus its signed latest.json (a .zip bundle, or the three files in one folder,
/// e.g. from a USB stick or a Telegram message), or a bare installer checked against a hash the person typed.</summary>
public static class UpdateBundle
{
    public const string ManifestName = "latest.json", SignatureName = "latest.json.sig";

    public sealed record Prepared(UpdateManifest Manifest, string InstallerPath);

    /// <summary>Opens a .zip bundle (extracted into <paramref name="work"/>) or a folder, verifies signature, version,
    /// size and SHA-256 of the installer inside. Throws <see cref="UpdateRejectedException"/> otherwise.</summary>
    public static Prepared Open(string path, string work, Version current, string publicKey)
    {
        string folder;
        if (Directory.Exists(path)) folder = path;
        else if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) folder = Extract(path, work);
        else throw new UpdateRejectedException("parse", "Pick a .zip update bundle or a folder with the installer and latest.json.");
        return FromFolder(folder, current, publicKey);
    }

    /// <summary>Folder with ShabpaSetup.exe + latest.json + latest.json.sig.</summary>
    public static Prepared FromFolder(string folder, Version current, string publicKey)
    {
        var manifestFile = Path.Combine(folder, ManifestName);
        var sigFile = Path.Combine(folder, SignatureName);
        var installer = Path.Combine(folder, Updater.SetupName);
        if (!File.Exists(manifestFile) || !File.Exists(sigFile) || !File.Exists(installer))
            throw new UpdateRejectedException("parse", "The bundle must contain ShabpaSetup.exe, latest.json and latest.json.sig.");
        var manifest = UpdateManifestReader.ReadSigned(ReadLimited(manifestFile, UpdateManifestReader.MaxManifestBytes), File.ReadAllText(sigFile),
            publicKey, null, allowLocal: false, requireUrls: false);
        return Check(manifest, installer, current);
    }

    /// <summary>Installer next to a signed manifest (same folder) - the "Install from file" path when the person also copied latest.json.</summary>
    public static Prepared? FromInstallerWithSiblingManifest(string installer, Version current, string publicKey)
    {
        var folder = Path.GetDirectoryName(installer) ?? "";
        if (!File.Exists(Path.Combine(folder, ManifestName)) || !File.Exists(Path.Combine(folder, SignatureName))) return null;
        var manifest = UpdateManifestReader.ReadSigned(ReadLimited(Path.Combine(folder, ManifestName), UpdateManifestReader.MaxManifestBytes),
            File.ReadAllText(Path.Combine(folder, SignatureName)), publicKey, null, false, requireUrls: false);
        return Check(manifest, installer, current);
    }

    private static Prepared Check(UpdateManifest manifest, string installer, Version current)
    {
        if (manifest.Version <= current) throw new UpdateRejectedException("downgrade", "This update is not newer than the installed version.");
        if (manifest.MinApp > current) throw new UpdateRejectedException("min-app", "Install an earlier update first; this one needs a newer base version.");
        var info = new FileInfo(installer);
        if (info.Length > UpdateManifestReader.MaxSetupBytes || (manifest.Size > 0 && info.Length != manifest.Size))
            throw new UpdateRejectedException("size", "The installer is not the announced size.");
        if (UpdateManifestReader.Sha256Hex(installer) != manifest.Sha256)
            throw new UpdateRejectedException("sha", "The installer does not match the signed checksum.");
        return new Prepared(manifest, installer);
    }

    /// <summary>Accepts "hash", "HASH  ShabpaSetup.exe" (sha256sum output) or hash with spaces/newlines.</summary>
    public static string? NormalizeHash(string text)
    {
        foreach (var token in text.Split([' ', '\t', '\r', '\n', '*'], StringSplitOptions.RemoveEmptyEntries))
            if (token.Length == 64 && token.All(Uri.IsHexDigit)) return token.ToLowerInvariant();
        return null;
    }

    public static bool MatchesHash(string file, string hashText)
    {
        var expected = NormalizeHash(hashText);
        if (expected is null || !File.Exists(file) || new FileInfo(file).Length > UpdateManifestReader.MaxSetupBytes) return false;
        return UpdateManifestReader.Sha256Hex(file) == expected;
    }

    private static byte[] ReadLimited(string file, int max)
    {
        if (new FileInfo(file).Length > max) throw new UpdateRejectedException("size", "The manifest file is too large.");
        return File.ReadAllBytes(file);
    }

    private static string Extract(string zip, string work)
    {
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        using var archive = ZipFile.OpenRead(zip);
        var wanted = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            [ManifestName] = UpdateManifestReader.MaxManifestBytes, [SignatureName] = 4096, [Updater.SetupName] = UpdateManifestReader.MaxSetupBytes,
        };
        foreach (var entry in archive.Entries)
        {
            // Only the three expected names, taken by file name (never the entry's path): no path tricks.
            var name = entry.Name;
            if (!wanted.TryGetValue(name, out var cap)) continue;
            if (entry.Length > cap) throw new UpdateRejectedException("size", "A file in the bundle is too large.");
            using var input = entry.Open();
            using var output = File.Create(Path.Combine(work, wanted.Keys.First(k => k.Equals(name, StringComparison.OrdinalIgnoreCase))));
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                written += read;
                if (written > cap) throw new UpdateRejectedException("size", "A file in the bundle is too large.");
                output.Write(buffer, 0, read);
            }
        }
        return work;
    }
}
