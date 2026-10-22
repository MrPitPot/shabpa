using Shabpa.Core;

namespace Shabpa.Tests;

public class SupportTests
{
    [Fact]
    public void The_owner_card_is_hidden_from_everyone_else_and_the_server_address_is_built_in()
    {
        Assert.False(Shabpa.Pages.SupportPage.OwnerVisible(false, null));
        Assert.False(Shabpa.Pages.SupportPage.OwnerVisible(false, "0"));
        Assert.True(Shabpa.Pages.SupportPage.OwnerVisible(true, null));
        Assert.True(Shabpa.Pages.SupportPage.OwnerVisible(false, "1"));
        Assert.True(SupportClient.IsValidEndpoint(SupportClient.DefaultEndpoint));
    }

    [Theory]
    [InlineData("https://shabpa-support.example.workers.dev", true)]
    [InlineData("http://shabpa-support.example.workers.dev", false)]
    [InlineData("ftp://x.test", false)]
    [InlineData("nonsense", false)]
    [InlineData("", false)]
    public void Only_https_server_addresses_are_accepted(string value, bool expected) => Assert.Equal(expected, SupportClient.IsValidEndpoint(value));

    [Fact]
    public void A_github_link_carries_the_text_and_a_label_and_stays_short()
    {
        var url = GitHubIssue.NewIssueUrl("Overlay is empty", "FPS shows a dash & nothing else", "bug");
        Assert.StartsWith("https://github.com/MrPitPot/shabpa/issues/new?title=", url);
        Assert.Contains("FPS%20shows%20a%20dash%20%26%20nothing%20else", url);
        Assert.EndsWith("&labels=bug", url);
        Assert.True(GitHubIssue.NewIssueUrl("t", new string('x', 20000), "idea").Length < 5200);
    }

    [Fact]
    public void Diagnostics_hold_only_simple_technical_facts()
    {
        var text = SupportInfo.Diagnostics(true);
        Assert.Contains("Shabpa", text);
        Assert.Contains("Administrator: True", text);
        Assert.DoesNotContain(Environment.UserName, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_servers_ticket_list_is_read()
    {
        const string json = """{"tickets":[{"id":"ab12cd34ef","title":"Hi","body":"Text here","category":"bug","status":"answered","created":"2026-10-15T10:00:00.000Z","replies":[{"text":"Run as admin","at":"2026-10-15T11:00:00.000Z","admin":true}],"diag":"v2.8.0"}]}""";
        var tickets = SupportClient.ParseListing(json);
        var t = Assert.Single(tickets);
        Assert.Equal("answered", t.Status);
        Assert.True(t.Replies[0].Admin);
        Assert.Equal("v2.8.0", t.Diag);
    }

    [Fact]
    public void The_built_in_address_makes_the_server_route_available_but_nobody_is_admin_without_a_key()
    {
        var settings = new SupportSettings();
        var client = new SupportClient(() => settings, () => "");
        Assert.True(client.Available);
        Assert.Equal(SupportClient.DefaultEndpoint, client.Endpoint);
        Assert.False(client.IsAdmin);
        settings.Endpoint = "https://x.example.workers.dev/";
        Assert.True(client.Available);
        Assert.Equal("https://x.example.workers.dev", client.Endpoint);
        settings.AdminKeyProtected = Secret.Protect("secret");
        Assert.True(client.IsAdmin);
        Assert.Equal("secret", client.AdminKey);
    }
}
