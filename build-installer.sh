#!/bin/bash
# Builds installer/ShabpaSetup.exe: publishes the app, packs it into one Brotli payload and embeds that in the setup.
set -e
cd "$(dirname "$0")"
VERSION=3.3.3
APP=obj/installer/app
rm -rf obj/installer && mkdir -p obj/installer
dotnet publish src/Shabpa/Shabpa.csproj -c Release -r win-x64 --self-contained false -p:DebugType=none -o "$APP" -nologo -v q
# Symbol-reading helper the ETW library ships but Shabpa never uses (it only reads live network events).
rm -f "$APP/amd64/msdia140.dll"
dotnet run --project tools/Packer -c Release -- "$APP" "$VERSION" obj/installer/payload.bin
# The installer is a .NET Framework 4.8 exe (built into Windows): a single file, nothing to install first.
dotnet build src/Setup/Setup.csproj -c Release -nologo -v q -p:PayloadFile="$(cygpath -w "$PWD/obj/installer/payload.bin")"
mkdir -p installer
cp src/Setup/bin/Release/net48/ShabpaSetup.exe installer/ShabpaSetup.exe
# Published next to the installer so anyone can check the download is the original file.
(cd installer && sha256sum ShabpaSetup.exe > ShabpaSetup.exe.sha256)
ls -la installer/
cat installer/ShabpaSetup.exe.sha256
