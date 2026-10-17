using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Shabpa.Setup;

/// <summary>Why a silent run ended. winget (and scripts) only see the number, the log has the story.</summary>
public static class ExitCodes
{
    public const int Ok = 0;
    /// <summary>Anything not classified below.</summary>
    public const int Failed = 1;
    /// <summary>The .NET 10 Desktop Runtime is missing and could not be downloaded, verified or installed.</summary>
    public const int RuntimeMissing = 10;
    /// <summary>Files could not be written (no rights, disk full, locked by something else).</summary>
    public const int WriteFailed = 11;
    /// <summary>The uninstall entry could not be written or removed.</summary>
    public const int RegistryFailed = 12;
    /// <summary>This installer has no payload or it is damaged.</summary>
    public const int PayloadDamaged = 13;
    /// <summary>The uninstall failed.</summary>
    public const int UninstallFailed = 14;
    /// <summary>The watchdog ended a run that took too long.</summary>
    public const int Timeout = 15;
    /// <summary>An exception nobody caught.</summary>
    public const int Unhandled = 16;
}

/// <summary>A failure with a stable exit code.</summary>
public sealed class SetupException : Exception
{
    public int Code { get; }
    public SetupException(int code, string message, Exception? inner = null) : base(message, inner) => Code = code;
}

/// <summary>A persistent text log: %ProgramData%\Shabpa\setup-log.txt, or %TEMP%\shabpa-setup-log.txt when that cannot be
/// written. Written in every mode and for every step. It never throws: a log must not be the reason an install fails.</summary>
public static class SetupLog
{
    private static readonly object Gate = new();
    private static string? _path;
    private const long MaxBytes = 512 * 1024;

    /// <summary>Where the log is being written (null before the first line).</summary>
    public static string? Path => _path;

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : message + Environment.NewLine + ex);

    /// <summary>The first lines of a run: what, where and who, so a log from a stranger's VM is enough to understand it.</summary>
    public static void Begin(string mode, string[] args)
    {
        Info("==== Shabpa Setup " + (typeof(SetupLog).Assembly.GetName().Version?.ToString(3) ?? "?") + " (" + mode + ") ====");
        Safe(() => Info("args: " + (args.Length == 0 ? "(none)" : string.Join(" ", args)) + "   cmdline: " + Environment.CommandLine));
        Safe(() => Info("os: " + Environment.OSVersion + "  64-bit os=" + Environment.Is64BitOperatingSystem + "  64-bit process=" + Environment.Is64BitProcess +
                        "  clr=" + Environment.Version + "  interactive=" + Environment.UserInteractive));
        Safe(() => Info("user: " + Environment.UserDomainName + "\\" + Environment.UserName + "  admin=" + IsAdministrator() + "  session=" + Process.GetCurrentProcess().SessionId));
        Safe(() => Info("exe: " + Process.GetCurrentProcess().MainModule?.FileName));
        Safe(() => Info("programfiles=" + InstallTarget.ProgramFilesDir() + "  temp=" + System.IO.Path.GetTempPath()));
    }

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static void Safe(Action a)
    {
        try { a(); } catch (Exception ex) { Write("WARN ", "log helper failed: " + ex.Message); }
    }

    private static void Write(string level, string message)
    {
        var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + Environment.CurrentManagedThreadId + "] " + level + " " + message + Environment.NewLine;
        lock (Gate)
        {
            if (_path is not null && TryAppend(_path, line)) return;
            foreach (var candidate in Candidates())
            {
                if (TryAppend(candidate, line)) { _path = candidate; return; }
            }
        }
    }

    private static string?[] Candidates()
    {
        string? over = null;
        string? programData = null, temp = null;
#if DEBUG || SHABPA_TESTHOOKS
        over = TestHooks.Env("SHABPA_SETUP_LOG");
#endif
        try { programData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Shabpa", "setup-log.txt"); } catch { }
        try { temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shabpa-setup-log.txt"); } catch { }
        if (over is not null) return [over, temp];
        return [programData, temp];
    }

    private static bool TryAppend(string? path, string line)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var old = path + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(path, old);
                }
            }
            catch { /* rotation is best effort */ }
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            var bytes = new UTF8Encoding(false).GetBytes(line);
            stream.Write(bytes, 0, bytes.Length);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>Test-only switches. They exist only in Debug builds and in builds made with -p:DefineConstants=SHABPA_TESTHOOKS;
/// the released installer contains none of them (every accessor returns null there).</summary>
internal static class TestHooks
{
    public static string? Env(string name)
    {
#if DEBUG || SHABPA_TESTHOOKS
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v;
#else
        _ = name;
        return null;
#endif
    }

    /// <summary>Everything is redirected into this folder and an HKCU key; nothing real is touched.</summary>
    public static string? Root => Env("SHABPA_SETUP_ROOT");
    public static bool Active => Root is not null;
}
