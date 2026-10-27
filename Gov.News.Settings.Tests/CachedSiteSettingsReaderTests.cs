using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Gov.News.Settings.Tests;

public class CachedSiteSettingsReaderTests
{
    private static CachedSiteSettingsReader Reader(BlobContainerClient container, FakeTimeProvider time) =>
        new(container, NullLogger<CachedSiteSettingsReader>.Instance, TimeSpan.FromSeconds(60), time);

    private static SiteSettings WithBanner(string message)
    {
        var settings = SiteSettings.Defaults();
        settings.Banner.Message = message;
        return settings;
    }

    [AzuriteFact]
    public async Task Nothing_published_yet_serves_defaults()
    {
        var reader = Reader(Azurite.NewContainer(), new FakeTimeProvider());

        var settings = await reader.GetAsync();

        Assert.Equal(0, settings.Revision);
        Assert.Equal(SiteSettings.Defaults().Banner.Message, settings.Banner.Message);
    }

    [AzuriteFact]
    public async Task New_revision_appears_after_the_refresh_interval_not_before()
    {
        var container = Azurite.NewContainer();
        var store = new BlobSiteSettingsStore(container);
        var time = new FakeTimeProvider();
        var reader = Reader(container, time);
        var r1 = (await store.PublishAsync(WithBanner("One"), null, "a", null)).Live!;
        Assert.Equal("One", (await reader.GetAsync()).Banner.Message);

        await store.PublishAsync(WithBanner("Two"), r1.ETag, "a", null);
        time.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal("One", (await reader.GetAsync()).Banner.Message);

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("Two", (await reader.GetAsync()).Banner.Message);
    }

    [AzuriteFact]
    public async Task Hand_edited_invalid_document_is_ignored_and_last_good_kept()
    {
        var container = Azurite.NewContainer();
        var store = new BlobSiteSettingsStore(container);
        var time = new FakeTimeProvider();
        var reader = Reader(container, time);
        await store.PublishAsync(WithBanner("Good"), null, "a", null);
        Assert.Equal("Good", (await reader.GetAsync()).Banner.Message);

        // Someone bypasses the admin and uploads a broken file straight to storage.
        var bad = WithBanner("Bad");
        bad.Theme.Colors.Link = "#FFFF00";
        await container.GetBlobClient(BlobSiteSettingsStore.LiveBlobName).UploadAsync(BinaryData.FromString(bad.ToJson()), overwrite: true);
        time.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal("Good", (await reader.GetAsync()).Banner.Message);
    }

    [AzuriteFact]
    public async Task Malformed_json_is_ignored_and_last_good_kept()
    {
        var container = Azurite.NewContainer();
        var store = new BlobSiteSettingsStore(container);
        var time = new FakeTimeProvider();
        var reader = Reader(container, time);
        await store.PublishAsync(WithBanner("Good"), null, "a", null);
        Assert.Equal("Good", (await reader.GetAsync()).Banner.Message);

        await container.GetBlobClient(BlobSiteSettingsStore.LiveBlobName).UploadAsync(BinaryData.FromString("{ not json"), overwrite: true);
        time.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal("Good", (await reader.GetAsync()).Banner.Message);
    }

    [Fact]
    public async Task Unreachable_storage_serves_defaults_without_throwing()
    {
        // Nothing listens on port 1; no retries so the test stays fast.
        var options = new BlobClientOptions { Retry = { MaxRetries = 0, NetworkTimeout = TimeSpan.FromSeconds(2) } };
        var container = new BlobContainerClient(new Uri("http://127.0.0.1:1/devstoreaccount1/settings"), options);
        var reader = Reader(container, new FakeTimeProvider());

        var settings = await reader.GetAsync();

        Assert.Equal(SiteSettings.Defaults().Banner.Message, settings.Banner.Message);
    }
}
