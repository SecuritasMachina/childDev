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
    public void Decrypt_LegacyPlaintextStartingWithVersionPrefix_PassesThrough()
    {
        // A child could have typed text literally beginning with "v1:" before encryption existed.
        // It is NOT our format, so it must pass through unchanged, never throw.
        var svc = Svc();
        Assert.Equal("v1: my plan", svc.Decrypt("v1: my plan"));
        Assert.Equal("v1:hello", svc.Decrypt("v1:hello")); // 'hello' is valid base64 but too short to be a blob
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
        var ct = svc.Encrypt("secret")!;
        // Flip the first base64 payload char (just after the "v1:" prefix), preserving length and
        // base64 validity, so the blob is structurally valid but the AES-GCM auth tag fails.
        var chars = ct.ToCharArray();
        chars[3] = chars[3] == 'A' ? 'B' : 'A';
        var bad = new string(chars);
        Assert.ThrowsAny<Exception>(() => svc.Decrypt(bad));
    }

    [Fact]
    public void Constructor_RejectsWrongKeyLength()
    {
        Assert.Throws<ArgumentException>(() => new EncryptionService(Convert.ToBase64String(new byte[16])));
    }
}
