using Shabpa.Core;

namespace Shabpa;

// ---- typed commands («یه ساعت دیگه خاموش کن»): the command box and free text from @ShabPa_Bot ----------------------
public sealed partial class Hub
{
    /// <summary>The shortcut that opens the command box (null in demo runs).</summary>
    public AssistantHotkey? AssistantKey { get; private set; }

    /// <summary>Runs a command the person has already confirmed on the card. Power actions go through the same paths as
    /// the Power page: "now" shows the cancellable countdown, a time goes into the queue (which shows its own countdown),
    /// a condition becomes the active rule. Returns what happened, in the current language.</summary>
    public RemoteReply RunAssistant(AssistantCommand command, string origin)
    {
        var now = DateTime.Now;
        Log.Write($"assistant {origin}: {command.Intent} {command.Action} at={command.At:yyyy-MM-dd HH:mm} rule={command.Condition?.Kind}");
        // a card that waited on screen past its own time must not run late
        if (command.At is { } late && late <= now && command.Intent is CommandIntent.Power or CommandIntent.Reminder && command.Repeat.Count == 0)
            return Reply(false, L.T("زمانش گذشت. دوباره بنویس.", "That time has passed. Type it again."));
        switch (command.Intent)
        {
            case CommandIntent.Power when command.Condition is { } rule:
            {
                var replaced = Engine.Rule is not null;
                ActivateRule(rule);
                return Reply(true, L.T("قانون شرطی فعال شد", "The conditional rule is on") + (replaced ? L.T(" (جای قانون قبلی).", " (it replaces the previous one).") : "."));
            }
            case CommandIntent.Power when command.At is { } at:
            {
                var action = command.Action ?? PowerAction.Sleep;
                return AddCommand(action, at, command.Repeat.Count > 0 ? command.Repeat : null)
                    ? Reply(true, L.T($"ثبت شد: {Fa.DayOf(at, now)} ساعت {Fa.Clock(at)} {Fa.ActionSentence(action)}.", $"Scheduled: {Fa.DayOf(at, now)} at {Fa.Clock(at)} {Fa.ActionSentence(action)}."))
                    : Reply(true, L.T("همین دستور از قبل توی صف بود.", "The same command was already in the queue."));
            }
            case CommandIntent.Power:
            {
                var action = command.Action ?? PowerAction.Sleep;
                if (action == PowerAction.Lock)
                {
                    Run(action);
                    return Reply(true, L.T("صفحه قفل شد.", "The screen is locked."));
                }
                var seconds = Math.Max(5, Settings.CountdownSeconds);
                return RequestAction(action, L.T("فرمان متنی", "Typed command"), seconds)
                    ? Reply(true, L.T($"{Fa.Digits(seconds.ToString())} ثانیه‌ی دیگه {Fa.ActionSentence(action)}؛ شمارش معکوس روی صفحه‌ست و می‌تونی لغوش کنی.",
                        $"In {seconds} seconds {Fa.ActionSentence(action)}; the countdown is on screen and can be cancelled."))
                    : Reply(false, L.T("الان یه شمارش معکوس دیگه روی صفحه‌ست. اول لغوش کن.", "Another countdown is already on screen. Cancel it first."));
            }
            case CommandIntent.Cancel:
            {
                var sleep = SleepActive;
                CancelSleepMode();
                var text = CancelEverything();
                if (sleep) text += "\n" + L.T("حالت شب‌خواب هم لغو شد.", "Sleep mode was cancelled too.");
                return Reply(true, text);
            }
            case CommandIntent.Wake when command.At is { } wake:
            {
                var error = AddWake(new WakeSchedule { Hour = wake.Hour, Minute = wake.Minute, Days = [.. command.Repeat] });
                return error is null
                    ? Reply(true, L.T($"بیدارباش ثبت شد: {Fa.Days(command.Repeat)} ساعت {Fa.Clock(wake)}.", $"Wake timer set: {Fa.Days(command.Repeat)} at {Fa.Clock(wake)}."))
                    : Reply(false, error);
            }
            case CommandIntent.Reminder:
            {
                var item = Tasks.Add(command.Title, command.At, command.At is null ? -1 : 0, now: now);
                if (item is null) return Reply(false, L.T("کار اضافه نشد (لیست پره یا عنوان خالیه).", "The task was not added (the list is full or the title is empty)."));
                return Reply(true, command.At is { } due
                    ? L.T($"یادآوری ثبت شد: {Fa.DayOf(due, now)} ساعت {Fa.Clock(due)}.", $"Reminder set: {Fa.DayOf(due, now)} at {Fa.Clock(due)}.")
                    : L.T("به کارها اضافه شد.", "Added to your tasks."));
            }
            case CommandIntent.Focus when !command.On:
                Focus.Stop(now);
                Store.Save();
                return Reply(true, L.T("تایمر تمرکز متوقف شد.", "The focus timer is stopped."));
            case CommandIntent.Focus:
                if (command.Minutes > 0) Settings.Focus.FocusMinutes = Math.Clamp(command.Minutes, 5, 90);
                Store.Save();
                Focus.Start(now);
                return Reply(true, L.T($"تمرکز {Fa.Digits(Settings.Focus.FocusMinutes.ToString())} دقیقه‌ای شروع شد.", $"A {Settings.Focus.FocusMinutes}-minute focus started."));
            case CommandIntent.NightLight:
            {
                var c = Settings.Comfort;
                c.DimOn = command.On;
                if (command.Level > 0) c.DimLevel = Math.Clamp(command.Level / 100.0, 0.05, 0.92);
                Store.Save();
                Dim?.Apply();
                ComfortChanged();
                return Reply(true, command.On ? L.T("نور شب روشن شد.", "Night light is on.") : L.T("نور شب خاموش شد.", "Night light is off."));
            }
            case CommandIntent.SleepMode when !command.On:
                if (!SleepActive) return Reply(true, L.T("حالت شب‌خواب روشن نبود.", "Sleep mode was not on."));
                CancelSleepMode();
                return Reply(true, L.T("حالت شب‌خواب لغو شد.", "Sleep mode is cancelled."));
            case CommandIntent.SleepMode:
            {
                if (SleepActive) return Reply(false, L.T("حالت شب‌خواب از قبل روشنه.", "Sleep mode is already on."));
                var c = Settings.Comfort;
                if (command.Minutes > 0) c.SleepMinutes = Math.Clamp(command.Minutes, 1, 600);
                if (command.Action is { } end) c.SleepAction = end;
                Store.Save();
                StartSleepMode();
                return Reply(true, L.T($"حالت شب‌خواب شروع شد؛ {Fa.Digits(c.SleepMinutes.ToString())} دقیقه‌ی دیگه {Fa.ActionSentence(c.SleepAction)}.",
                    $"Sleep mode started; in {c.SleepMinutes} minutes {Fa.ActionSentence(c.SleepAction)}."));
            }
            case CommandIntent.ListSchedules:
                return Reply(true, ScheduleText(now));
            default:
                return Reply(false, L.T("این دستور رو نمی‌شناسم.", "Unknown command."));
        }
    }

