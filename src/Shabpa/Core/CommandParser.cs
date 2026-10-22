using System.Text.Json;

namespace Shabpa.Core;

/// <summary>What a typed command asks for.</summary>
public enum CommandIntent { None, Power, Cancel, Wake, Reminder, Focus, NightLight, SleepMode, ListSchedules }

/// <summary>How sure the parser is. Only <see cref="Ready"/> may be offered for confirmation; everything else asks first
/// and runs nothing.</summary>
public enum CommandStatus
{
    /// <summary>One clear reading: show the confirmation card.</summary>
    Ready,
    /// <summary>Two or more readings: show them as choices.</summary>
    Clarify,
    /// <summary>The intent is clear but something is missing (a time, a title, a program name).</summary>
    NeedsInfo,
    /// <summary>Not understood.</summary>
    Unknown,
}

/// <summary>One fully worked-out command. Nothing here runs anything: the window or the bot shows
/// <see cref="Summary"/> and only runs it after the person confirms.</summary>
public sealed record AssistantCommand
{
    public CommandIntent Intent { get; init; }
    /// <summary>Power: what to do. Sleep mode: the action at the end (null = the one in the settings).</summary>
    public PowerAction? Action { get; init; }
    /// <summary>When: power, wake and reminder. Null for "now" (power) or "when the condition holds".</summary>
    public DateTime? At { get; init; }
    /// <summary>Weekly repeat days; empty = once.</summary>
    public IReadOnlyList<DayOfWeek> Repeat { get; init; } = [];
    /// <summary>Power after a condition (download done, idle, program closed): the rule to activate.</summary>
    public Rule? Condition { get; init; }
    /// <summary>Night light, focus, sleep mode: false to switch it off.</summary>
    public bool On { get; init; } = true;
    /// <summary>Focus length or sleep-mode minutes; 0 = the setting.</summary>
    public int Minutes { get; init; }
    /// <summary>Night light strength in percent; 0 = the setting.</summary>
    public int Level { get; init; }
    /// <summary>Reminder title.</summary>
    public string Title { get; init; } = "";
    /// <summary>Part of the time was filled in («فردا صبح» → 8:00), so the card says it plainly.</summary>
    public bool Defaulted { get; init; }

    /// <summary>Changes the PC or the schedule in a way that cannot simply be ignored (anything but a list or a cancel).</summary>
    public bool Changes => Intent is not (CommandIntent.ListSchedules or CommandIntent.None);

    /// <summary>One sentence for the confirmation card, with the exact time: «فردا ۲۲ مهر ساعت ۰۲:۳۰ سیستم خاموش می‌شه.»</summary>
    public string Summary(DateTime now)
    {
        string When(DateTime at) => Repeat.Count > 0
            ? L.T($"{Fa.Days(Repeat)} ساعت {Fa.Clock(at)}", $"{Fa.Days(Repeat)} at {Fa.Clock(at)}")
            : L.T($"{Fa.DayOf(at, now)} ساعت {Fa.Clock(at)}", $"{Fa.DayOf(at, now)} at {Fa.Clock(at)}");
        switch (Intent)
        {
            case CommandIntent.Power when Condition is { } rule:
            {
                var what = Fa.ActionSentence(Action ?? PowerAction.Sleep);
                return rule.Kind switch
                {
                    RuleKind.Network => L.T(
                        $"وقتی {(rule.Upload ? "آپلود" : "دانلود")} تموم شد (سرعت {Fa.Digits(rule.HoldMinutes.ToString())} دقیقه زیر {Fa.Mbit(rule.ThresholdMbps)} مگابیت بمونه)، {what}.",
                        $"When the {(rule.Upload ? "upload" : "download")} is done (speed under {Fa.Mbit(rule.ThresholdMbps)} Mbit for {rule.HoldMinutes} min), {what}."),
                    RuleKind.Idle => L.T(
                        $"اگه {Fa.Digits(rule.HoldMinutes.ToString())} دقیقه موس و کیبورد دست نخوره، {what}.",
                        $"If the mouse and keyboard are untouched for {rule.HoldMinutes} min, {what}."),
                    RuleKind.Load => L.T(
                        $"وقتی پردازنده و گرافیک {Fa.Digits(rule.HoldMinutes.ToString())} دقیقه زیر {Fa.Digits(rule.LoadPercent.ToString())}٪ موندن (کار سنگین تموم شد)، {what}.",
                        $"When CPU and GPU stay under {rule.LoadPercent}% for {rule.HoldMinutes} min (the heavy work is done), {what}."),
                    _ => L.T(
                        $"وقتی «{rule.ProcessName}» بسته شد (و {Fa.Digits(rule.HoldMinutes.ToString())} دقیقه بسته موند)، {what}.",
                        $"When “{rule.ProcessName}” closes (and stays closed for {rule.HoldMinutes} min), {what}."),
                };
            }
            case CommandIntent.Power:
            {
                var what = Fa.ActionSentence(Action ?? PowerAction.Sleep);
                if (At is not { } at)
                    return Action == PowerAction.Lock
                        ? L.T("همین الان صفحه قفل می‌شه.", "The screen locks right now.")
                        : L.T($"همین الان {what} (اول یه شمارش معکوس روی صفحه میاد که می‌تونی لغوش کنی).", $"Right now {what} (after an on-screen countdown you can cancel).");
                return L.T($"{When(at)} {what}.", $"{When(at)} {what}.");
            }
            case CommandIntent.Cancel:
                return L.T("همه‌ی دستورهای خاموشی و خواب در صف، شمارش معکوس و قانون شرطی لغو می‌شن.",
                    "Every queued power command, the countdown and the conditional rule are cancelled.");
            case CommandIntent.Wake:
                return At is { } wake
                    ? L.T($"سیستم {Fa.Days(Repeat)} ساعت {Fa.Clock(wake)} از خواب بیدار می‌شه (تکرار هفتگی؛ از صفحه‌ی بیدارباش حذف می‌شه).",
                        $"The PC wakes from sleep {Fa.Days(Repeat)} at {Fa.Clock(wake)} (weekly; remove it on the Wake page).")
                    : "";
            case CommandIntent.Reminder:
                return At is { } due
                    ? L.T($"{When(due)} یادت می‌ندازم: «{Title}»", $"{When(due)} I will remind you: “{Title}”")
                    : L.T($"«{Title}» به کارها اضافه می‌شه.", $"“{Title}” is added to your tasks.");
            case CommandIntent.Focus:
                return On
                    ? L.T($"تایمر تمرکز {(Minutes > 0 ? Fa.Digits(Minutes.ToString()) + " دقیقه‌ای" : "")} شروع می‌شه.".Replace("  ", " "),
                        $"A {(Minutes > 0 ? Minutes + "-minute " : "")}focus timer starts.")
                    : L.T("تایمر تمرکز متوقف می‌شه.", "The focus timer stops.");
            case CommandIntent.NightLight:
                return On
                    ? L.T($"نور شب (کم‌نور و گرم کردن صفحه) روشن می‌شه{(Level > 0 ? $" با شدت {Fa.Digits(Level.ToString())}٪" : "")}.",
                        $"Night light (dim and warm screen) turns on{(Level > 0 ? $" at {Level}%" : "")}.")
                    : L.T("نور شب خاموش می‌شه و صفحه به حالت عادی برمی‌گرده.", "Night light turns off and the screen goes back to normal.");
            case CommandIntent.SleepMode:
                if (!On) return L.T("حالت شب‌خواب لغو می‌شه.", "Sleep mode is cancelled.");
                return L.T(
                    $"حالت شب‌خواب شروع می‌شه{(Minutes > 0 ? $"؛ {Fa.Digits(Minutes.ToString())} دقیقه‌ی دیگه" : "")}{(Action is { } sa ? $" {Fa.ActionSentence(sa)}" : "")}.",
                    $"Sleep mode starts{(Minutes > 0 ? $"; in {Minutes} minutes" : "")}{(Action is { } se ? $" {Fa.ActionSentence(se)}" : "")}.");
            case CommandIntent.ListSchedules:
                return L.T("دستورهای زمان‌بندی‌شده رو نشون می‌دم.", "Showing what is scheduled.");
            default:
                return "";
        }
    }

