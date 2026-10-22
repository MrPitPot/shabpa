using System.Security.Cryptography;
using Shabpa.Core;

namespace Shabpa.Tests;

public class UpdateKeyTests
{
    [Fact]
    public void Embedded_public_key_is_a_valid_P256_key()
    {
        Assert.NotEmpty(UpdateKeys.PublicKeyBase64);
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(UpdateKeys.PublicKeyBase64), out _);
        Assert.Equal(256, key.KeySize);
    }
}
