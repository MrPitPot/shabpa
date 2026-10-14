using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Shabpa.Setup;

/// <summary>The app needs the .NET 10 Desktop Runtime. When a PC lacks it, the installer fetches Microsoft's
/// own runtime installer, checks Microsoft's signature on it and runs it silently.</summary>
public static class Runtime
{
    public const int Major = 10;
    /// <summary>Microsoft's permanent link to the latest .NET 10 Windows Desktop Runtime for x64.</summary>
    public const string DownloadUrl = "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe";

    /// <summary>True when a .NET 10 Windows Desktop Runtime is installed for x64.</summary>
    public static bool IsInstalled(string? dotnetRoot = null)
    {
        dotnetRoot ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
        var shared = Path.Combine(dotnetRoot, "shared", "Microsoft.WindowsDesktop.App");
        if (!Directory.Exists(shared)) return false;
        return Directory.GetDirectories(shared)
            .Select(Path.GetFileName)
            .Any(name => name is not null && name.StartsWith(Major + ".", StringComparison.Ordinal) &&
                         Version.TryParse(name.Split('-')[0], out _));
    }

    /// <summary>Downloads, verifies and installs the runtime. <paramref name="progress"/> gets 0..1 for the
    /// download; returns an error message or null.</summary>
    public static async Task<string?> InstallAsync(Action<double> progress, Func<string, string, string> t)
    {
        var file = Path.Combine(Path.GetTempPath(), "shabpa-dotnet-desktop-runtime.exe");
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) })
            using (var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 60_000_000;
                using var input = await response.Content.ReadAsStreamAsync();
                using var output = File.Create(file);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await output.WriteAsync(buffer, 0, read);
                    done += read;
                    progress(Math.Min(1, (double)done / total));
                }
            }
        }
        catch (Exception ex)
        {
            return t($"دانلود ‎.NET از سایت مایکروسافت انجام نشد ({ex.Message}). اینترنت یا فیلترشکن رو چک کن و دوباره امتحان کن.",
                $"Could not download .NET from Microsoft ({ex.Message}). Check the internet connection and try again.");
        }

        if (!IsSignedByMicrosoft(file))
        {
            TryDelete(file);
            return t("فایل دانلودشده امضای معتبر مایکروسافت رو نداشت و اجرا نشد.", "The downloaded file did not carry a valid Microsoft signature and was not run.");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, "/install /quiet /norestart") { UseShellExecute = false, CreateNoWindow = true })!;
            await Task.Run(() => process.WaitForExit());
            // 0 = done, 3010/1641 = done but Windows wants a restart (not needed for the app to start).
            return process.ExitCode is 0 or 3010 or 1641 || IsInstalled()
                ? null
                : t($"نصب ‎.NET انجام نشد (کد {process.ExitCode}).", $".NET installation failed (code {process.ExitCode}).");
        }
        finally
        {
            TryDelete(file);
        }
    }

    /// <summary>A valid Authenticode signature (checked by Windows) whose signer is Microsoft Corporation.</summary>
    public static bool IsSignedByMicrosoft(string file)
    {
        if (!WinTrust.Verify(file)) return false;
        try
        {
            var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
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