    /// <summary>The same command in the bot's protocol (docs/PROTOCOL.md: type + arguments), or null when the bot
    /// protocol has no such command (reminders, wake timers, conditions, weekly repeats).</summary>
    public (string Type, string Args)? ToBotRequest(DateTime now)
    {
        static string Name(PowerAction a) => a switch
        {
            PowerAction.Sleep => "sleep",
            PowerAction.Hibernate => "hibernate",
            PowerAction.Shutdown => "shutdown",
            PowerAction.Restart => "restart",
            _ => "lock",
        };
        switch (Intent)
        {
            case CommandIntent.Power when Condition is null && Repeat.Count == 0 && Action is { } a:
            {
                var delay = At is { } at ? Math.Max(0, (int)Math.Round((at - now).TotalSeconds)) : 0;
                return ("power", JsonSerializer.Serialize(new { action = Name(a), delaySeconds = delay }));
            }
            case CommandIntent.Cancel:
                return ("cancel", "{}");
            case CommandIntent.NightLight:
                return ("dim", Level > 0 ? JsonSerializer.Serialize(new { on = On, level = Level }) : JsonSerializer.Serialize(new { on = On }));
            case CommandIntent.SleepMode when !On:
                return ("sleep-cancel", "{}");
            case CommandIntent.SleepMode:
                return ("sleep-mode", Action is { } sa
                    ? JsonSerializer.Serialize(new { minutes = Minutes, action = Name(sa) })
                    : JsonSerializer.Serialize(new { minutes = Minutes }));
            case CommandIntent.Focus:
                return ("focus", On ? JsonSerializer.Serialize(new { minutes = Minutes }) : "{\"action\":\"stop\"}");
            case CommandIntent.ListSchedules:
                return ("status", "{}");
            default:
                return null;
        }
    }
}

/// <summary>The parser's answer: a command to confirm, choices to pick from, or a question.</summary>
public sealed record CommandResult(CommandStatus Status, AssistantCommand? Command, IReadOnlyList<AssistantCommand> Choices, string Question, double Confidence)
{
    public static CommandResult Unknown(string question) => new(CommandStatus.Unknown, null, [], question, 0);
}

/// <summary>Understands short commands typed in Persian or English, with no network and no language model: a fixed
/// list of phrasings per intent, a shared time reader (<see cref="TimePhrase"/>) and the conditions the rule engine
/// already has. Anything it is not sure about comes back as choices or a question, never as a command. Pure logic;
/// the caller supplies the clock.</summary>
public static class CommandParser
{
    /// <summary>Below this the parser offers choices instead of a command.</summary>
    public const double ReadyAt = 0.7;

    /// <summary>The longest delay a typed command may set, like the bot's limit.</summary>
    public static readonly TimeSpan MaxAhead = TimeSpan.FromDays(7);

    private enum Cue
    {
        Shutdown, Sleep, Hibernate, Restart, Lock, Cancel, Negation, On, Off,
        Wake, Reminder, Focus, NightLight, SleepMode, List,
        Download, Upload, Idle, Closes, Render, Game, Now,
    }

    /// <summary>A phrasing: words in order (a trailing * matches any ending), with at most two other words between them.</summary>
    private sealed record Phrase(Cue Cue, string[] Words, double Weight);

    private static readonly List<Phrase> Phrases = Build();

    private static List<Phrase> Build()
    {
        var list = new List<Phrase>();
        void P(Cue cue, double weight, params string[] phrases)
        {
            foreach (var p in phrases)
                list.Add(new Phrase(cue, p.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.EndsWith('*') ? CommandText.Normalize(w[..^1]) + "*" : CommandText.Normalize(w)).ToArray(), weight));
        }

