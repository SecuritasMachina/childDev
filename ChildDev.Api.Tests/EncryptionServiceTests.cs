using ChildDev.Api.Services;
using Xunit;

namespace ChildDev.Api.Tests;

public class EncryptionServiceTests
{
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
    private static EncryptionService Svc() => new(Key);

    [Fact]
    public void RoundTrips_PlaintextThroughCipher()
    {
        var svc = Svc();
        var ct = svc.Encrypt("hello world");
        Assert.StartsWith("v1:", ct);
        Assert.NotEqual("hello world", ct);
        Assert.Equal("hello world", svc.Decrypt(ct));
    }

    [Fact]
    public void Decrypt_LegacyPlaintext_PassesThrough()
    {
        var svc = Svc();
        Assert.Equal("legacy notes", svc.Decrypt("legacy notes"));
    }

    [Fact]
    public void NullAndEmpty_PassThrough()
    {
        var svc = Svc();
        Assert.Null(svc.Encrypt(null));
        Assert.Equal("", svc.Encrypt(""));
        Assert.Null(svc.Decrypt(null));
        Assert.Equal("", svc.Decrypt(""));
    }

    [Fact]
    public void Encrypt_UsesFreshNonce_DifferentCiphertextSamePlaintext()
    {
        var svc = Svc();
        Assert.NotEqual(svc.Encrypt("same"), svc.Encrypt("same"));
    }

    [Fact]
    public void Decrypt_Tampered_Throws()
    {
        var svc = Svc();
        var ct = svc.Encrypt("secret");
        var bad = ct[..^2] + (ct.EndsWith("A") ? "B" : "A");
        Assert.ThrowsAny<Exception>(() => svc.Decrypt(bad));
    }

    [Fact]
    public void Constructor_RejectsWrongKeyLength()
    {
        Assert.Throws<ArgumentException>(() => new EncryptionService(Convert.ToBase64String(new byte[16])));
    }
}
