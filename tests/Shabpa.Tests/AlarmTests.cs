using System.IO;
using Shabpa.Core;

namespace Shabpa.Tests;

/// <summary>A due reminder is an alarm: a sound that repeats and a window above everything. It becomes an ordinary notice only when the person switched the alarm off. No sound is played here.</summary>
public class AlarmTests
{
    private static Hub NewHub() => new(new Store(Path.Combine(Path.GetTempPath(), "shabpa-al-" + Guid.NewGuid().ToString("N"))) { ReadOnly = true }, dryRun: true, demo: true) { Synchronous = true };

    [Fact]
    public void The_alarm_sound_is_a_valid_mono_wav_with_four_beeps_and_a_pause()
    {
        var wav = AlarmTone.Wav();
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));                        // mono
        Assert.Equal(44100, BitConverter.ToInt32(wav, 24));
        Assert.Equal(wav.Length - 44, BitConverter.ToInt32(wav, 40));
        var samples = Enumerable.Range(0, (wav.Length - 44) / 2).Select(i => (int)BitConverter.ToInt16(wav, 44 + i * 2)).ToArray();
        Assert.InRange(samples.Max(), 10_000, 30_000);                         // clearly audible, never clipping
        var perSecond = 44100;
        Assert.All(samples.Skip((int)(0.8 * perSecond)), s => Assert.Equal(0, s));   // the last second is the pause
        // four separate bursts: count the stretches of sound
        var bursts = 0; var inside = false;
        foreach (var s in samples.Take((int)(0.7 * perSecond)))
        {
            var loud = Math.Abs(s) > 2000;
            if (loud && !inside) bursts++;
            inside = loud || (inside && s != 0 && Math.Abs(s) > 200);   // a quiet moment inside a wave is not the gap between beeps
        }
        Assert.True(bursts >= 4, $"{bursts} bursts");
    }

    [Fact]
    public void A_reminder_raises_the_alarm_unless_the_person_switched_it_off()
    {
        var hub = NewHub();
        var alarms = new List<(string, string, bool)>();
        var notices = new List<string>();
        hub.AlarmRaised += (t, x, snooze) => alarms.Add((t, x, snooze is not null));
        hub.Notified += (t, _) => notices.Add(t);

        hub.Alarm("Task reminder", "call mom", () => { });
        Assert.Equal([("Task reminder", "call mom", true)], alarms);
        Assert.Empty(notices);

        hub.Settings.AlarmSound = false;
        hub.Alarm("Task reminder", "again");
        Assert.Single(alarms);                                                  // no new alarm
        Assert.Equal(["Task reminder"], notices);                               // an ordinary notice instead
        Assert.Equal(2, hub.Notices.Count);                                     // both are in the notice list
    }

    [Fact]
    public void The_alarm_is_on_by_default()
    {
        Assert.True(new Settings().AlarmSound);
    }
}