    private static RemoteReply Reply(bool ok, string text) => new(ok, text, text);

    /// <summary>Everything that is waiting: queued commands, the conditional rule, wake timers and sleep mode.</summary>
    public string ScheduleText(DateTime now)
    {
        var lines = new List<string>();
        foreach (var c in Queue.Items.OrderBy(c => c.DueAt))
            lines.Add(c.Repeats
                ? L.T($"⏳ {Fa.ActionName(c.Action)}: {Fa.Days(c.RepeatDays)} ساعت {Fa.Clock(c.DueAt)}", $"⏳ {Fa.ActionName(c.Action)}: {Fa.Days(c.RepeatDays)} at {Fa.Clock(c.DueAt)}")
                : L.T($"⏳ {Fa.ActionName(c.Action)}: {Fa.DayOf(c.DueAt, now)} ساعت {Fa.Clock(c.DueAt)}", $"⏳ {Fa.ActionName(c.Action)}: {Fa.DayOf(c.DueAt, now)} at {Fa.Clock(c.DueAt)}"));
        if (Engine.Rule is { } rule)
            lines.Add(L.T($"🎯 قانون شرطی ({Fa.ActionName(rule.Action)}): {Engine.Status.Text}", $"🎯 Conditional rule ({Fa.ActionName(rule.Action)}): {Engine.Status.Text}"));
        foreach (var w in State.WakeSchedules)
            lines.Add(L.T($"⏰ بیدارباش: {Fa.Days(w.Days)} ساعت {Fa.Clock(w.Hour, w.Minute)}", $"⏰ Wake: {Fa.Days(w.Days)} at {Fa.Clock(w.Hour, w.Minute)}"));
        if (SleepActive) lines.Add(L.T("🌙 حالت شب‌خواب روشنه.", "🌙 Sleep mode is on."));
        return lines.Count == 0 ? L.T("هیچ دستوری در راه نیست.", "Nothing is scheduled.") : string.Join("\n", lines);
    }

    /// <summary>Free text sent to @ShabPa_Bot. Only a clear reading runs, through the same commands the bot already has
    /// (power still shows its on-screen countdown); anything unclear is answered with the choices, and nothing runs.</summary>
    internal RemoteReply ExecuteText(string text)
    {
        var now = DateTime.Now;
        var result = CommandParser.Parse(text, now);
        if (result is { Status: CommandStatus.Ready, Command: { } command })
        {
            if (command.ToBotRequest(now) is { } request)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(request.Args);
                return ExecuteRemote(request.Type, doc.RootElement.Clone());
            }
            return RunAssistant(command, "bot");
        }
        var lines = new List<string> { result.Question };
        // the bot has no buttons for these yet: the person answers by writing one of them out in full
        foreach (var choice in result.Choices) lines.Add("• " + choice.Summary(now));
        return RemoteReply.Fail(string.Join("\n", lines), string.Join("\n", lines));
    }
}