        // ---- power -----------------------------------------------------------------------
        P(Cue.Shutdown, 1, "خاموش*", "بخاموش*", "شاتدان*", "شات دان*", "شاتداون", "شات داون", "خاموشی", "سیستمو ببند",
            "shutdown*", "shut down", "shut off", "turn off", "power off", "poweroff", "switch off", "power down", "shut it down", "turn it off");
        P(Cue.Sleep, 1, "خواب", "بخواب*", "خوابون*", "خوابش", "خوابیدن", "اسلیپ*", "حالت خواب", "استندبای",
            "sleep", "sleeep", "suspend", "standby", "stand by", "go to sleep", "put to sleep");
        P(Cue.Hibernate, 1.15, "هایبرنیت*", "هایبرنت*", "هایبرنیشن", "هایبر نیت", "خواب عمیق", "خواب زمستانی",
            "hibernate*", "hibernation", "hybernate", "deep sleep");
        P(Cue.Restart, 1.1, "ریاستارت*", "ری استارت*", "ریستارت*", "ری ستارت", "راهاندازی مجدد", "راه اندازی مجدد", "راهاندازی دوباره",
            "دوباره راهاندازی*", "دوباره راه اندازی*", "ریبوت*", "دوباره روشن", "خاموش و روشن",
            "restart*", "reboot*", "re start", "restrat");
        P(Cue.Lock, 1, "قفل*", "لاک", "lock*", "lockscreen");

        // ---- cancel / negation / on / off -----------------------------------------------
        P(Cue.Cancel, 1.05, "لغو*", "کنسل*", "بیخیال*", "بیخیالش", "منصرف", "متوقف*", "استاپ", "نمیخوام خاموش",
            "cancel*", "abort*", "nevermind", "never mind", "stop", "call off", "forget it", "undo");
        P(Cue.Negation, 1, "نکن*", "نکنی", "نشه", "نخواب*", "نخوابون*", "نزن", "نره", "don't", "dont", "don’t", "do not", "never");
        P(Cue.On, 1, "روشن*", "فعال*", "بزن", "شروع*", "استارت", "on", "enable*", "start*", "activate", "begin");
        P(Cue.Off, 1, "غیرفعال*", "غیر فعال*", "بردار*", "تموم کن", "قطع*", "off", "disable*", "deactivate", "end", "finish");

        // ---- features ---------------------------------------------------------------------
        P(Cue.Wake, 1.1, "بیدار*", "بیدارش*", "wake*", "wakeup", "wake up", "از خواب دربیار", "از خواب بیار");
        P(Cue.Reminder, 1.3, "یادم بنداز*", "یادم بیار*", "یادم بده", "یادم باشه", "یادت باشه", "یاداوری*", "یاد اوری*", "یاداور*",
            "یادم بنداز", "یادم بندازی", "بهم یاداوری", "یادم ننداز", "تسک", "کار جدید", "به کارها اضافه", "یادداشت کن",
            "remind*", "reminder*", "remember to", "add task", "add a task", "new task", "todo", "to do", "to-do");
        P(Cue.Focus, 1.3, "تمرکز*", "فوکوس*", "فوکس", "پومودورو*", "پومودرو", "focus*", "pomodoro*", "pomo");
        P(Cue.NightLight, 1.3, "نور شب", "نورشب", "نور گرم", "حالت شب", "حالت شبانه", "کمنور*", "کم نور*", "تاریک*", "نور صفحه*", "نور کم",
            "night light", "nightlight", "night mode", "warm light", "dim*", "darken", "darker", "blue light");
        P(Cue.SleepMode, 1.4, "شبخواب*", "شب خواب*", "حالت شب خواب", "sleep mode", "sleepmode", "bedtime", "bed time", "wind down");
        P(Cue.List, 0.85, "لیست*", "فهرست*", "صف", "صفه", "صفو", "تو صف", "زمانبندیها", "زمان بندیها", "دستورها", "دستورات", "وضعیت", "چی تو صف",
            "چه خبر", "برنامه هام", "برنامههام", "چی زمانبندی", "چه دستوری", "کی خاموش میشه", "کی خاموش میشه",
            "list*", "queue", "pending", "scheduled", "schedules", "upcoming", "status", "what's planned", "whats planned", "what is planned");

