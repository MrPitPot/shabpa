// Language (L.En) is process-wide, so tests must not run side by side.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Shabpa.Tests;

public class RateQueueTests
{
    // 1 Mbit/s = 125,000 bytes per second.
    private const double Mbit = 125_000;

    private static double Pump(RateQueue<int> queue, double seconds, int packetSize, double offeredBytesPerSecond)
    {
        // Offers a steady stream for a while and returns the bytes that got through.
        double sent = 0, now = 0, offered = 0;
        const double step = 0.004;
        while (now < seconds)
        {
            now += step;
            offered += offeredBytesPerSecond * step;
            while (offered >= packetSize)
            {
                offered -= packetSize;
                if (queue.Offer(0, packetSize, now) == Verdict.Send) sent += packetSize;
            }
            queue.Release(now, _ => sent += packetSize);
        }
        return sent;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(50)]
    public void Throughput_settles_at_the_configured_rate(double mbit)
    {
        var queue = new RateQueue<int>(mbit * Mbit);
        var sent = Pump(queue, seconds: 10, packetSize: 1400, offeredBytesPerSecond: mbit * Mbit * 4);
        Assert.InRange(sent / 10, mbit * Mbit * 0.95, mbit * Mbit * 1.08);
    }

    [Fact]
    public void Traffic_below_the_rate_is_never_delayed_or_dropped()
    {
        var queue = new RateQueue<int>(10 * Mbit);
        double now = 0;
        for (var i = 0; i < 2000; i++)
        {
            now += 0.01; // 1400 bytes every 10 ms = 1.1 Mbit, far below 10
            Assert.Equal(Verdict.Send, queue.Offer(i, 1400, now));
        }
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void A_flood_is_dropped_once_the_queue_is_full_and_the_queue_stays_bounded()
    {
        var queue = new RateQueue<int>(1 * Mbit);
        var verdicts = Enumerable.Range(0, 2000).Select(i => queue.Offer(i, 1400, 0)).ToList();
        Assert.Contains(Verdict.Queued, verdicts);
        Assert.Contains(Verdict.Drop, verdicts);
        Assert.True(queue.QueuedBytes <= queue.MaxQueuedBytes);
    }

    [Fact]
    public void Queued_packets_leave_in_order()
    {
        var queue = new RateQueue<int>(1 * Mbit);
        for (var i = 0; i < 40; i++) queue.Offer(i, 1400, 0);
        var released = new List<int>();
        for (var t = 0.0; t < 5; t += 0.01) queue.Release(t, released.Add);
        Assert.Equal(released.Order(), released);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void A_packet_larger_than_the_burst_still_passes_and_is_paid_for()
    {
        // 64 KB loopback packets through a 0.5 Mbit cap: one packet, then a wait of about a second.
        var queue = new RateQueue<int>(0.5 * Mbit);
        Assert.Equal(Verdict.Send, queue.Offer(1, 64_000, 0));
        Assert.Equal(Verdict.Queued, queue.Offer(2, 64_000, 0.01));
        var released = 0;
        queue.Release(0.5, _ => released++);
        Assert.Equal(0, released);
        queue.Release(1.2, _ => released++);
        Assert.Equal(1, released);
    }

    [Fact]
    public void Flush_releases_everything_at_once()
    {
        var queue = new RateQueue<int>(1 * Mbit);
        for (var i = 0; i < 30; i++) queue.Offer(i, 1400, 0);
        var waiting = queue.Count;
        var flushed = 0;
        queue.Flush(_ => flushed++);
        Assert.True(waiting > 0);
        Assert.Equal(waiting, flushed);
        Assert.Equal(0, queue.QueuedBytes);
    }
}

public class PacketParserTests
{
    private static byte[] Ipv4(byte protocol, int source, int destination, int fragmentOffset = 0, int headerWords = 5)
    {
        var packet = new byte[headerWords * 4 + 20];
        packet[0] = (byte)(0x40 | headerWords);
        packet[6] = (byte)(fragmentOffset >> 8);
        packet[7] = (byte)fragmentOffset;
        packet[9] = protocol;
        var at = headerWords * 4;
        packet[at] = (byte)(source >> 8); packet[at + 1] = (byte)source;
        packet[at + 2] = (byte)(destination >> 8); packet[at + 3] = (byte)destination;
        return packet;
    }

    [Fact]
    public void Tcp_over_ipv4()
    {
        Assert.True(PacketParser.TryParse(Ipv4(6, 51234, 443), out var tcp, out var source, out var destination));
        Assert.Equal((true, 51234, 443), (tcp, source, destination));
    }

    [Fact]
    public void Udp_over_ipv4_with_ip_options()
    {
        Assert.True(PacketParser.TryParse(Ipv4(17, 5353, 60000, headerWords: 7), out var tcp, out var source, out var destination));
        Assert.Equal((false, 5353, 60000), (tcp, source, destination));
    }

    [Fact]
    public void Later_fragments_and_other_protocols_are_left_alone()
    {
        Assert.False(PacketParser.TryParse(Ipv4(6, 1, 2, fragmentOffset: 185), out _, out _, out _));
        Assert.False(PacketParser.TryParse(Ipv4(1, 0, 0), out _, out _, out _)); // ICMP
        Assert.False(PacketParser.TryParse(new byte[8], out _, out _, out _));
    }

    [Fact]
    public void Tcp_over_ipv6_with_and_without_an_extension_header()
    {
        var plain = new byte[60];
        plain[0] = 0x60;
        plain[6] = 6;
        plain[40] = 0xC8; plain[41] = 0x22; plain[42] = 0x01; plain[43] = 0xBB;
        Assert.True(PacketParser.TryParse(plain, out var tcp, out var source, out var destination));
        Assert.Equal((true, 51234, 443), (tcp, source, destination));

        var extended = new byte[68];
        extended[0] = 0x60;
        extended[6] = 0;      // hop-by-hop options first
        extended[40] = 17;    // then UDP
        extended[41] = 0;     // 8 bytes long
        extended[48] = 0x14; extended[49] = 0xE9; extended[50] = 0x00; extended[51] = 0x35;
        Assert.True(PacketParser.TryParse(extended, out tcp, out source, out destination));
        Assert.Equal((false, 5353, 53), (tcp, source, destination));
    }

    [Fact]
    public void Port_table_lists_this_machines_sockets()
    {
        // A live check against Windows: every PC has at least a few listening ports owned by real processes.
        var table = PortTable.Read();
        Assert.NotEmpty(table);
        Assert.All(table.Keys, key => Assert.InRange(key & 0xFFFF, 1, 65535));
        Assert.Contains(table.Keys, key => (key & 0x10000) != 0);
        Assert.Contains(table.Keys, key => (key & 0x20000) != 0);
    }

    [Fact]
    public void A_socket_opened_here_is_attributed_to_this_process()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        Assert.Equal(Environment.ProcessId, PortTable.Read()[PortTable.Key(true, port)]);

        using var udp = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.IPv6Loopback, 0));
        var udpPort = ((System.Net.IPEndPoint)udp.Client.LocalEndPoint!).Port;
        Assert.Equal(Environment.ProcessId, PortTable.Read()[PortTable.Key(false, udpPort)]);
    }
}

