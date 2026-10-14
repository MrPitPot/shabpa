using Shabpa.Setup;

// Packer <published app folder> <version> <output payload.bin>
if (args.Length != 3)
{
    Console.Error.WriteLine("usage: Packer <app folder> <version> <payload.bin>");
    return 2;
}
var files = Directory.EnumerateFiles(args[0], "*", SearchOption.AllDirectories)
    .Order(StringComparer.Ordinal)
    .Select(f => new PayloadFile(Path.GetRelativePath(args[0], f), File.ReadAllBytes(f)))
    .ToList();
var payload = Payload.Pack(args[1], files);
File.WriteAllBytes(args[2], payload);
var raw = files.Sum(f => (long)f.Data.Length);
Console.WriteLine($"{files.Count} files, {raw / 1024.0 / 1024:0.00} MB -> {payload.Length / 1024.0 / 1024:0.00} MB ({100.0 * payload.Length / raw:0}%)");
return 0;
