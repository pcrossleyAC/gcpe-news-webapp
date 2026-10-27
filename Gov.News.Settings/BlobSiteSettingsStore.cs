using System.Net;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Gov.News.Settings;

/// <summary>A settings document and the blob ETag it was read with.</summary>
public sealed record StoredSettings(SiteSettings Settings, string ETag);

public enum PublishStatus { Published, Conflict, Invalid, NotFound }

/// <summary>
/// Outcome of a publish or restore. On <see cref="PublishStatus.Conflict"/>, <see cref="Live"/> is what
/// someone else published in the meantime, so the editor can review it before trying again.
/// </summary>
public sealed record PublishResult(PublishStatus Status, StoredSettings? Live, IReadOnlyList<ValidationError> Errors)
{
    public static PublishResult Published(StoredSettings live) => new(PublishStatus.Published, live, []);
    public static PublishResult Conflict(StoredSettings? live) => new(PublishStatus.Conflict, live, []);
    public static PublishResult Invalid(IReadOnlyList<ValidationError> errors) => new(PublishStatus.Invalid, null, errors);
    public static PublishResult NotFound() => new(PublishStatus.NotFound, null, []);
}

public sealed record HistoryEntry(int Revision, DateTimeOffset? PublishedAt, string? PublishedBy, string? Note);

/// <summary>
/// Read/write access to site settings in one Azure Blob container, laid out as:
/// <code>
/// live.json            what the public site reads
/// draft.json           work in progress in the admin
/// history/000042.json  a copy of every published revision
/// </code>
/// Blob ETags stand in for database transactions: a publish only lands if <c>live.json</c>
/// is still the version the editor started from, so two editors can't overwrite each other.
/// </summary>
public sealed class BlobSiteSettingsStore
{
    public const string LiveBlobName = "live.json";
    public const string DraftBlobName = "draft.json";
    public const string HistoryPrefix = "history/";

    private readonly BlobContainerClient _container;
    private readonly TimeProvider _time;

    public BlobSiteSettingsStore(BlobContainerClient container, TimeProvider? time = null)
    {
        _container = container;
        _time = time ?? TimeProvider.System;
    }

    public Task<StoredSettings?> GetLiveAsync(CancellationToken ct = default) => ReadAsync(LiveBlobName, ct);

    public Task<StoredSettings?> GetDraftAsync(CancellationToken ct = default) => ReadAsync(DraftBlobName, ct);

    public async Task SaveDraftAsync(SiteSettings draft, CancellationToken ct = default) =>
        await WriteAsync(_container.GetBlobClient(DraftBlobName), draft, conditions: null, metadata: null, ct);

    public async Task DiscardDraftAsync(CancellationToken ct = default) =>
        await _container.GetBlobClient(DraftBlobName).DeleteIfExistsAsync(cancellationToken: ct);