public class DownloadLimitTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 21, 0, 0);

    [Fact]
    public void A_download_cap_alone_is_a_limit()
    {
        var limit = new AppLimit { Key = "steam", DownMbps = 20 };
        Assert.False(limit.IsEmpty);
        var planned = Assert.Single(LimitPlanner.Plan([limit], false, 1, "", [], Now));
        Assert.Equal((20.0, 0.0, false), (planned.DownMbps, planned.UpMbps, planned.Blocked));
    }

    [Fact]
    public void In_game_cap_keeps_the_users_download_cap()
    {
        var app = new AppTraffic("steam", "Steam", "", 0, 0, 1, 1, Now);
        var planned = Assert.Single(LimitPlanner.Plan([new AppLimit { Key = "steam", DownMbps = 20 }], true, 1, "cs2", [app], Now));
        Assert.Equal((20.0, 1.0), (planned.DownMbps, planned.UpMbps));
    }

    [Fact]
    public void Signature_tracks_download_caps_too()
    {
        var a = LimitPlanner.Signature([new EffectiveLimit("x", 10, 1, false, false)]);
        var b = LimitPlanner.Signature([new EffectiveLimit("x", 20, 1, false, false)]);
        Assert.NotEqual(a, b);
    }
}

public class PaletteTests
{
    public static TheoryData<string> Themes => [.. Palette.All.Select(p => p.Key)];

