using ChildDev.Api.Data;
using ChildDev.Api.Services;
using Xunit;

namespace ChildDev.Api.Tests;

public class EncryptedStringConverterTests
{
    private static EncryptionService Svc() =>
        new(Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()));

    [Fact]
    public void Converter_EncryptsToProvider_DecryptsFromProvider()
    {
        var svc = Svc();
        var conv = new EncryptedStringConverter(svc);
        var toProvider = conv.ConvertToProvider.Compile();
        var fromProvider = conv.ConvertFromProvider.Compile();

        var stored = (string?)toProvider("note")!;
        Assert.StartsWith("v1:", stored);
        Assert.Equal("note", (string?)fromProvider(stored));
        Assert.Equal("legacy", (string?)fromProvider("legacy"));
    }
}
