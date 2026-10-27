using System.Net;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace Gov.News.Settings;

/// <summary>
/// The public site's read-only view of <c>live.json</c>. Re-checks the blob at most once per
/// <c>refreshInterval</c> and never throws: if storage is unreachable or the document is invalid,
/// it keeps serving the last good settings, or the built-in defaults if there never were any.
/// </summary>
public sealed class CachedSiteSettingsReader : ISiteSettingsSource
{
    private readonly BlobClient _live;
    private readonly TimeSpan _refreshInterval;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private SiteSettings _current = SiteSettings.Defaults();
    private ETag? _etag;
    private DateTimeOffset _nextRefresh = DateTimeOffset.MinValue;

    public CachedSiteSettingsReader(BlobContainerClient container, ILogger<CachedSiteSettingsReader> logger, TimeSpan? refreshInterval = null, TimeProvider? time = null)
    {
        _live = container.GetBlobClient(BlobSiteSettingsStore.LiveBlobName);
        _logger = logger;
        _refreshInterval = refreshInterval ?? TimeSpan.FromSeconds(60);
        _time = time ?? TimeProvider.System;
    }

    public async Task<SiteSettings> GetAsync(CancellationToken ct = default)
    {
        if (_time.GetUtcNow() < _nextRefresh)
            return _current;

        // One request refreshes; everyone else keeps the current copy instead of queueing.
        if (!await _refreshLock.WaitAsync(0, ct))
            return _current;
        try
        {
            await RefreshAsync(ct);
        }
        finally
        {
            _nextRefresh = _time.GetUtcNow() + _refreshInterval;
            _refreshLock.Release();
        }
        return _current;
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            var conditions = _etag is null ? null : new BlobRequestConditions { IfNoneMatch = _etag };
            var response = await _live.DownloadContentAsync(new BlobDownloadOptions { Conditions = conditions }, ct);
            if (response.GetRawResponse().Status == (int)HttpStatusCode.NotModified)
                return;

            var candidate = SiteSettings.FromJson(response.Value.Content.ToString());
            var errors = SettingsValidator.Validate(candidate);
            if (errors.Count > 0)
            {
                _logger.LogError("Ignoring invalid site settings revision {Revision}: {Errors}",
                    candidate.Revision, string.Join("; ", errors.Select(e => $"{e.Field}: {e.Message}")));
                return;
            }
            _current = candidate;
            _etag = response.Value.Details.ETag;
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotModified)
        {
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation("No site settings published yet; using defaults.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not refresh site settings; keeping revision {Revision}.", _current.Revision);
        }
    }
}
