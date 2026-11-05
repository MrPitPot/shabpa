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

    private static readonly byte[] GiftSalt = Convert.FromBase64String("+UHywSlCSDkJsUYqTRGN0Q==");
    private static readonly byte[] GiftHash = Convert.FromBase64String("qW9xmc2VYDCt5I62S/5dV7v8sniOFiiN5e2ESkgFO4g=");

    /// <summary>A one-time gift code for a friend: it pays <see cref="GiftFeathers"/> once per copy of the program (the same salted-hash idea; the code itself is not in the source).</summary>
    public const int GiftFeathers = 10_000;
    public const string GiftMark = "gift-10k";

    public static bool VerifyGift(string? code) => Verify(code, GiftSalt, GiftHash);

    /// <summary>"gift &lt;code&gt;" (or «هدیه»): the code, or null when the text is not that command.</summary>
    public static string? ParseGift(string? text)
    {
        if (text is null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^(?:gift|هدیه)\s+([A-Za-z0-9\-]{4,64})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>"admin &lt;passphrase&gt;" (or «ادمین»/«مدیر»): the passphrase, or null when the text is not that command.</summary>
    public static string? Parse(string? text)
    {
        if (text is null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^(?:admin|ادمین|مدیر)\s+([A-Za-z0-9\-]{4,64})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }
}