        // ---- conditions -------------------------------------------------------------------
        P(Cue.Download, 1, "دانلود*", "دانلودا", "دانلودها", "download*", "downlod*");
        P(Cue.Upload, 1, "اپلود*", "upload*");
        P(Cue.Idle, 1, "بیکار*", "بیاستفاده", "بی استفاده", "کاری نکردم", "کاری نکنم", "دست نزدم", "دست نخوره", "دست نخورد", "پشتش نبودم", "پشت سیستم نبودم",
            "نبودم", "idle", "inactive", "inactivity", "away", "afk", "not using", "no activity", "untouched");
        P(Cue.Closes, 1, "بسته شد", "بسته بشه", "بسته شه", "بسته شود", "بستمش", "بستم", "تموم شد", "تموم بشه", "تموم شه", "تمام شد",
            "closes", "closed", "close", "exits", "exited", "quits", "quit", "is done", "finishes", "finished", "ends");
        P(Cue.Render, 1, "رندر*", "render*", "encode*", "انکد*", "کار سنگین");
        P(Cue.Game, 1, "بازی*", "گیم", "game*");
        P(Cue.Now, 1, "الان", "همین الان", "فوری", "فورا", "سریع", "now", "right now", "immediately", "asap");
        // longest first, so «خواب عمیق» wins over «خواب» and «حالت شب خواب» over «خواب»
        return [.. list.OrderByDescending(p => p.Words.Length).ThenByDescending(p => p.Words.Sum(w => w.Length))];
    }

    /// <summary>Words that carry no meaning of their own in a command («سیستم رو»، "please", "the").</summary>
    private static readonly HashSet<string> Filler = new(new[]
    {
        "سیستم", "سیستمو", "سیستمم", "سیستممو", "کامپیوتر", "کامپیوترو", "کامپیوترم", "کامپیوترمو", "لپتاپ", "لپتاپو", "لپ", "تاپ", "پیسی", "کیس", "مانیتور", "صفحه", "صفحهرو",
        "رو", "را", "و", "کن", "بکن", "کنید", "بکنید", "کنه", "بکنه", "کنم", "بکنم", "شو", "بشو", "بشه", "شه", "میشه", "شد", "بشن", "لطفا", "لطفن", "مرسی", "ممنون", "میکنی", "میکنید",
        "من", "منو", "برام", "واسم", "برای", "واسه", "که", "اگه", "اگر", "وقتی", "موقعی", "زمانی", "بعد", "از", "تا", "با", "به", "در", "توی", "تو", "یه", "یک", "اینو", "این", "اون", "اونو",
        "میخوام", "میخواستم", "بخوام", "باید", "بزار", "بذار", "بره", "برو", "ببر", "ببرش", "بفرست", "حالت", "روی", "رو", "سر", "اینکه", "دیگه", "هم", "همه", "همهی", "جان", "جون",
        "سلام", "شبپا", "بیدارجان", "لطفاً", "ok", "okay", "اوکی", "باشه", "خب", "خوب", "پس", "یعنی", "lol", "pls", "plz", "please", "thanks", "thank", "you", "hey", "hi",
        "the", "a", "an", "my", "it", "this", "that", "me", "i", "i'm", "im", "want", "wanna", "need", "can", "could", "would", "will", "should", "to", "into", "in", "on", "at", "for",
        "pc", "computer", "laptop", "system", "machine", "screen", "monitor", "go", "put", "set", "make", "mode", "timer", "تایمر", "and", "then", "when", "if", "after", "once", "is", "be",
        "do", "just", "please,", "now", "شما", "تو", "بده", "کار", "کارو", "lets", "let's", "let", "start", "up", "down", "of", "with", "by", "from", "done", "time", "وقت", "ساعت",
    }.Select(CommandText.Normalize));

    private sealed record Hit(Cue Cue, int Start, int End, double Weight, bool Fuzzy);

    private static bool WordMatches(string word, string token, bool allowFuzzy, out bool fuzzy)
    {
        fuzzy = false;
        if (token.Length == 0) return false;
        if (!allowFuzzy) return word.EndsWith('*') ? token.StartsWith(word[..^1], StringComparison.Ordinal) : token == word;
        if (word.EndsWith('*'))
        {
            var stem = word[..^1];
            if (token.StartsWith(stem, StringComparison.Ordinal)) return true;
            // «خاموس کن» → «خاموش»: a typo inside the stem, with any ending (the first letter must be right:
            // «نخواب» is not a typo of «بخواب»)
            if (stem.Length >= 5 && token.Length >= stem.Length && token[0] == stem[0] && CommandText.Distance(stem, token[..stem.Length], 1) <= 1) { fuzzy = true; return true; }
            return false;
        }
        if (token == word) return true;
        var tol = CommandText.Tolerance(word);
        if (tol > 0 && token[0] == word[0] && Math.Abs(token.Length - word.Length) <= tol && CommandText.Distance(word, token, tol) <= tol)
        {
            fuzzy = true;
            return true;
        }
        return false;
    }

    private static List<Hit> Find(IReadOnlyList<CommandToken> t, bool[] used)
    {
        var hits = new List<Hit>();
        var taken = new List<int>(4);
        // every exact match first, so a typo reading never takes a word that is right as written («بیکار» is not «بیدار»)
        foreach (var allowFuzzy in new[] { false, true })
        foreach (var p in Phrases)
        {
            for (var i = 0; i < t.Count; i++)
            {
                if (used[i] || !WordMatches(p.Words[0], t[i].Norm, allowFuzzy, out var fuzzy)) continue;
                taken.Clear();
                taken.Add(i);
                var j = i + 1;
                var ok = true;
                for (var w = 1; w < p.Words.Length && ok; w++)
                {
                    var found = false;
                    // filler words may sit in between: "turn the pc off", "shut it down"
                    for (var k = j; k < t.Count && k <= j + 2; k++)
                    {
                        if (used[k]) break;
                        if (WordMatches(p.Words[w], t[k].Norm, allowFuzzy, out var f2))
                        {
                            fuzzy |= f2;
                            taken.Add(k);
                            j = k + 1;
                            found = true;
                            break;
                        }
                        if (!Filler.Contains(t[k].Norm) || p.Words[0].Length <= 3) break; // short starts («ری»، "re") must be followed straight away
                    }
                    ok = found;
                }
                if (!ok) continue;
                hits.Add(new Hit(p.Cue, i, j, p.Weight, fuzzy));
                foreach (var k in taken) used[k] = true;
            }
        }
        return hits;
    }

    private static readonly Dictionary<string, string> KnownPrograms = new()
    {
        ["استیم"] = "steam", ["کروم"] = "chrome", ["فایرفاکس"] = "firefox", ["تلگرام"] = "telegram", ["اسپاتیفای"] = "spotify",
        ["دیسکورد"] = "discord", ["بلندر"] = "blender", ["اپیک"] = "EpicGamesLauncher", ["یوتورنت"] = "utorrent", ["تورنت"] = "qbittorrent",
        ["اینترنت دانلود منیجر"] = "IDMan", ["idm"] = "IDMan", ["وی ال سی"] = "vlc", ["وی‌ال‌سی"] = "vlc", ["پریمیر"] = "Adobe Premiere Pro",
        ["فتوشاپ"] = "Photoshop", ["افتر"] = "AfterFX", ["هندبریک"] = "HandBrake", ["obs"] = "obs64", ["اوبیاس"] = "obs64",
    };

    /// <summary>Reads one command. <paramref name="now"/> is the local time the command is read at.</summary>
    public static CommandResult Parse(string? text, DateTime now)
    {
        var tokens = CommandText.Tokenize(text);
        var norm = CommandText.Normalize(text);
        if (tokens.Count == 0)
            return CommandResult.Unknown(L.T("چیزی ننوشتی. مثلاً بنویس «یه ساعت دیگه خاموش کن».", "Nothing was typed. Try “shut down in an hour”."));

        var used = new bool[tokens.Count];
        var hits = Find(tokens, used);
        bool Has(Cue c) => hits.Any(h => h.Cue == c);
        double Weight(Cue c) => hits.Where(h => h.Cue == c).Select(h => h.Weight).DefaultIfEmpty(0).Max();
        var anyFuzzy = hits.Any(h => h.Fuzzy);

        // A reminder keeps every other word as its title: «یادم بنداز ساعت ۵ سیستم رو خاموش کنم» is a reminder about shutting down.
        if (Has(Cue.Reminder))
        {
            var keep = new bool[tokens.Count];
            foreach (var h in hits.Where(h => h.Cue == Cue.Reminder))
                for (var k = h.Start; k < h.End; k++) keep[k] = true;
            var when = TimePhrase.Parse(tokens, keep, now);
            return Reminder(tokens, keep, when, now, Weight(Cue.Reminder));
        }

        var time = TimePhrase.Parse(tokens, used, now);
        var actions = new List<(PowerAction Action, double Weight)>();
        void Act(Cue c, PowerAction a) { if (Has(c)) actions.Add((a, Weight(c))); }
        Act(Cue.Shutdown, PowerAction.Shutdown);
        Act(Cue.Sleep, PowerAction.Sleep);
        Act(Cue.Hibernate, PowerAction.Hibernate);
        Act(Cue.Restart, PowerAction.Restart);
        Act(Cue.Lock, PowerAction.Lock);
        var off = Has(Cue.Off) || Has(Cue.Cancel) || Has(Cue.Negation) || Has(Cue.Shutdown);
        var leftovers = Leftovers(tokens, used);
        var statement = Statement(tokens, hits);
        var question = statement || norm.Contains('?') || tokens.Any(w => w.Norm is "کی" or "چرا" or "چطور" or "چطوری" or "ایا" or "چندمه")
                       || tokens[0].Norm is "why" or "how" or "what" or "what's" or "whats" or "will" or "is" or "does" or "did" or "are"
                       || (tokens[0].Norm == "when" && tokens.Count > 1 && tokens[1].Norm is "will" or "is" or "does" or "do" or "did" or "are" or "can" or "would" or "should");

        // ---- features named by their noun win over the bare verbs («نور شب رو خاموش کن» is not a shutdown) -------------
        if (Has(Cue.SleepMode))
        {
            var on = !off;
            var minutes = time.Duration is { } d ? (int)Math.Round(d.TotalMinutes) : 0;
            var end = actions.Where(a => a.Action != PowerAction.Shutdown || !off).Select(a => (PowerAction?)a.Action).FirstOrDefault();
            if (Has(Cue.Shutdown) && !Has(Cue.Cancel) && !Has(Cue.Negation) && !Has(Cue.Off) && minutes > 0) { on = true; end = PowerAction.Shutdown; }
            return Ready(new AssistantCommand { Intent = CommandIntent.SleepMode, On = on, Minutes = on ? Math.Clamp(minutes, 0, 600) : 0, Action = on ? end : null },
                Weight(Cue.SleepMode), leftovers, anyFuzzy);
        }
        if (Has(Cue.NightLight))
        {
            var on = !off || (Has(Cue.On) && !Has(Cue.Cancel) && !Has(Cue.Negation) && !Has(Cue.Off));
            if (Has(Cue.On) && Has(Cue.Shutdown) && !Has(Cue.Off) && !Has(Cue.Cancel))
                return Choose([new AssistantCommand { Intent = CommandIntent.NightLight, On = true }, new AssistantCommand { Intent = CommandIntent.NightLight, On = false }], 0.5);
            if (time.HasClock || (time.Duration is not null && time.Relative))
                return Need(L.T("نور شب رو فعلاً فقط همین الان می‌تونم روشن یا خاموش کنم، نه برای ساعت بعد. برای زمان‌بندی، «خودکار» رو توی صفحه‌ی آسایش چشم روشن کن.",
                    "Night light can only be switched right now, not at a later time. To schedule it, turn on “automatic” on the Eye comfort page."), CommandIntent.NightLight);
            return Ready(new AssistantCommand { Intent = CommandIntent.NightLight, On = on, Level = on && time.Percent is { } p ? Math.Clamp(p, 5, 92) : 0 },
                Weight(Cue.NightLight), leftovers, anyFuzzy);
        }
        if (Has(Cue.Focus))
        {
            var stop = Has(Cue.Cancel) || Has(Cue.Off) || Has(Cue.Negation) || (Has(Cue.Shutdown) && !Has(Cue.On));
            if (stop)
                return Ready(new AssistantCommand { Intent = CommandIntent.Focus, On = false }, Weight(Cue.Focus), leftovers, anyFuzzy);
            var minutes = 0;
            if (time.Duration is { } d) minutes = (int)Math.Round(d.TotalMinutes);
            else if (time.HasClock && !time.Defaulted) minutes = (int)Math.Round((time.Moments[0] - now).TotalMinutes);
            if (minutes != 0 && minutes is < 5 or > 90)
                return Need(L.T("تایمر تمرکز می‌تونه بین ۵ تا ۹۰ دقیقه باشه.", "A focus timer can be 5 to 90 minutes long."), CommandIntent.Focus);
            return Ready(new AssistantCommand { Intent = CommandIntent.Focus, On = true, Minutes = minutes }, Weight(Cue.Focus), leftovers, anyFuzzy);
        }

        // ---- cancel ------------------------------------------------------------------------
        if (Has(Cue.Cancel) && !(Has(Cue.Wake) && actions.Count == 0))
            return Ready(new AssistantCommand { Intent = CommandIntent.Cancel }, Weight(Cue.Cancel), leftovers, anyFuzzy);
        if (Has(Cue.Negation) && (actions.Count > 0 || hits.Any(h => h.Cue == Cue.Negation && tokens[h.Start].Norm.StartsWith("نخواب", StringComparison.Ordinal))))
        {
            // «خاموش نکن» most likely means "stop the shutdown", but it is not said outright: ask
            return Choose([new AssistantCommand { Intent = CommandIntent.Cancel }], 0.6,
                L.T("منظورت لغو دستورهای خاموشی و خوابه؟", "Do you mean cancel the power commands?"));
        }

        // ---- wake timer ----------------------------------------------------------------------
        if (Has(Cue.Wake) && actions.Count == 0)
            return Wake(time, now, Weight(Cue.Wake), leftovers, anyFuzzy);

        // ---- power ---------------------------------------------------------------------------
        if (actions.Count > 0)
        {
            var distinct = actions.GroupBy(a => a.Action).Select(g => g.OrderByDescending(a => a.Weight).First()).ToList();
            // «خواب عمیق» found as hibernate also matched no plain sleep, but «خاموش و روشن» (restart) leaves a shutdown hit
            if (distinct.Any(a => a.Action == PowerAction.Restart)) distinct.RemoveAll(a => a.Action == PowerAction.Shutdown && hits.Any(h => h.Cue == Cue.Restart && h.Weight > 1));
            if (question && !Has(Cue.Now))
            {
                var listing = new AssistantCommand { Intent = CommandIntent.ListSchedules };
                var asked = Power(distinct[0].Action, time, now, hits, tokens, used, 1, leftovers, anyFuzzy);
                var choices = new List<AssistantCommand> { listing };
                if (asked.Command is { } c) choices.Add(c);
                else choices.AddRange(asked.Choices);
                return Choose(choices, 0.5, L.T("این یه سؤال بود یا یه دستور؟", "Was that a question or a command?"));
            }
            if (distinct.Count > 1)
            {
                var options = distinct.Select(a => Power(a.Action, time, now, hits, tokens, used, a.Weight, leftovers, anyFuzzy))
                    .SelectMany(r => r.Command is { } c ? [c] : r.Choices).ToList();
                return Choose(options, 0.5, L.T("کدومش؟", "Which one?"));
            }
            return Power(distinct[0].Action, time, now, hits, tokens, used, distinct[0].Weight, leftovers, anyFuzzy);
        }

        // «سیستم رو ساعت ۷ روشن کن»: the PC can only be woken from sleep, so it is offered, not assumed
        if (Has(Cue.On) && time.HasClock)
        {
            var wake = Wake(time, now, 1, 0, false);
            var options = wake.Command is { } w ? [w] : wake.Choices.ToList();
            if (options.Count > 0)
                return Choose(options, 0.55, L.T("منظورت بیدار کردن سیستم از خوابه؟ (از حالت خاموش کامل نمی‌شه روشنش کرد.)", "Do you mean wake the PC from sleep? (It cannot be switched on from fully off.)"));
        }

        if (Has(Cue.List))
            return Ready(new AssistantCommand { Intent = CommandIntent.ListSchedules }, Weight(Cue.List), Math.Max(0, leftovers - 1), anyFuzzy);

        // only a time: «ساعت ۲» — what should happen then?
        if (time.HasClock || time.Duration is not null)
        {
            var at = time.HasClock ? time.Moments[0] : now + time.Duration!.Value;
            return new CommandResult(CommandStatus.NeedsInfo, null,
                [
                    new AssistantCommand { Intent = CommandIntent.Power, Action = PowerAction.Shutdown, At = at },
                    new AssistantCommand { Intent = CommandIntent.Power, Action = PowerAction.Sleep, At = at },
                ],
                L.T("اون موقع چی کار کنم؟", "What should happen then?"), 0.3);
        }

        return CommandResult.Unknown(L.T("متوجه نشدم. مثلاً بنویس «ساعت ۲ شب خاموش کن»، «وقتی دانلود تموم شد بخوابون» یا «ساعت ۵ یادم بنداز زنگ بزنم».",
            "I did not understand. Try “shut down at 2am”, “sleep when the download is done” or “remind me to call at 5pm”."));
    }

    /// <summary>Words after a power verb that make it a statement or a question, not an order: «خاموش شد»، «خاموش نمیشه»،
    /// «خاموش میشه»، "keeps going to sleep", "won't shut down".</summary>
    private static readonly HashSet<string> StatementAfter = new(new[]
    {
        "شد", "شده", "شدہ", "میشه", "نمیشه", "میشود", "نمیشود", "نشد", "نشده", "میکنه", "نمیکنه", "کرد", "کرده", "نکرد", "میاد", "نمیاد", "بود", "میره", "نمیره", "رفت",
    }.Select(CommandText.Normalize));

    private static readonly HashSet<string> StatementAnywhere = new(new[]
    {
        "خودش", "خودبهخود", "دیشب", "دیروز", "همش", "هی", "keeps", "keep", "won't", "wont", "doesn't", "doesnt", "didn't", "didnt", "isn't", "went", "was", "already", "randomly", "itself",
    }.Select(CommandText.Normalize));

    private static bool Statement(IReadOnlyList<CommandToken> t, List<Hit> hits)
    {
        if (t.Any(w => StatementAnywhere.Contains(w.Norm))) return true;
        foreach (var h in hits)
        {
            if (h.Cue is not (Cue.Shutdown or Cue.Sleep or Cue.Hibernate or Cue.Restart or Cue.Lock)) continue;
            if (h.End < t.Count && StatementAfter.Contains(t[h.End].Norm)) return true;
            // «خوابم میاد»، «خوابیدم»: about the person, not the PC
            if (t[h.Start].Norm is "خوابم" or "خوابیدم" or "خوابیده" or "بخوابم" or "میخوابم") return true;
        }
        // "I want to sleep", "I'm going to sleep": the person, unless the PC is named
        if (hits.Any(h => h.Cue == Cue.Sleep) && !hits.Any(h => h.Cue is Cue.Idle or Cue.Download or Cue.Upload or Cue.Closes) && t.Any(w => w.Norm is "i" or "i'm" or "im" or "i’m")
            && !t.Any(w => w.Norm is "pc" or "computer" or "laptop" or "it" or "system" or "machine")) return true;
        return false;
    }

    private static int Leftovers(IReadOnlyList<CommandToken> t, bool[] used)
    {
        var n = 0;
        for (var i = 0; i < t.Count; i++)
            if (!used[i] && !Filler.Contains(t[i].Norm)) n++;
        return n;
    }

    private static double Confidence(double weight, int leftovers, bool fuzzy)
    {
        var c = Math.Min(1, weight) * 0.95;
        if (fuzzy) c *= 0.85;
        // a couple of unknown words is normal speech; a whole sentence around one keyword is probably about something else
        c -= Math.Max(0, leftovers - 2) * 0.08;
        return Math.Clamp(c, 0, 1);
    }

    private static CommandResult Ready(AssistantCommand command, double weight, int leftovers, bool fuzzy)
    {
        var c = Confidence(weight, leftovers, fuzzy);
        return c >= ReadyAt
            ? new CommandResult(CommandStatus.Ready, command, [], "", c)
            : new CommandResult(CommandStatus.Clarify, null, [command], L.T("درست فهمیدم؟", "Did I get that right?"), c);
    }

    private static CommandResult Choose(List<AssistantCommand> choices, double confidence, string? question = null) =>
        new(CommandStatus.Clarify, null, choices, question ?? L.T("کدوم منظورته؟", "Which one do you mean?"), confidence);

    private static CommandResult Need(string question, CommandIntent intent, IReadOnlyList<AssistantCommand>? choices = null) =>
        new(CommandStatus.NeedsInfo, null, choices ?? [], question, 0.5);

    private static string Past => L.T("اون زمان گذشته. یه زمان بعد از الان بگو.", "That time has already passed. Give a time after now.");
    private static string BadTime => L.T("این ساعت درست نیست. مثلاً بنویس «ساعت ۲۳:۳۰».", "That is not a valid time. Try something like “at 23:30”.");
    private static string TooFar => L.T("بیشتر از یه هفته جلوتر رو نمی‌شه زمان‌بندی کرد.", "Commands can be set at most a week ahead.");

    // ---- power --------------------------------------------------------------------------

    private static CommandResult Power(PowerAction action, TimeFound time, DateTime now, List<Hit> hits, IReadOnlyList<CommandToken> t, bool[] used, double weight, int leftovers, bool fuzzy)
    {
        bool Has(Cue c) => hits.Any(h => h.Cue == c);

        // a condition from the rule engine
        Rule? rule = null;
        if (Has(Cue.Download) || Has(Cue.Upload))
            rule = new Rule { Kind = RuleKind.Network, Upload = Has(Cue.Upload) && !Has(Cue.Download), WaitForStart = true };
        else if (Has(Cue.Idle))
            rule = new Rule { Kind = RuleKind.Idle, HoldMinutes = 15 };
        else if (Has(Cue.Render))
            rule = new Rule { Kind = RuleKind.Load };
        else if (Has(Cue.Closes) || Has(Cue.Game))
        {
            var name = ProgramName(t, used);
            if (name is null)
                return Need(Has(Cue.Game)
                    ? L.T("اسم برنامه‌ی بازی رو بنویس (مثلاً «وقتی steam بسته شد خاموش کن»)؛ بدون اسم نمی‌دونم منتظر کدوم بمونم.", "Write the game's program name (e.g. “shut down when steam closes”); without it I do not know what to wait for.")
                    : L.T("منتظر بسته شدن کدوم برنامه بمونم؟ اسمش رو بنویس، مثلاً «وقتی chrome بسته شد بخوابون».", "Which program should I wait for? Write its name, e.g. “sleep when chrome closes”."), CommandIntent.Power);
            rule = new Rule { Kind = RuleKind.ProcessExit, ProcessName = name, HoldMinutes = 1 };
            leftovers = Math.Max(0, leftovers - 1);
        }
        if (rule is not null)
        {
            rule.Action = action;
            if (time.Duration is { } hold) rule.HoldMinutes = Math.Clamp((int)Math.Round(hold.TotalMinutes), 0, 240);
            if (time.HasClock) return Need(L.T("هم زمان گفتی هم شرط؛ یکی‌شون رو بگو.", "You gave both a time and a condition; give one of them."), CommandIntent.Power);
            return Ready(new AssistantCommand { Intent = CommandIntent.Power, Action = action, Condition = rule }, weight, leftovers, fuzzy);
        }

        if (time.InPast) return Need(Past, CommandIntent.Power);
        if (time.Invalid) return Need(BadTime, CommandIntent.Power);
        if (!time.Any && t.Any(w => w.Norm is "بعدا" or "بعدن" or "later" or "soon" or "زودی" or "یکم" or "کمی"))
            return Need(L.T("کی؟ یه زمان بگو، مثلاً «۳۰ دقیقه دیگه».", "When? Give a time, for example “in 30 minutes”."), CommandIntent.Power,
                [new AssistantCommand { Intent = CommandIntent.Power, Action = action, At = now.AddMinutes(30) }, new AssistantCommand { Intent = CommandIntent.Power, Action = action, At = now.AddHours(1) }]);
        // a weekly command always needs a time said outright: «هر شب» alone is not 22:00
        if (time.Repeat.Count > 0 && (!time.HasClock || time.Defaulted))
            return Need(L.T("چه ساعتی؟ مثلاً «هر شب ساعت ۱ خاموش کن».", "At what time? For example “shut down every night at 1am”."), CommandIntent.Power);

        AssistantCommand Make(DateTime? at) => new() { Intent = CommandIntent.Power, Action = action, At = at, Repeat = time.Repeat, Defaulted = time.Defaulted };

        if (time.Moments.Count > 1)
            return Choose([.. time.Moments.Select(m => Make(m))], 0.6, L.T("ساعت رو صبح گفتی یا شب؟", "Did you mean in the morning or at night?"));
        if (time.Moments.Count == 1)
        {
            if (time.Moments[0] - now > MaxAhead && time.Repeat.Count == 0) return Need(TooFar, CommandIntent.Power);
            return Ready(Make(time.Moments[0]), weight * (time.Defaulted ? 0.85 : 1), leftovers, fuzzy);
        }
        if (time.Duration is { } delay)
        {
            if (delay > MaxAhead) return Need(TooFar, CommandIntent.Power);
            // «۳۰ دقیقه خاموش کن» without «دیگه» still reads as a delay, a little less surely
            return Ready(Make(delay < TimeSpan.FromSeconds(1) ? null : now + delay), weight * (time.Relative ? 1 : 0.9), leftovers, fuzzy);
        }
        return Ready(Make(null), weight, leftovers, fuzzy);
    }

    /// <summary>The program named in «وقتی steam بسته شد»: a Latin word that is not a keyword, or a known Persian name.</summary>
    private static string? ProgramName(IReadOnlyList<CommandToken> t, bool[] used)
    {
        for (var i = 0; i < t.Count; i++)
        {
            if (used[i]) continue;
            var w = t[i].Norm;
            if (KnownPrograms.TryGetValue(w, out var known)) { used[i] = true; return known; }
            if (i + 2 < t.Count && KnownPrograms.TryGetValue(w + " " + t[i + 1].Norm + " " + t[i + 2].Norm, out known)) { used[i] = used[i + 1] = used[i + 2] = true; return known; }
        }
        for (var i = 0; i < t.Count; i++)
        {
            if (used[i]) continue;
            var w = t[i].Norm;
            if (w.Length < 2 || Filler.Contains(w) || !char.IsAsciiLetter(w[0])) continue;
            if (w.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            {
                used[i] = true;
                var raw = t[i].Raw.Trim();
                return RuleEngine.NormalizeProcess(raw.Length > 0 ? raw : w) is { Length: > 0 } name ? name : null;
            }
        }
        return null;
    }

    // ---- wake ---------------------------------------------------------------------------

    private static CommandResult Wake(TimeFound time, DateTime now, double weight, int leftovers, bool fuzzy)
    {
        if (time.InPast) return Need(Past, CommandIntent.Wake);
        if (time.Invalid) return Need(BadTime, CommandIntent.Wake);
        if (time.Repeat.Count > 0 && time.Defaulted)
            return Need(L.T("چه ساعتی بیدارش کنم؟ مثلاً «هر روز ساعت ۷ صبح بیدارش کن».", "At what time should it wake? For example “wake every day at 7am”."), CommandIntent.Wake);
        var moments = time.Moments.ToList();
        if (moments.Count == 0 && time.Duration is { } d && d > TimeSpan.Zero) moments.Add(now + d);
        if (moments.Count == 0)
            return Need(L.T("چه ساعتی بیدارش کنم؟ مثلاً «ساعت ۷ صبح بیدارش کن».", "At what time should it wake? For example “wake the PC at 7am”."), CommandIntent.Wake);
        AssistantCommand Make(DateTime at) => new()
        {
            Intent = CommandIntent.Wake,
            At = at,
            // the wake page only keeps weekly schedules: a one-off time becomes "every <that weekday>", and the card says so
            Repeat = time.Repeat.Count > 0 ? time.Repeat : [at.DayOfWeek],
            Defaulted = time.Defaulted,
        };
        if (moments.Count > 1) return Choose([.. moments.Select(Make)], 0.6, L.T("صبح یا شب؟", "Morning or night?"));
        return Ready(Make(moments[0]), weight * (time.Defaulted ? 0.85 : 1), leftovers, fuzzy);
    }

    // ---- reminder -----------------------------------------------------------------------

    private static readonly HashSet<string> TitleEdges = new(new[]
    {
        "که", "رو", "را", "بهم", "منو", "من", "برای", "واسه", "برام", "واسم", "درباره", "دربارهی", "مورد", "در", "و", "تا", "از", "یه", "یک", "اینو", "این",
        "to", "me", "about", "that", "i", "for", "of", "and", "a", "an", "the", "at", "on", "in", "please", "لطفا", "بنداز", "بندازی", "کن", "بکن", "need", "should", "must",
        "اینکه", "باید", "بعد", "دیگه", "ساعت", "شد", "is", "it", "my", "it's",
    }.Select(CommandText.Normalize));

    private static CommandResult Reminder(IReadOnlyList<CommandToken> t, bool[] used, TimeFound time, DateTime now, double weight)
    {
        var words = new List<CommandToken>();
        for (var i = 0; i < t.Count; i++)
            if (!used[i] && t[i].Raw.Length > 0) words.Add(t[i]);
        // trim the joining words at both ends: «که»، "to", «رو»
        while (words.Count > 0 && TitleEdges.Contains(words[0].Norm)) words.RemoveAt(0);
        while (words.Count > 0 && TitleEdges.Contains(words[^1].Norm)) words.RemoveAt(words.Count - 1);
        var title = TaskBook.Clean(string.Join(' ', words.Select(w => w.Raw)), TaskBook.MaxTitle);
        if (title.Length == 0)
            return Need(L.T("چی رو یادت بندازم؟ مثلاً «ساعت ۵ یادم بنداز به مامان زنگ بزنم».", "Remind you of what? For example “remind me to call mom at 5pm”."), CommandIntent.Reminder);
        if (time.InPast) return Need(Past, CommandIntent.Reminder);
        if (time.Invalid) return Need(BadTime, CommandIntent.Reminder);

        AssistantCommand Make(DateTime? at) => new() { Intent = CommandIntent.Reminder, Title = title, At = at, Defaulted = time.Defaulted };
        if (time.Moments.Count > 1) return Choose([.. time.Moments.Select(m => Make(m))], 0.6, L.T("صبح یا شب؟", "Morning or night?"));
        DateTime? due = time.Moments.Count == 1 ? time.Moments[0] : time.Duration is { } d ? now + d : null;
        if (due is null)
        {
            // "remind me" with no time: offer the usual ones instead of guessing
            return Need(L.T("کی یادت بندازم؟", "When should I remind you?"), CommandIntent.Reminder,
                [Make(now.AddMinutes(30)), Make(now.AddHours(1)), Make(now.Date.AddDays(1).AddHours(9))]);
        }
        if (due.Value - now > TimeSpan.FromDays(366)) return Need(TooFar, CommandIntent.Reminder);
        return Ready(Make(due), weight * (time.Defaulted ? 0.85 : 1), 0, false);
    }
}
