namespace Shabpa.Core;

public enum RuleState
{
    /// <summary>No rule is active.</summary>
    Off,
    /// <summary>Waiting for the transfer / program to show up before counting.</summary>
    Waiting,
    /// <summary>The condition is not met right now.</summary>
    NotMet,
    /// <summary>An exception (recording, game) is holding the rule back.</summary>
    Blocked,
    /// <summary>The condition holds and the clock is running.</summary>
    Counting,
    /// <summary>The condition held long enough: run the action.</summary>
    Fire,
}

public readonly record struct RuleStatus(RuleState State, TimeSpan Elapsed, TimeSpan Hold, string Text)
{
    public double Progress => Hold <= TimeSpan.Zero ? 0 : Math.Clamp(Elapsed / Hold, 0, 1);
    public TimeSpan Remaining => Hold - Elapsed < TimeSpan.Zero ? TimeSpan.Zero : Hold - Elapsed;
}

/// <summary>Evaluates the active conditional rule against sensor snapshots. Pure logic.</summary>
public sealed class RuleEngine
{
    /// <summary>A gap this long between evaluations means the machine slept; start over.</summary>
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(30);

    private DateTime? _since;
    private DateTime? _last;
    private bool _armed;

    public Rule? Rule { get; private set; }
    public RuleStatus Status { get; private set; } = new(RuleState.Off, default, default, "");

    public void Activate(Rule rule)
    {
        Rule = rule.Clone();
        _since = null;
        _last = null;
        _armed = false;
        Status = new RuleStatus(RuleState.NotMet, default, Hold(Rule), "");
    }

    public void Deactivate()
    {
        Rule = null;
        _since = null;
        Status = new RuleStatus(RuleState.Off, default, default, "");
    }

    private static TimeSpan Hold(Rule r) => TimeSpan.FromMinutes(Math.Max(0, r.HoldMinutes));

    public RuleStatus Evaluate(SensorSnapshot s, DateTime now)
    {
        var rule = Rule;
        if (rule is null) return Status = new RuleStatus(RuleState.Off, default, default, "");

        var hold = Hold(rule);
        if (_last is { } last && (now - last > MaxGap || now < last)) _since = null;
        _last = now;

        if ((rule.ExceptRecording && s.Recording) || (rule.ExceptGame && s.GameRunning))
        {
            _since = null;
            var why = rule.ExceptRecording && s.Recording
                ? L.T("ضبط صفحه فعاله؛ شمارش متوقف شده.", "Screen recording is on; the count is paused.")
                : L.T("بازی در حال اجراست؛ شمارش متوقف شده.", "A game is running; the count is paused.");
            return Status = new RuleStatus(RuleState.Blocked, default, hold, why);
        }

        // Idle is special: Windows already tracks how long the user has been away.
        if (rule.Kind == RuleKind.Idle)
        {
            var idle = TimeSpan.FromSeconds(s.IdleSeconds);
            if (idle >= hold)
                return Status = new RuleStatus(RuleState.Fire, hold, hold, L.T("سیستم به اندازه‌ی کافی بی‌استفاده مونده.", "The PC has been idle long enough."));
            return Status = new RuleStatus(idle.TotalSeconds < 5 ? RuleState.NotMet : RuleState.Counting, idle, hold,
                idle.TotalSeconds < 5
                    ? L.T("داری از سیستم استفاده می‌کنی.", "You are using the PC.")
                    : L.T($"{Fa.Duration(idle)} بدون موس و کیبورد.", $"{Fa.Duration(idle)} without mouse or keyboard."));
        }

        bool met;
        string text;
        switch (rule.Kind)
        {
            case RuleKind.Network:
            {
                var speed = rule.Upload ? s.UpMbps : s.DownMbps;
                var what = rule.Upload ? L.T("آپلود", "upload") : L.T("دانلود", "download");
                met = speed < rule.ThresholdMbps;
                if (!met) _armed = true;
                if (rule.WaitForStart && !_armed)
                    return Status = new RuleStatus(RuleState.Waiting, default, hold,
                        L.T($"منتظرم {what} شروع بشه. سرعت الان {Fa.Speed(speed)} مگابیت.", $"Waiting for the {what} to start. Speed now: {Fa.Speed(speed)} Mbit."));
                text = met
                    ? L.T($"سرعت {what} {Fa.Speed(speed)} مگابیت، زیر آستانه‌ست.", $"The {what} speed is {Fa.Speed(speed)} Mbit, below the threshold.")
                    : L.T($"سرعت {what} {Fa.Speed(speed)} مگابیت، هنوز در جریانه.", $"The {what} speed is {Fa.Speed(speed)} Mbit, still going.");
                break;
            }
            case RuleKind.Load:
            {
                met = s.CpuLoad < rule.LoadPercent && s.GpuLoad < rule.LoadPercent;
                text = met
                    ? L.T($"پردازنده {Fa.N(s.CpuLoad, 0)}٪ و گرافیک {Fa.N(s.GpuLoad, 0)}٪، زیر آستانه‌ست.", $"CPU {Fa.N(s.CpuLoad, 0)}% and GPU {Fa.N(s.GpuLoad, 0)}%, below the threshold.")
                    : L.T($"پردازنده {Fa.N(s.CpuLoad, 0)}٪ و گرافیک {Fa.N(s.GpuLoad, 0)}٪، هنوز مشغوله.", $"CPU {Fa.N(s.CpuLoad, 0)}% and GPU {Fa.N(s.GpuLoad, 0)}%, still busy.");
                break;
            }
            default:
            {
                var name = NormalizeProcess(rule.ProcessName);
                var running = name.Length > 0 && s.Processes.Contains(name);
                if (running) _armed = true;
                if (!_armed)
                    return Status = new RuleStatus(RuleState.Waiting, default, hold, L.T($"«{rule.ProcessName}» الان باز نیست؛ منتظرم اجرا بشه.", $"“{rule.ProcessName}” is not running yet; waiting for it to start."));
                met = !running;
                text = met
                    ? L.T($"«{rule.ProcessName}» بسته شده.", $"“{rule.ProcessName}” has closed.")
                    : L.T($"«{rule.ProcessName}» هنوز بازه.", $"“{rule.ProcessName}” is still open.");
                break;
            }
        }

        if (!met)
        {
            _since = null;
            return Status = new RuleStatus(RuleState.NotMet, default, hold, text);
        }

        _since ??= now;
        var elapsed = now - _since.Value;
        return Status = elapsed >= hold
            ? new RuleStatus(RuleState.Fire, hold, hold, text)
            : new RuleStatus(RuleState.Counting, elapsed, hold, text);
    }

    public static string NormalizeProcess(string name)
    {
        name = name.Trim().ToLowerInvariant();
        return name.EndsWith(".exe", StringComparison.Ordinal) ? name[..^4] : name;
    }
}