    [Fact]
    public void There_are_dark_and_light_themes_including_the_requested_ones()
    {
        Assert.Equal(["night", "black", "white", "pink", "purple", "emerald", "bluviolet", "pinkviolet", "sunset", "ocean", "aurora", "vampire", "fjord", "neon", "retro", "teal", "duskrose", "code", "latte"], Palette.All.Select(p => p.Key));
        Assert.Contains(Palette.All, p => p.Light);
        Assert.Equal("night", Palette.Find("no-such-theme").Key);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Every_theme_defines_every_colour(string theme)
    {
        var palette = Palette.Find(theme);
        Assert.Equal(Palette.Keys.Order(), palette.Colors.Keys.Order());
        Assert.All(palette.Colors.Values, hex => Assert.Matches("^[0-9A-F]{6}$", hex));
    }

    /// <summary>The readability rule the user asked for, as numbers: WCAG AA needs 4.5:1 for normal text
    /// and 3:1 for large or bold text; main text here is held to the stricter AAA 7:1.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void Text_stays_readable_on_every_surface(string theme)
    {
        var p = Palette.Find(theme);
        void AtLeast(double ratio, string foreground, string background) =>
            Assert.True(p.Contrast(foreground, background) >= ratio,
                $"{theme}: {foreground} on {background} is {p.Contrast(foreground, background):0.00}, needs {ratio}");

        foreach (var surface in new[] { "Bg", "Bg2", "Side", "Side2", "Panel", "Panel2", "Panel3", "Hero", "Hero2", "AccentSoft" })
            AtLeast(7, "Text", surface);
        foreach (var surface in new[] { "Bg", "Bg2", "Side", "Side2", "Panel", "Panel2" })
            AtLeast(4.5, "Muted", surface);
        foreach (var surface in new[] { "Side", "Panel", "Panel2" })
            AtLeast(3, "Faint", surface);
        foreach (var colour in new[] { "Accent", "Gold", "Green", "Red" })
        {
            AtLeast(4.5, colour, "Panel");
            AtLeast(4.5, colour, "Panel2");
            AtLeast(4.5, colour, colour + "Soft");
        }
        AtLeast(4.5, "Gold", "Hero");
        AtLeast(4.5, "Gold", "Hero2");
        // Bold button labels on the accent gradient.
        AtLeast(3.5, "OnAccent", "Accent1");
        AtLeast(3.5, "OnAccent", "Accent2");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Light_flag_matches_the_background(string theme)
    {
        var palette = Palette.Find(theme);
        Assert.Equal(palette.Light, palette.Rgb("Bg").g > palette.Rgb("Text").g);
    }
}

/// <summary>Language is process-wide state, so these run one after another and always switch back.</summary>
[Collection("language")]
public class EnglishTests
{
    private static void InEnglish(Action test)
    {
        L.En = true;
        try { test(); }
        finally { L.En = false; }
    }

    [Fact]
    public void English_uses_latin_digits_gregorian_dates_and_short_durations() => InEnglish(() =>
    {
        Assert.Equal("07:30", Fa.Clock(7, 30));
        Assert.Equal("Oct 14", Fa.Date(new DateTime(2026, 10, 14)));
        Assert.Equal("2 h 5 min", Fa.Duration(TimeSpan.FromSeconds(7500)));
        Assert.Equal("40 s", Fa.Duration(TimeSpan.FromSeconds(40)));
        Assert.Equal("12,345", Fa.N(12345));
        Assert.Equal("1.50", Fa.N(1.5, 2));
        Assert.Equal("Every day", Fa.Days([]));
        Assert.Equal("Shut down", Fa.ActionName(PowerAction.Shutdown));
        Assert.Equal(DayOfWeek.Monday, Fa.Week[0]);
    });

    [Fact]
    public void Persian_is_the_default_and_unchanged()
    {
        Assert.False(L.En);
        Assert.Equal("۰۷:۳۰", Fa.Clock(7, 30));
        Assert.Equal("۱۲٬۳۴۵", Fa.N(12345));
        Assert.Equal("خاموش کردن", Fa.ActionName(PowerAction.Shutdown));
        Assert.Equal(DayOfWeek.Saturday, Fa.Week[0]);
        Assert.Equal("۰٫۵", Fa.Mbit(0.5));
        Assert.Equal("۲۰", Fa.Mbit(20));
    }

    [Fact]
    public void Rule_and_bot_texts_follow_the_language() => InEnglish(() =>
    {
        var engine = new RuleEngine();
        engine.Activate(new Rule { Kind = RuleKind.Idle, HoldMinutes = 10 });
        Assert.Equal("You are using the PC.", engine.Evaluate(new SensorSnapshot { IdleSeconds = 1 }, DateTime.Now).Text);

        Assert.Equal("Restarted", new PowerEvent(DateTime.Now, PowerEventKind.Restart).Title);
    });
}

public class MemoryTests
{
    [Fact]
    public void Memory_reading_is_sane()
    {
        var info = MemoryCleaner.Read();
        Assert.True(info.Total > 1L << 30, "this PC should have more than 1 GB of RAM");
        Assert.InRange(info.Available, 1, info.Total);
        Assert.InRange(info.UsedPercent, 1, 100);
        Assert.InRange(info.Standby, 0, info.Total);
    }

    [Fact]
    public void Trimming_this_process_works_without_admin()
    {
        Assert.Equal(1, MemoryCleaner.TrimEach(Environment.ProcessId));
    }
}
