using System.Security.Cryptography;
using System.Text;

namespace Shabpa.Core;

/// <summary>The owner's key to Boofi's unlimited feathers. Only a salted PBKDF2 hash of the passphrase is in the program, never the passphrase itself, so reading the
/// source (it is open) does not give it away. It is a convenience for whoever builds and tests Shabpa, not a security boundary: anyone who patches their own copy can
/// do anything with their own feathers anyway, and feathers are only cosmetic.</summary>
public static class AdminKey
{
    private static readonly byte[] Salt = Convert.FromBase64String("xqSmT+aNrfVI34HEsfBZ7A==");
    private static readonly byte[] Hash = Convert.FromBase64String("KlXJjc7uR0hqAyGkHkiPRzxIM/xaTRoazFqoxAas8Qc=");
    private const int Rounds = 200_000;

    /// <summary>The feathers shown while the key is on (so nothing in the shop looks too dear).</summary>
    public const int Shown = 999_999;

    public static bool Verify(string? passphrase) => Verify(passphrase, Salt, Hash);

    internal static bool Verify(string? passphrase, byte[] salt, byte[] hash)
    {
        if (string.IsNullOrEmpty(passphrase) || passphrase.Length > 64) return false;
        var got = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase.Trim()), salt, Rounds, HashAlgorithmName.SHA256, hash.Length);
        return CryptographicOperations.FixedTimeEquals(got, hash);
    }

    private static readonly byte[] GiftSalt = Convert.FromBase64String("zxJNP807aqtoyi/jjiBNyg==");
    private static readonly byte[] GiftHash = Convert.FromBase64String("+GaQe2YcKlV+4bPylQfsjwWRN3L3DEqhnwg1rzoxcVU=");

    /// <summary>A one-time gift code for a friend: it pays <see cref="GiftFeathers"/> once per copy of the program (the same salted-hash idea; the code itself is not in the source).</summary>
    public const int GiftFeathers = 5_000;
    public const string GiftMark = "gift-5k";

    public static bool VerifyGift(string? code) => Verify(code, GiftSalt, GiftHash);

    /// <summary>"gift &lt;code&gt;" (or «هدیه»): the code, or null when the text is not that command.</summary>
    public static string? ParseGift(string? text)
    {
        if (text is null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^(?:gift|هدیه)\s+([A-Za-z0-9\-]{4,64})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>"code &lt;CODE&gt;" (or «کد»): a feather code the owner made in the bot, typed to Boofi; the code in capitals, or null when the text is not that command.
    /// Persian digits are accepted. The server decides whether the code is good; nothing here is secret.</summary>
    public static string? ParseFeatherCode(string? text)
    {
        if (text is null) return null;
        var latin = new string(text.Trim().Select(c => c is >= '\u06F0' and <= '\u06F9' ? (char)('0' + (c - '\u06F0')) : c is >= '\u0660' and <= '\u0669' ? (char)('0' + (c - '\u0660')) : c).ToArray());
        var m = System.Text.RegularExpressions.Regex.Match(latin, @"^(?:code|کد)\s+([A-Za-z0-9_\-]{3,20})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>"admin &lt;passphrase&gt;" (or «ادمین»/«مدیر»): the passphrase, or null when the text is not that command.</summary>
    public static string? Parse(string? text)
    {
        if (text is null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^(?:admin|ادمین|مدیر)\s+([A-Za-z0-9\-]{4,64})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }
}
