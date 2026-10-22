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

public class ArvanMirrorTests
{
    [Fact]
    public void Arvan_mirror_is_https_and_listed_before_the_unprovisioned_domestic_name()
    {
        Assert.StartsWith("https://", UpdateSources.ArvanBase);
        Assert.EndsWith("/update/", UpdateSources.ArvanBase);
        var ids = UpdateSources.Build("", "").Select(s => s.Id).ToList();
        Assert.True(ids.IndexOf(UpdateSources.ArvanId) < ids.IndexOf(UpdateSources.DomesticId));
        Assert.Equal(UpdateSources.ArvanBase + "latest.json", UpdateSources.Build("", "").First(s => s.Id == UpdateSources.ArvanId).ManifestUrl);
    }
}
