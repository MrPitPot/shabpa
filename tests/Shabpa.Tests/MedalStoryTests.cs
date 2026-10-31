using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;
using Xunit;

namespace Shabpa.Tests;

[Collection("Wpf")]
public class MedalStoryTests
{
    private static void Prepare()
    {
        StaRunner.EnsureApp();
        _ = new Hub(new Store(Path.Combine(Path.GetTempPath(), "shabpa-story-" + Guid.NewGuid().ToString("N"))) { ReadOnly = true }, dryRun: true, demo: true);
    }

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        if (root is TextBlock tb)
        {
            if (tb.Inlines.Count > 0 && tb.Inlines.OfType<Run>().Any()) yield return string.Concat(tb.Inlines.OfType<Run>().Select(r => r.Text)); // the poster colours one word, so a title is several runs
            else yield return tb.Text;
        }
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var t in Texts(child)) yield return t;
        if (root is Decorator { Child: { } c }) foreach (var t in Texts(c)) yield return t;
    }

    private static bool HasPersian(string s) => s.Any(c => c is >= '\u0600' and <= '\u06FF');

    private static bool HasLatin(string s) => s.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');

    [Fact]
    public void There_are_ten_designs_with_names_and_hints_in_both_languages()
    {
        Assert.Equal(10, MedalStory.Designs.Length);
        Assert.Equal(10, MedalStory.Designs.Select(d => d.Key).Distinct().Count());
        Assert.All(MedalStory.Designs, d => { Assert.True(HasPersian(d.FaName) && !HasLatin(d.FaName)); Assert.True(HasLatin(d.EnName) && !HasPersian(d.EnName)); Assert.True(HasPersian(d.FaHint)); Assert.True(HasLatin(d.EnHint)); });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_design_of_every_kind_of_medal_draws_and_speaks_one_language(bool english)
    {
        StaRunner.Run(() =>
        {
            Prepare();
            using var _ = L.Scope(english);
            // one medal of every tier, a long name and a hidden one
            var medals = RewardsCatalog.Medals.GroupBy(m => m.Tier).Select(g => g.First()).Append(RewardsCatalog.Find("explorer")!).Append(RewardsCatalog.Find("allmedals")!).ToList();
            foreach (var medal in medals)
            {
                var data = MedalStory.DataFor(medal, new DateTime(2026, 11, 2, 12, 0, 0));
                foreach (var design in MedalStory.Designs)
                {
                    var card = MedalStory.Build(design.Key, data);
                    var text = string.Join(" ", Texts(card));
                    Assert.False(string.IsNullOrWhiteSpace(text), $"{design.Key}: no text");
                    Assert.Contains(medal.Name, text);
                    // the picture says «shabpa.ir» and a serial number in Latin letters; everything else is in the app's language
                    var words = text.Replace("shabpa.ir", "").Replace("No. SP-", "").Replace("ACHIEVEMENT", english ? "" : "ACHIEVEMENT");
                    if (english) Assert.False(HasPersian(words), $"{design.Key} ({medal.Id}): Persian text in the English card: {words}");
                    else Assert.False(HasLatin(Strip(words)), $"{design.Key} ({medal.Id}): Latin text in the Persian card: {words}");
                    var bmp = MedalStory.Render(card);
                    Assert.Equal((1080, 1920), (bmp.PixelWidth, bmp.PixelHeight));
                }
            }
        });
    }

    /// <summary>Letters of the serial number (SP-20261102-24) are allowed in the Persian card; nothing else is.</summary>
    private static string Strip(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"SP-\d+-\d+", "");

    [Fact]
    public void A_design_is_not_blank_and_the_designs_differ()
    {
        StaRunner.Run(() =>
        {
            Prepare();
            var data = MedalStory.DataFor(RewardsCatalog.Find("marathon")!, new DateTime(2026, 11, 2));
            var hashes = new HashSet<string>();
            foreach (var design in MedalStory.Designs)
            {
                var bmp = MedalStory.Render(MedalStory.Build(design.Key, data));
                var pixels = new byte[bmp.PixelWidth * bmp.PixelHeight * 4];
                bmp.CopyPixels(pixels, bmp.PixelWidth * 4, 0);
                Assert.True(pixels.Distinct().Count() > 20, $"{design.Key} looks blank");
                hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)));
            }
            Assert.Equal(10, hashes.Count);
        });
    }

    [Fact]
    public void Saving_writes_a_png_of_the_story_size()
    {
        StaRunner.Run(() =>
        {
            Prepare();
            var file = Path.Combine(Path.GetTempPath(), "shabpa-story-test-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                MedalStory.Save(MedalStory.Build("card", MedalStory.DataFor(RewardsCatalog.Find("hello")!, DateTime.Now)), file);
                var bytes = File.ReadAllBytes(file);
                Assert.True(bytes.Length > 20_000);
                Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
            }
            finally { File.Delete(file); }
        });
    }
}
