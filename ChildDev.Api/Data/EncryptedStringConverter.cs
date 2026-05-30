using ChildDev.Api.Services;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ChildDev.Api.Data;

/// <summary>EF value converter that encrypts strings at rest via <see cref="EncryptionService"/>.</summary>
public sealed class EncryptedStringConverter : ValueConverter<string?, string?>
{
    public EncryptedStringConverter(EncryptionService enc)
        : base(v => enc.Encrypt(v), v => enc.Decrypt(v)) { }
}
