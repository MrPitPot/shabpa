using Shabpa.Core;

namespace Shabpa.Tests;

public class WingetParsingTests
{
    // captured from a real "winget upgrade" run (names shortened), including odd version texts
    private const string Sample = """
        -
        \
        Name                                          Id                                        Version           Available        Source
        ----------------------------------------------------------------------------------------------------------------------------------
        Adobe Acrobat Reader                          Adobe.Acrobat.Reader.32-bit               26.002.21931      26.002.22031     winget
        AnyDesk                                       AnyDesk.AnyDesk                           ad 9.7.15         9.8.2            winget
        Copilot                                       XP9CXNGPPJ97XX                            154.0.4258.53     155.0.4283.45    msstore
        OpenAL                                        CreativeTechnology.OpenAL                 Unknown           1.1              winget
        Ubisoft Connect                               Ubisoft.Connect                           < 174.2.0.13389   174.2.0.13389    winget
        Visual Studio Community 2026 Insiders         Microsoft.VisualStudio.Community.Insiders 18.11.12224.323   18.12.12308.306  winget
        5 upgrades available.
        """;

    [Fact]
    public void The_table_is_read_into_programs_with_both_versions()
    {
        var list = Winget.ParseUpgradeTable(Sample);
        Assert.True(list.Count == 6, string.Join(" | ", list.Select(x => $"[{x.Name}][{x.Id}][{x.Installed}][{x.Available}][{x.Source}]")));
        var acrobat = list[0];
        Assert.Equal("Adobe Acrobat Reader", acrobat.Name);
        Assert.Equal("Adobe.Acrobat.Reader.32-bit", acrobat.Id);
        Assert.Equal("26.002.21931", acrobat.Installed);
        Assert.Equal("26.002.22031", acrobat.Available);
        Assert.Equal("winget", acrobat.Source);
        Assert.Equal("ad 9.7.15", list[1].Installed);
        Assert.Equal("msstore", list[2].Source);
        Assert.True(list[3].InstalledUnknown);
        Assert.True(list[4].InstalledUnknown);
        Assert.Equal("Microsoft.VisualStudio.Community.Insiders", list[5].Id);
        Assert.Equal("18.12.12308.306", list[5].Available);
    }

    [Fact]
    public void Nothing_to_update_and_garbage_give_an_empty_list()
    {
        Assert.Empty(Winget.ParseUpgradeTable("No installed package found matching input criteria."));
        Assert.Empty(Winget.ParseUpgradeTable(""));
        Assert.Empty(Winget.ParseUpgradeTable("Name Id\n----\nfoo bar"));
    }

    [Fact]
    public void Wide_characters_do_not_shift_the_columns()
    {
        const string table = """
            Name                  Id                    Version     Available   Source
            -----------------------------------------------------------------------------
            ツール                Vendor.Tool           1.0         2.0         winget
            Plain                 Vendor.Plain          3.0         4.0         winget
            """;
        // the wide name takes two cells per character, so its row is padded with fewer characters than it looks
        var list = Winget.ParseUpgradeTable(table);
        Assert.Equal(2, list.Count);
        Assert.Equal("Vendor.Tool", list[0].Id);
        Assert.Equal("2.0", list[0].Available);
    }

    [Theory]
    [InlineData("Mozilla.Firefox", true)]
    [InlineData("7zip.7zip", true)]
    [InlineData("XP9CXNGPPJ97XX", true)]
    [InlineData("a & calc", false)]
    [InlineData("--source evil", false)]
    [InlineData("x\"y", false)]
    [InlineData("", false)]
    public async Task Only_plain_package_ids_are_passed_to_winget(string id, bool plain)
    {
        // an id that is not plain is refused before any process starts
        if (plain) return; // a plain id would start winget for real; the refusal path is what is tested here
        Assert.Equal("invalid id", await Winget.UpgradeAsync(id));
    }
}

public class DriverInfoTests
{
    [Theory]
    [InlineData("32.0.15.6094", "560.94")]
    [InlineData("31.0.15.4601", "546.01")]
    [InlineData("32.0.15.5585", "555.85")]
    [InlineData("garbage", "garbage")]
    public void NVIDIA_driver_numbers_are_shown_the_way_the_maker_prints_them(string windows, string expected) =>
        Assert.Equal(expected, Drivers.NvidiaVersion(windows));

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070", GpuVendor.Nvidia)]
    [InlineData("AMD Radeon RX 6900 XT", GpuVendor.Amd)]
    [InlineData("Advanced Micro Devices, Inc.", GpuVendor.Amd)]
    [InlineData("Intel(R) UHD Graphics 770", GpuVendor.Intel)]
    [InlineData("Matrox G200", GpuVendor.Other)]
    public void The_vendor_is_recognised(string text, GpuVendor expected) => Assert.Equal(expected, Drivers.VendorOf(text));

    [Fact]
    public void Official_pages_are_https_and_belong_to_the_makers()
    {
        foreach (var v in new[] { GpuVendor.Nvidia, GpuVendor.Amd, GpuVendor.Intel })
        {
            var page = new GpuDriver("x", v, "1", "1", null, "").DownloadPage;
            Assert.StartsWith("https://", page);
            var host = new Uri(page).Host;
            Assert.True(host.EndsWith("nvidia.com") || host.EndsWith("amd.com") || host.EndsWith("intel.com"), host);
        }
        Assert.Equal("", new GpuDriver("x", GpuVendor.Other, "1", "1", null, "").DownloadPage);
    }

    [Fact]
    public void Windows_update_driver_lines_are_parsed()
    {
        const string output = "DRV|Realtek - Audio - 6.0.9500.1|Realtek|MEDIA|Realtek High Definition Audio|2026-09-01\nnoise line\nDRV|bad|too|short";
        var offers = Drivers.ParseOffers(output);
        var o = Assert.Single(offers);
        Assert.Equal("Realtek", o.Manufacturer);
        Assert.Equal("MEDIA", o.Class);
        Assert.Equal(new DateTime(2026, 9, 1), o.Date);
    }

    [Fact]
    public void The_real_graphics_card_list_can_be_read()
    {
        var gpus = Drivers.Gpus();
        Assert.All(gpus, g =>
        {
            Assert.NotEmpty(g.Name);
            Assert.NotEmpty(g.Version);
        });
    }
}
