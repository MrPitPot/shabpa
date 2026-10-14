using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Shabpa.Setup;

public sealed record PayloadFile(string Path, byte[] Data);

/// <summary>The app's files as one solid compressed stream: compressing them together lets content shared
/// across DLLs shrink, which per-file zip entries cannot do. Deflate, because the installer runs on
/// .NET Framework 4.8 (built into Windows) and that is what it can read.</summary>
public static class Payload
{
    private const string Magic = "SHPK3";

    public static byte[] Pack(string version, IEnumerable<PayloadFile> files)
    {
        var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, Encoding.UTF8, leaveOpen: true))
        {
            var list = files.ToList();
            writer.Write(version);
            writer.Write(list.Count);
            foreach (var file in list)
            {
                writer.Write(file.Path.Replace('\\', '/'));
                writer.Write(file.Data.Length);
                writer.Write(file.Data);
            }
        }
        var plain = raw.ToArray();

        var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(plain.Length);
            writer.Write(Hash(plain));
        }
#if NET6_0_OR_GREATER
        const CompressionLevel level = CompressionLevel.SmallestSize;
#else
        const CompressionLevel level = CompressionLevel.Optimal;
#endif
        using (var deflate = new DeflateStream(output, level, leaveOpen: true))
            deflate.Write(plain, 0, plain.Length);
        return output.ToArray();
    }

    public static (string version, List<PayloadFile> files) Unpack(byte[] payload)
    {
        try
        {
            return UnpackUnchecked(payload);
        }
        catch (Exception ex) when (ex is EndOfStreamException or ArgumentException or OverflowException or OutOfMemoryException)
        {
            throw new InvalidDataException("the installer is damaged", ex);
        }
    }

    private static (string version, List<PayloadFile> files) UnpackUnchecked(byte[] payload)
    {
        var input = new MemoryStream(payload);
        var reader = new BinaryReader(input, Encoding.UTF8);
        if (reader.ReadString() != Magic) throw new InvalidDataException("not a Shabpa payload");
        var length = reader.ReadInt32();
        if (length < 0 || length > 1 << 30) throw new InvalidDataException("the installer is damaged");
        var hash = reader.ReadBytes(32);

        var plain = new byte[length];
        using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
        {
            var read = 0;
            while (read < length)
            {
                var n = deflate.Read(plain, read, length - read);
                if (n <= 0) throw new InvalidDataException("the installer is damaged (truncated)");
                read += n;
            }
        }
        if (!Hash(plain).SequenceEqual(hash)) throw new InvalidDataException("the installer is damaged (checksum mismatch)");

        var body = new BinaryReader(new MemoryStream(plain), Encoding.UTF8);
        var version = body.ReadString();
        var count = body.ReadInt32();
        var files = new List<PayloadFile>(count);
        for (var i = 0; i < count; i++)
        {
            var path = body.ReadString();
            // Never write outside the install folder, whatever the archive says.
            if (path.Length == 0 || System.IO.Path.IsPathRooted(path) || path.IndexOf(':') >= 0 || path.Split('/').Contains(".."))
                throw new InvalidDataException($"bad path in payload: {path}");
            files.Add(new PayloadFile(path, body.ReadBytes(body.ReadInt32())));
        }
        return (version, files);
    }

    private static byte[] Hash(byte[] data)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(data);
    }
}
