using System.Security.Cryptography;
using System.Text;

namespace ChildDev.Api.Services;

/// <summary>AES-256-GCM string encryption with a version-tagged, backward-compatible format.</summary>
public sealed class EncryptionService
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public EncryptionService(string base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
            throw new ArgumentException("Encryption key is not configured.", nameof(base64Key));
        byte[] key;
        try { key = Convert.FromBase64String(base64Key); }
        catch (FormatException) { throw new ArgumentException("Encryption key must be base64.", nameof(base64Key)); }
        if (key.Length != 32)
            throw new ArgumentException($"Encryption key must decode to 32 bytes, got {key.Length}.", nameof(base64Key));
        _key = key;
    }

    public string? Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ct = new byte[pt.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, pt, ct, tag);
        var blob = new byte[NonceSize + TagSize + ct.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
        Buffer.BlockCopy(ct, 0, blob, NonceSize + TagSize, ct.Length);
        return Prefix + Convert.ToBase64String(blob);
    }

    public string? Decrypt(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;
        var blob = Convert.FromBase64String(stored.Substring(Prefix.Length));
        var nonce = blob.AsSpan(0, NonceSize);
        var tag = blob.AsSpan(NonceSize, TagSize);
        var ct = blob.AsSpan(NonceSize + TagSize);
        var pt = new byte[ct.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ct, tag, pt);
        return Encoding.UTF8.GetString(pt);
    }

    public bool IsEncrypted(string? stored) =>
        stored is not null && stored.StartsWith(Prefix, StringComparison.Ordinal);
}
