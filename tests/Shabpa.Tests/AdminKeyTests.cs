using System.IO;
using System.Security.Cryptography;
using System.Text;
using Shabpa.Core;

namespace Shabpa.Tests;

/// <summary>The owner's key to unlimited feathers: only a salted hash is in the program, the wrong word never works, the key is read only from the exact command,
/// and while it is on buying costs nothing and the level does not move.</summary>
public class AdminKeyTests
{
    private static (byte[] salt, byte[] hash) For(string word)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return (salt, Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(word), salt, 200_000, HashAlgorithmName.SHA256, 32));
    }

    [Fact]
    public void Only_the_right_word_passes_and_the_real_key_rejects_guesses()
    {
        var (salt, hash) = For("test-word-1234");
        Assert.True(AdminKey.Verify("test-word-1234", salt, hash));
        Assert.True(AdminKey.Verify("  test-word-1234 ", salt, hash));
        Assert.False(AdminKey.Verify("test-word-1235", salt, hash));
        Assert.False(AdminKey.Verify("", salt, hash));
        Assert.False(AdminKey.Verify(null, salt, hash));
        Assert.False(AdminKey.Verify(new string('a', 500), salt, hash));
        Assert.False(AdminKey.Verify("password"));
        Assert.False(AdminKey.Verify("1234"));
    }

    [Theory]
    [InlineData("admin abcd-efgh", "abcd-efgh")]
    [InlineData("ADMIN abcd-efgh", "abcd-efgh")]
    [InlineData("مدیر abcd-efgh", "abcd-efgh")]
    [InlineData("ادمین abcd-efgh ", "abcd-efgh")]
    [InlineData("admin", null)]
    [InlineData("please admin abcd-efgh", null)]
    [InlineData("admin abcd efgh", null)]
    [InlineData("اینو بگو admin x", null)]
    public void The_key_is_read_only_from_the_exact_command(string text, string? word) => Assert.Equal(word, AdminKey.Parse(text));

    [Fact]
    public void The_command_is_an_info_command_so_it_never_reaches_a_power_action()
    {
        var result = CommandParser.Parse("admin abcd-efgh", new DateTime(2026, 11, 3, 12, 0, 0));
        Assert.Equal(CommandIntent.Info, result.Command!.Intent);
        Assert.Equal("admin:abcd-efgh", result.Command.Value);
    }

    [Fact]
    public void With_the_key_on_buying_costs_nothing_and_the_level_does_not_move()
    {
        var hub = new Hub(new Store(Path.Combine(Path.GetTempPath(), "shabpa-ak-" + Guid.NewGuid().ToString("N"))) { ReadOnly = true }, dryRun: true, demo: true);
        hub.State.Rewards = new RewardsState { Feathers = 10, TotalFeathers = 10 };
        var e = new RewardsEngine(hub, () => new DateTime(2026, 11, 2, 12, 0, 0));
        var dear = BoofiCatalog.All.OrderByDescending(p => p.Price).First();
        Assert.Equal(RewardsEngine.BuyResult.NotEnough, e.Buy(dear.Id));

        e.SetUnlimited(true);
        Assert.Equal(AdminKey.Shown, e.State.Feathers);
        Assert.Equal(10, e.State.TotalFeathers);                                 // the level counts what was earned, not this
        Assert.Equal(RewardsEngine.BuyResult.Ok, e.Buy(dear.Id));
        Assert.True(e.State.Feathers >= AdminKey.Shown);                         // nothing was spent (the first-buy quest may even pay a little)

        e.SetUnlimited(false);
        Assert.Equal(10 + (e.State.TotalFeathers - 10), e.State.Feathers);       // the real balance comes back: what was earned meanwhile is in it, the key gave nothing
        Assert.Contains(dear.Id, e.State.Owned);                                 // what was bought stays
    }

    [Fact]
    public void Switching_the_key_off_does_not_refund_what_was_spent_before()
    {
        var hub = new Hub(new Store(Path.Combine(Path.GetTempPath(), "shabpa-ak-" + Guid.NewGuid().ToString("N"))) { ReadOnly = true }, dryRun: true, demo: true);
        hub.State.Rewards = new RewardsState { Feathers = 100, TotalFeathers = 500 };     // earned 500, spent 400
        var e = new RewardsEngine(hub, () => new DateTime(2026, 11, 2, 12, 0, 0));
        e.SetUnlimited(true);
        Assert.Equal(100, e.State.Balance);                                                   // the shown number is large, the balance is the real one
        Assert.Equal(100, e.State.RealFeathers);
        e.SetUnlimited(false);
        Assert.Equal(100, e.State.Feathers);                                              // not 500
        Assert.Equal(0, e.State.RealFeathers);
    }
}
