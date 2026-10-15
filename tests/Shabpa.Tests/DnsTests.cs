using Shabpa.Core;
using Xunit;

namespace Shabpa.Tests;

public class DnsTests
{
    [Fact]
    public void Query_is_a_valid_a_record_question()
    {
        var q = DnsProbe.BuildQuery(0x1234, "www.google.com");
        Assert.Equal(0x12, q[0]);
        Assert.Equal(0x34, q[1]);
        Assert.Equal(0x01, q[2]);          // recursion desired
        Assert.Equal(1, q[5]);             // one question
        Assert.Equal(3, q[12]);            // "www"
        Assert.Equal((byte)'w', q[13]);
        Assert.Equal(0, q[^5]);            // root label
        Assert.Equal(1, q[^3]);            // type A
        Assert.Equal(1, q[^1]);            // class IN
    }

    [Fact]
    public void Only_a_reply_with_the_same_id_counts()
    {
        var reply = new byte[] { 0x12, 0x34, 0x81, 0x80, 0, 1, 0, 1, 0, 0, 0, 0 };
        Assert.True(DnsProbe.IsAnswer(reply, reply.Length, 0x1234));
        Assert.False(DnsProbe.IsAnswer(reply, reply.Length, 0x9999));
        var query = new byte[] { 0x12, 0x34, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
        Assert.False(DnsProbe.IsAnswer(query, query.Length, 0x1234)); // not a response
        var refused = new byte[] { 0x12, 0x34, 0x81, 0x85, 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.False(DnsProbe.IsAnswer(refused, refused.Length, 0x1234));
    }

    [Fact]
    public void Catalog_entries_are_valid_unique_ipv4()
    {
        foreach (var e in DnsCatalog.All)
        {
            Assert.True(DnsCatalog.TryParseIPv4(e.Primary, out _), e.Name);
            Assert.True(DnsCatalog.TryParseIPv4(e.Secondary, out _), e.Name);
            Assert.NotEqual(e.Primary, e.Secondary);
        }
        Assert.Equal(DnsCatalog.All.Count, DnsCatalog.All.Select(e => e.Name).Distinct().Count());
        Assert.Equal(DnsCatalog.All.Count, DnsCatalog.All.Select(e => e.Primary + "/" + e.Secondary).Distinct().Count());
        Assert.Contains(DnsCatalog.All, e => e.Group == "iran");
    }

    [Theory]
    [InlineData("1.1.1.1", true)]
    [InlineData(" 8.8.4.4 ", true)]
    [InlineData("256.1.1.1", false)]
    [InlineData("1.1.1", false)]
    [InlineData("::1", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void Address_parsing(string text, bool ok) => Assert.Equal(ok, DnsCatalog.TryParseIPv4(text, out _));

    [Fact]
    public void Applying_without_admin_changes_nothing_and_says_so()
    {
        if (Elevation.IsElevated) return;
        var adapter = new DnsAdapter("x", 1, "id", [], true, true);
        Assert.Equal("admin", DnsConfig.Apply(adapter, "1.1.1.1"));
    }

    [Fact]
    public async Task A_server_that_does_not_exist_times_out_as_null()
    {
        Assert.Null(await DnsProbe.PingAsync("not-an-ip"));
    }
}
