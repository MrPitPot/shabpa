#if DEBUG || SHABPA_TESTHOOKS
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Test aid (Debug / SHABPA_TESTHOOKS builds, screenshot runs only): walks through the birthday feature on a fake
/// "today" (SHABPA_FAKE_TODAY) and saves screenshots plus a short log. Nothing is saved to disk: the run is read-only.</summary>
public static class BirthdayProbe
{
    public static async Task Run(MainWindow window, string folder, Action<FrameworkElement, string> save)
    {
        Directory.CreateDirectory(folder);
        var tag = $"{(L.En ? "en" : "fa")}-{(Ui.BaseTheme.Light ? "light" : "dark")}";
        var log = new StringBuilder();
        var hub = Hub.I;
        void Say(string line) => log.AppendLine($"[{tag}] {line}");
        try
        {
            hub.Settings.Animations = false;
            await Task.Delay(3500);
            window.Navigate("settings");
            await Task.Delay(1000);
            var card = All(window.PageFor("settings")).OfType<BirthdayCard>().First();
            save(card, Path.Combine(folder, $"settings-empty-{tag}.png"));
            Say($"today={Birthday.Now():yyyy-MM-dd} baseTheme={Ui.BaseTheme.Key} activeTheme={Ui.Theme.Key} savedTheme={hub.Settings.Theme} gameRunning={hub.Snapshot.GameRunning}");

            // fill the form (made-up person; Persian: 15 Aban, English: 6 Nov) and look at the card
            hub.Settings.Birthday = new BirthdaySettings { Name = L.En ? "Ali" : "علی", Jalali = !L.En, Month = L.En ? 11 : 8, Day = L.En ? 6 : 15 };
            card.Reload();
            await Task.Delay(400);
            card.BringIntoView(); await Task.Delay(500); save(window.CaptureRoot, Path.Combine(folder, $"settings-filled-{tag}.png"));

            // the wheels: keyboard, a year, and the day count following the month and year
            var (dayW, monthW, yearW) = card.Wheels;
            Say($"wheels: day={dayW.CentreText} month={monthW.CentreText} year={yearW.CentreText} fields={card.Fields}");
            void Press(WheelColumn w, System.Windows.Input.Key key)
            {
                w.Focus();
                w.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(w)!, 0, key) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
            }
            Press(dayW, System.Windows.Input.Key.Down);
            Press(dayW, System.Windows.Input.Key.Down);
            Press(dayW, System.Windows.Input.Key.Up);
            Say($"after Down,Down,Up on the day wheel: {card.Fields}");
            yearW.Pick(L.En ? 36 : 15, 0); // some years back
            monthW.Pick(L.En ? 1 : 11, 0); // Feb / Esfand
            dayW.Pick(L.En ? 28 : 29, 0);
            await Task.Delay(300);
            Say($"Feb/Esfand with a year: days={dayW.Count} {card.Fields}");
            card.BringIntoView(); await Task.Delay(500); save(window.CaptureRoot, Path.Combine(folder, $"settings-wheel-{tag}.png"));
            yearW.Pick(0, 0);
            await Task.Delay(200);
            Say($"Feb/Esfand without a year: days={dayW.Count} {card.Fields}");
            card.Reload();
            await Task.Delay(300);

            // the day arrives: motion on, so the circle reveal and the shower really run
            hub.Settings.Animations = true;
            var cpu0 = Process.GetCurrentProcess().TotalProcessorTime;
            hub.RecheckBirthday();
            Say($"after recheck: today={hub.BirthdayToday} look={hub.BirthdayLook} activeTheme={Ui.Theme.Key} savedTheme={hub.Settings.Theme} baseTheme={Ui.BaseTheme.Key} revealRunning={window.RevealRunning} greetingOpen={window.BirthdayOpen} decor={window.BirthdayDecor} lastGreeted={hub.Settings.Birthday.LastGreeted}");
            await Task.Delay(2100);
            var confetti = window.BirthdayConfetti;
            Say($"mid-shower: running={confetti?.Running} visiblePieces={confetti?.Visible} revealRunning={window.RevealRunning}");
            save(window.CaptureRoot, Path.Combine(folder, $"greeting-{tag}.png"));
            await Task.Delay(5200);
            Say($"after shower: running={confetti?.Running} visiblePieces={confetti?.Visible}");
            var idle0 = Process.GetCurrentProcess().TotalProcessorTime;
            await Task.Delay(8000);
            var idleCpu = (Process.GetCurrentProcess().TotalProcessorTime - idle0).TotalMilliseconds / 8000 * 100;
            Say($"cpu while greeting stays open and settled (8 s): {idleCpu:0.0}% of one core; total since greeting {(idle0 - cpu0).TotalMilliseconds:0} ms");
            window.CloseBirthdayGreeting();

            // a second greeting is not due the same day
            hub.RecheckBirthday();
            await Task.Delay(1500);
            Say($"second check same day: greetingOpen={window.BirthdayOpen}");

            hub.Settings.Animations = false; // settled pages
            window.Navigate("home");
            await Task.Delay(1500);
            save(window.CaptureRoot, Path.Combine(folder, $"home-{tag}.png"));
            window.Navigate("system:rules");
            await Task.Delay(1200);
            save(window.CaptureRoot, Path.Combine(folder, $"control-{tag}.png"));
            window.Navigate("playtime");
            await Task.Delay(1200);
            save(window.CaptureRoot, Path.Combine(folder, $"playtime-{tag}.png"));
            window.Navigate("settings");
            await Task.Delay(800);
            save(window.CaptureRoot, Path.Combine(folder, $"settings-{tag}.png"));

            // the still version (animations off)
            window.ShowBirthdayGreeting();
            await Task.Delay(700);
            Say($"static greeting: running={window.BirthdayConfetti?.Running} visiblePieces={window.BirthdayConfetti?.Visible}");
            save(window.CaptureRoot, Path.Combine(folder, $"greeting-static-{tag}.png"));
            window.CloseBirthdayGreeting();

            // midnight passes: the clock reads the next day
            Birthday.FakeClock(Birthday.Now().Date.AddDays(1).AddMinutes(1));
            await Task.Delay(2500);
            Say($"next day: today={hub.BirthdayToday} look={hub.BirthdayLook} activeTheme={Ui.Theme.Key} baseTheme={Ui.BaseTheme.Key} savedTheme={hub.Settings.Theme} decor={window.BirthdayDecor}");
            window.Navigate("home");
            await Task.Delay(1000);
            save(window.CaptureRoot, Path.Combine(folder, $"home-nextday-{tag}.png"));
        }
        catch (Exception ex)
        {
            Say("ERROR " + ex);
        }
        File.WriteAllText(Path.Combine(folder, $"probe-{tag}.txt"), log.ToString());
        hub.Shutdown();
        Application.Current.Shutdown();
    }

    private static IEnumerable<DependencyObject> All(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in All(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
#endif
