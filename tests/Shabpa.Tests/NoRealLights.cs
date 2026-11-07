using System.IO;
using System.Runtime.CompilerServices;
using Shabpa.Core;

namespace Shabpa.Tests;

/// <summary>No test may ever light the real lights: the lighting server is only started from a folder that has it, and on a PC with Shabpa installed that is the real one (a page test that opened the
/// Lighting page started it and every device on the desk lit up). Every test run points the folder at one that is empty, before any test starts; the tests that want a server use a fake one.</summary>
internal static class NoRealLights
{
    [ModuleInitializer]
    internal static void Init() => OpenRgbHost.Folder = Path.Combine(Path.GetTempPath(), "shabpa-tests-without-openrgb-" + Environment.ProcessId);
}
