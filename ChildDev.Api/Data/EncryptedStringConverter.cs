using System.Linq.Expressions;
using ChildDev.Api.Services;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ChildDev.Api.Data;

/// <summary>EF value converter that encrypts strings at rest via <see cref="EncryptionService"/>.</summary>
public sealed class EncryptedStringConverter : ValueConverter<string?, string?>
{
    public EncryptedStringConverter(EncryptionService enc)
        : base(v => enc.Encrypt(v), v => enc.Decrypt(v)) { }

    /// <summary>Typed encrypt expression; shadows the base Func property so tests can call .Compile().</summary>
    public new Expression<Func<string?, string?>> ConvertToProvider =>
        ConvertToProviderExpression;

    /// <summary>Typed decrypt expression; shadows the base Func property so tests can call .Compile().</summary>
    public new Expression<Func<string?, string?>> ConvertFromProvider =>
        ConvertFromProviderExpression;
}
