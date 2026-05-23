using ChildDev.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChildDev.Api.Tests;

public class DownloadEndpointTests : IAsyncLifetime
{
    private string _tempWebRoot = default!;
    private WebApplicationFactory<Program> _factory = default!;
    private HttpClient _client = default!;

    public Task InitializeAsync()
    {
        _tempWebRoot = Path.Combine(Path.GetTempPath(), "childdev-test-webroot-" + Guid.NewGuid());
        var downloadsDir = Path.Combine(_tempWebRoot, "downloads");
        Directory.CreateDirectory(downloadsDir);
        // Minimal fake APK (must be >0 bytes; deploy guard checks >1 MB but test only checks serving)
        File.WriteAllBytes(Path.Combine(downloadsDir, "LevelUp.apk"), new byte[1024]);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseWebRoot(_tempWebRoot);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CHILDDEV_JWT_SECRET"] = "test-secret-min-32-chars-placeholder"
                });
            });
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase("TestDb_" + Guid.NewGuid()));
            });
        });

        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        if (Directory.Exists(_tempWebRoot))
            Directory.Delete(_tempWebRoot, recursive: true);
    }

    [Fact]
    public async Task GetLevelUpApk_Returns200_WithAndroidMimeType()
    {
        var response = await _client.GetAsync("/downloads/LevelUp.apk");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.android.package-archive",
            response.Content.Headers.ContentType?.MediaType);
    }
}
