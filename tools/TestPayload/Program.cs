// Makes a tiny fake payload (3 files, version 3.6.0) for the silent-install harness: dotnet run -- <out payload.bin> <any small exe>
using Shabpa.Setup;
var files = new List<PayloadFile>
{
    new("Shabpa.exe", File.ReadAllBytes(args[1])),
    new("Shabpa.dll", new byte[200000]),
    new("amd64/native.dll", System.Text.Encoding.UTF8.GetBytes("native")),
};
File.WriteAllBytes(args[0], Payload.Pack("3.6.0", files));