    /// <summary>
    /// Validates <paramref name="draft"/> and makes it live as the next revision.
    /// <paramref name="expectedLiveETag"/> is the ETag of the live settings the editor started from,
    /// or null if nothing has ever been published.
    /// </summary>
    public async Task<PublishResult> PublishAsync(SiteSettings draft, string? expectedLiveETag, string publishedBy, string? note, CancellationToken ct = default)
    {
        var errors = SettingsValidator.Validate(draft);
        if (errors.Count > 0)
            return PublishResult.Invalid(errors);

        var current = await GetLiveAsync(ct);
        if (current?.ETag != expectedLiveETag)
            return PublishResult.Conflict(current);

        var next = SiteSettings.FromJson(draft.ToJson());
        next.Revision = (current?.Settings.Revision ?? 0) + 1;
        next.PublishedAt = _time.GetUtcNow();
        next.PublishedBy = publishedBy;
        next.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        // History first, and only if no one else has claimed this revision number.
        var history = _container.GetBlobClient(HistoryBlobName(next.Revision));
        var metadata = new Dictionary<string, string>
        {
            ["publishedBy"] = Uri.EscapeDataString(publishedBy),
            ["publishedAt"] = next.PublishedAt.Value.ToString("O"),
            ["note"] = Uri.EscapeDataString(next.Note ?? ""),
        };
        try
        {
            await WriteAsync(history, next, new BlobRequestConditions { IfNoneMatch = ETag.All }, metadata, ct);
        }
        catch (RequestFailedException ex) when (IsConditionFailure(ex))
        {
            return PublishResult.Conflict(await GetLiveAsync(ct));
        }

        var liveConditions = expectedLiveETag is null
            ? new BlobRequestConditions { IfNoneMatch = ETag.All }
            : new BlobRequestConditions { IfMatch = new ETag(expectedLiveETag) };
        try
        {
            var info = await WriteAsync(_container.GetBlobClient(LiveBlobName), next, liveConditions, metadata: null, ct);
            await DiscardDraftAsync(ct);
            return PublishResult.Published(new StoredSettings(next, info.ETag.ToString()));
        }
        catch (RequestFailedException ex) when (IsConditionFailure(ex))
        {
            await history.DeleteIfExistsAsync(cancellationToken: ct);
            return PublishResult.Conflict(await GetLiveAsync(ct));
        }
    }

    /// <summary>Publishes an earlier revision again, as a new revision.</summary>
    public async Task<PublishResult> RestoreAsync(int revision, string? expectedLiveETag, string publishedBy, CancellationToken ct = default)
    {
        var old = await ReadAsync(HistoryBlobName(revision), ct);
        if (old is null)
            return PublishResult.NotFound();
        return await PublishAsync(old.Settings, expectedLiveETag, publishedBy, $"Restored revision {revision}", ct);
    }

    /// <summary>Published revisions, newest first.</summary>
    public async Task<IReadOnlyList<HistoryEntry>> ListHistoryAsync(CancellationToken ct = default)
    {
        var entries = new List<HistoryEntry>();
        await foreach (var item in _container.GetBlobsAsync(BlobTraits.Metadata, BlobStates.None, HistoryPrefix, ct))
        {
            var name = Path.GetFileNameWithoutExtension(item.Name);
            if (!int.TryParse(name, out var revision))
                continue;
            item.Metadata.TryGetValue("publishedBy", out var by);
            item.Metadata.TryGetValue("publishedAt", out var at);
            item.Metadata.TryGetValue("note", out var note);
            entries.Add(new HistoryEntry(
                revision,
                DateTimeOffset.TryParse(at, out var when) ? when : null,
                by is null ? null : Uri.UnescapeDataString(by),
                string.IsNullOrEmpty(note) ? null : Uri.UnescapeDataString(note)));
        }
        return entries.OrderByDescending(e => e.Revision).ToList();
    }

    public static string HistoryBlobName(int revision) => $"{HistoryPrefix}{revision:D6}.json";

    private async Task<StoredSettings?> ReadAsync(string blobName, CancellationToken ct)
    {
        try
        {
            var result = await _container.GetBlobClient(blobName).DownloadContentAsync(ct);
            return new StoredSettings(SiteSettings.FromJson(result.Value.Content.ToString()), result.Value.Details.ETag.ToString());
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static async Task<BlobContentInfo> WriteAsync(BlobClient blob, SiteSettings settings, BlobRequestConditions? conditions, IDictionary<string, string>? metadata, CancellationToken ct)
    {
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" },
            Conditions = conditions,
            Metadata = metadata,
        };
        var response = await blob.UploadAsync(BinaryData.FromString(settings.ToJson()), options, ct);
        return response.Value;
    }

    // 412 when If-Match fails; 409 when If-None-Match:* finds an existing blob.
    private static bool IsConditionFailure(RequestFailedException ex) =>
        ex.Status is (int)HttpStatusCode.PreconditionFailed or (int)HttpStatusCode.Conflict;
}
