#if DEBUG || SHABPA_TESTHOOKS
using System.IO;
using System.Text;
using System.Windows;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Test aid (Debug / SHABPA_TESTHOOKS builds, screenshot runs only): opens the Halloween scare, saves a picture of the pumpkin
/// and of the greeting, and checks the layer goes away again. The sound is switched off for the run so nothing is played.</summary>
public static class HalloweenProbe
{
    public static async Task Run(MainWindow window, string folder, Action<FrameworkElement, string> save)
    {
        Directory.CreateDirectory(folder);
        var tag = $"{(L.En ? "en" : "fa")}-{(Ui.BaseTheme.Light ? "light" : "dark")}";
        var log = new StringBuilder();
        void Say(string line) => log.AppendLine($"[{tag}] {line}");
        try
        {
            Hub.I.Settings.UiSounds = false;
            await Task.Delay(3500);
            Say($"the greeting opened by itself at the first look of the window: {window.HalloweenOpen}");
            window.CloseHalloween();
            Say($"UiSounds={Hub.I.Settings.UiSounds}; the scream starts anyway: {Halloween.PlayScream()}; interface sounds held meanwhile: {!UiSound.Enabled}");
            Motion.TestOverride = true;
            window.ShowHalloween();
            await Task.Delay(330);
            Say($"scare open={window.HalloweenOpen}, card={(window.HalloweenCard is null ? "not yet" : "already")}");
            save(window.CaptureRoot, Path.Combine(folder, $"scare-{tag}.png"));
            await Task.Delay(2100);
            Say($"after the scare: open={window.HalloweenOpen}, card={(window.HalloweenCard is null ? "missing" : "shown")}");
            save(window.CaptureRoot, Path.Combine(folder, $"greeting-{tag}.png"));
            window.ShowHalloween();                                                  // a second call while open must do nothing
            Say($"second call kept one layer: {window.CaptureRootOverlaysHalloween}");
            System.Windows.Input.Keyboard.Focus(window);
            window.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.UIElement.PreviewKeyDownEvent });
            await Task.Delay(200);
            Say($"after Esc: open={window.HalloweenOpen}");
        }
        catch (Exception ex)
        {
            Say("FAILED " + ex);
        }
        File.AppendAllText(Path.Combine(folder, "halloween.log"), log.ToString());
        Application.Current.Shutdown(0);
    }
}
#endif