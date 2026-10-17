using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Shabpa.Setup;

/// <summary>The app needs the .NET 10 Desktop Runtime. When a PC lacks it, the installer fetches Microsoft's
/// own runtime installer, checks Microsoft's signature on it and runs it silently.</summary>
public static class Runtime
{
    public const int Major = 10;
    /// <summary>Microsoft's permanent link to the latest .NET 10 Windows Desktop Runtime for x64.</summary>
    public const string DownloadUrl = "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe";

    /// <summary>True when a .NET 10 Windows Desktop Runtime is installed for x64. Without a folder it asks the places the
    /// app's own launcher asks: Program Files\dotnet, DOTNET_ROOT and the registry record the runtime installer leaves.</summary>
    public static bool IsInstalled(string? dotnetRoot = null)
    {
        if (dotnetRoot is not null) return FolderHasRuntime(dotnetRoot);

        switch (TestHooks.Env("SHABPA_SETUP_RUNTIME"))
        {
            case "present": return true;
            case "missing": return false;
        }
        try
        {
            if (FolderHasRuntime(Path.Combine(InstallTarget.ProgramFilesDir(), "dotnet"))) return true;
            var env = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrWhiteSpace(env) && FolderHasRuntime(env!)) return true;
            return RegistryHasRuntime();
        }
        catch (Exception ex)
        {
            SetupLog.Warn("runtime check failed: " + ex.Message);
            return false;
        }
    }

    private static bool FolderHasRuntime(string dotnetRoot)
    {
        try
        {
            var shared = Path.Combine(dotnetRoot, "shared", "Microsoft.WindowsDesktop.App");
            if (!Directory.Exists(shared)) return false;
            return Directory.GetDirectories(shared)
                .Select(Path.GetFileName)
                .Any(name => name is not null && IsDesktopVersion(name));
        }
        catch { return false; }
    }

    private static bool IsDesktopVersion(string name) =>
        name.StartsWith(Major + ".", StringComparison.Ordinal) && Version.TryParse(name.Split('-')[0], out _);

    /// <summary>HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App lists one value per installed version.</summary>
    private static bool RegistryHasRuntime()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hklm.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App");
        return key is not null && key.GetValueNames().Any(IsDesktopVersion);
    }

    private static string Url => TestHooks.Env("SHABPA_SETUP_RUNTIME_URL") ?? DownloadUrl;

    private static TimeSpan HeaderTimeout =>
        TimeSpan.FromSeconds(int.TryParse(TestHooks.Env("SHABPA_SETUP_HTTP_TIMEOUT_SEC"), out var s) && s > 0 ? s : 60);

    /// <summary>No byte for this long means the connection is dead: give up instead of waiting for the global limit.</summary>
    private static TimeSpan StallTimeout => HeaderTimeout;

    /// <summary>Downloads, verifies and installs the runtime. <paramref name="progress"/> gets 0..1 for the
    /// download; returns an error message or null. Every step goes to the setup log.</summary>
    public static async Task<string?> InstallAsync(Action<double> progress, Func<string, string, string> t)
    {
        var file = Path.Combine(Path.GetTempPath(), "shabpa-dotnet-desktop-runtime.exe");
        SetupLog.Info("runtime: downloading " + Url + " to " + file);
        try
        {
            using var handler = new HttpClientHandler { UseProxy = true, DefaultProxyCredentials = CredentialCache.DefaultCredentials };
            using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(20) })
            {
                using var headerCts = new CancellationTokenSource(HeaderTimeout);
                using var response = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, headerCts.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 60_000_000;
                SetupLog.Info("runtime: connected, " + total + " bytes");
                using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var output = File.Create(file);
                var buffer = new byte[81920];
                long done = 0;
                while (true)
                {
                    var read = await WithStallLimit(input.ReadAsync(buffer, 0, buffer.Length)).ConfigureAwait(false);
                    if (read <= 0) break;
                    await output.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                    done += read;
                    progress(Math.Min(1, (double)done / total));
                }
                SetupLog.Info("runtime: downloaded " + done + " bytes");
            }
        }
        catch (Exception ex)
        {
            SetupLog.Error("runtime: download failed", ex);
            TryDelete(file);
            return t($"دانلود ‎.NET از سایت مایکروسافت انجام نشد. اینترنت یا فیلترشکن رو چک کن و دوباره امتحان کن.\n‎{ex.Message}‎",
                $"Could not download .NET from Microsoft ({ex.Message}). Check the internet connection and try again.");
        }

        if (!IsSignedByMicrosoft(file))
        {
            SetupLog.Error("runtime: the downloaded file has no valid Microsoft signature");
            TryDelete(file);
            return t("فایل دانلودشده امضای معتبر مایکروسافت رو نداشت و اجرا نشد.", "The downloaded file did not carry a valid Microsoft signature and was not run.");
        }

        try
        {
            // 1618 = another installation is running (for instance winget's own dependency step finishing): wait and retry.
            for (var attempt = 1; ; attempt++)
            {
                SetupLog.Info("runtime: running the Microsoft installer (attempt " + attempt + ")");
                using var process = Process.Start(new ProcessStartInfo(file, "/install /quiet /norestart") { UseShellExecute = false, CreateNoWindow = true })!;
                var finished = await Task.Run(() => process.WaitForExit(10 * 60 * 1000)).ConfigureAwait(false);
                if (!finished)
                {
                    try { process.Kill(); } catch { /* gone */ }
                    SetupLog.Error("runtime: the Microsoft installer did not finish in 10 minutes");
                    return t("نصب ‎.NET بیش از حد طول کشید.", ".NET installation took too long.");
                }
                var code = process.ExitCode;
                SetupLog.Info("runtime: installer exit code " + code);
                if (code == 1618 && attempt < 6 && !IsInstalled())
                {
                    await Task.Delay(15000).ConfigureAwait(false);
                    continue;
                }
                // 0 = done, 3010/1641 = done but Windows wants a restart (not needed for the app to start).
                return code is 0 or 3010 or 1641 || IsInstalled()
                    ? null
                    : t($"نصب ‎.NET انجام نشد (کد {code}).", $".NET installation failed (code {code}).");
            }
        }
        catch (Exception ex)
        {
            SetupLog.Error("runtime: could not run the Microsoft installer", ex);
            return t($"نصب ‎.NET انجام نشد: ‎{ex.Message}‎", $".NET installation failed: {ex.Message}");
        }
        finally
        {
            TryDelete(file);
        }
    }

    private static async Task<int> WithStallLimit(Task<int> read)
    {
        var finished = await Task.WhenAny(read, Task.Delay(StallTimeout)).ConfigureAwait(false);
        if (finished != read) throw new TimeoutException("the download stalled for " + (int)StallTimeout.TotalSeconds + " seconds");
        return await read.ConfigureAwait(false);
    }

    /// <summary>A valid Authenticode signature (checked by Windows) whose signer is Microsoft Corporation.</summary>
    public static bool IsSignedByMicrosoft(string file)
    {
        if (!WinTrust.Verify(file)) return false;
        try
        {
            // reading the signer of an Authenticode-signed file has no X509CertificateLoader equivalent
#pragma warning disable SYSLIB0057
            var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
            return certificate.Subject.Contains("O=Microsoft Corporation");
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { /* temp file */ }
    }

    /// <summary>WinVerifyTrust with the standard Authenticode policy and revocation checks off (they can
    /// stall for a long time behind restrictive networks).</summary>
    private static class WinTrust
    {
        private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WINTRUST_DATA data);

        public static bool Verify(string file)
        {
            var fileInfo = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = file };
            var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            try
            {
                Marshal.StructureToPtr(fileInfo, filePtr, false);
                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    dwUIChoice = 2,          // WTD_UI_NONE
                    fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                    dwUnionChoice = 1,       // WTD_CHOICE_FILE
                    pFile = filePtr,
                    dwStateAction = 0,
                    dwProvFlags = 0x80,      // WTD_REVOCATION_CHECK_NONE
                };
                return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data) == 0;
            }
            finally
            {
                Marshal.DestroyStructure<WINTRUST_FILE_INFO>(filePtr);
                Marshal.FreeHGlobal(filePtr);
            }
        }
    }
}
