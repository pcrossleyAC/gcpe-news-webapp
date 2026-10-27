namespace Gov.News.Settings.Tests;

public class BlobSiteSettingsStoreTests
{
    private static SiteSettings WithBanner(string message)
    {
        var settings = SiteSettings.Defaults();
        settings.Banner.Message = message;
        return settings;
    }

    [AzuriteFact]
    public async Task First_publish_becomes_revision_1_and_clears_the_draft()
    {
        var container = Azurite.NewContainer();
        var store = new BlobSiteSettingsStore(container);
        var draft = WithBanner("First");
        await store.SaveDraftAsync("jane.editor@gov.bc.ca", draft, baseLiveETag: null);
        Assert.Null((await store.GetDraftAsync("jane.editor@gov.bc.ca"))!.BaseLiveETag);

        var result = await store.PublishAsync(draft, expectedLiveETag: null, "jane.editor@gov.bc.ca", "Launch");

        Assert.Equal(PublishStatus.Published, result.Status);
        var live = await store.GetLiveAsync();
        Assert.NotNull(live);
        Assert.Equal(1, live.Settings.Revision);
        Assert.Equal("First", live.Settings.Banner.Message);
        Assert.Equal("jane.editor@gov.bc.ca", live.Settings.PublishedBy);
        Assert.Equal(result.Live!.ETag, live.ETag);
        Assert.Null(await store.GetDraftAsync("jane.editor@gov.bc.ca"));
        var history = Assert.Single(await store.ListHistoryAsync());
        Assert.Equal(1, history.Revision);
        Assert.Equal("Launch", history.Note);
    }

    [AzuriteFact]
    public async Task Second_editor_working_from_a_stale_version_gets_a_conflict_not_an_overwrite()
    {
        var store = new BlobSiteSettingsStore(Azurite.NewContainer());
        var start = (await store.PublishAsync(WithBanner("Original"), null, "admin", null)).Live!;

        // Both editors open the admin while revision 1 is live.
        var alice = await store.PublishAsync(WithBanner("Alice's wording"), start.ETag, "alice", null);
        var bob = await store.PublishAsync(WithBanner("Bob's wording"), start.ETag, "bob", null);

        Assert.Equal(PublishStatus.Published, alice.Status);
        Assert.Equal(PublishStatus.Conflict, bob.Status);
        Assert.Equal("Alice's wording", bob.Live!.Settings.Banner.Message); // Bob is shown what Alice published
        var live = await store.GetLiveAsync();
        Assert.Equal("Alice's wording", live!.Settings.Banner.Message);
        Assert.Equal([2, 1], (await store.ListHistoryAsync()).Select(h => h.Revision));
    }

    [AzuriteFact]
    public async Task Simultaneous_publishes_from_the_same_version_let_exactly_one_through()
    {
        var store = new BlobSiteSettingsStore(Azurite.NewContainer());
        var start = (await store.PublishAsync(WithBanner("Original"), null, "admin", null)).Live!;

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            store.PublishAsync(WithBanner($"Editor {i}"), start.ETag, $"editor{i}", null)));

        var winner = Assert.Single(results, r => r.Status == PublishStatus.Published);
        Assert.All(results.Where(r => r != winner), r => Assert.Equal(PublishStatus.Conflict, r.Status));
        var live = await store.GetLiveAsync();
        Assert.Equal(winner.Live!.Settings.Banner.Message, live!.Settings.Banner.Message);
        Assert.Equal(2, live.Settings.Revision);
        // History holds the winner's revision 2, not a loser's.
        Assert.Equal([2, 1], (await store.ListHistoryAsync()).Select(h => h.Revision));
        Assert.Equal(winner.Live.Settings.PublishedBy, (await store.ListHistoryAsync())[0].PublishedBy);
    }

    [AzuriteFact]
    public async Task Two_first_publishes_cannot_both_succeed()
    {
        var store = new BlobSiteSettingsStore(Azurite.NewContainer());

        var first = await store.PublishAsync(WithBanner("A"), null, "a", null);
        var second = await store.PublishAsync(WithBanner("B"), null, "b", null);

        Assert.Equal(PublishStatus.Published, first.Status);
        Assert.Equal(PublishStatus.Conflict, second.Status);
        Assert.Equal("A", (await store.GetLiveAsync())!.Settings.Banner.Message);
    }

    [AzuriteFact]
    public async Task Draft_remembers_the_live_version_it_started_from()
    {
        var store = new BlobSiteSettingsStore(Azurite.NewContainer());
        var live = (await store.PublishAsync(WithBanner("Live"), null, "a", null)).Live!;

        await store.SaveDraftAsync("jane", WithBanner("Work in progress"), live.ETag);

        var draft = await store.GetDraftAsync("jane");
        Assert.Equal("Work in progress", draft!.Settings.Banner.Message);
        Assert.Equal(live.ETag, draft.BaseLiveETag);
    }

    [AzuriteFact]
    public async Task Each_editor_has_their_own_draft_and_another_editors_publish_leaves_it_alone()
    {
        var store = new BlobSiteSettingsStore(Azurite.NewContainer());
        var live = (await store.PublishAsync(WithBanner("Live"), null, "setup", null)).Live!;
        await store.SaveDraftAsync("jane@gov.bc.ca", WithBanner("Jane's draft"), live.ETag);
        await store.SaveDraftAsync("sam@gov.bc.ca", WithBanner("Sam's draft"), live.ETag);

        await store.PublishAsync(WithBanner("Sam's draft"), live.ETag, "sam@gov.bc.ca", null);

        Assert.Null(await store.GetDraftAsync("sam@gov.bc.ca"));
        Assert.Equal("Jane's draft", (await store.GetDraftAsync("jane@gov.bc.ca"))!.Settings.Banner.Message);
        Assert.Equal("Jane's draft", (await store.GetDraftAsync("JANE@gov.bc.ca "))!.Settings.Banner.Message); // same person, however the name is cased
    }

    [AzuriteFact]
    public async Task Invalid_settings_are_not_written_anywhere()
    {
        var container = Azurite.NewContainer();
        var store = new BlobSiteSettingsStore(container);
        var draft = SiteSettings.Defaults();
        draft.Footer.MoreInfoLinks[0].Url = "javascript:alert(1)";

        var result = await store.PublishAsync(draft, null, "jane", null);

        Assert.Equal(PublishStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Field == "footer.moreInfoLinks[0].url");
        Assert.Null(await store.GetLiveAsync());
        Assert.Empty(await store.ListHistoryAsync());
    }

    [AzuriteFact]
    public async Task Restore_republishes_an_old_revision_as_a_new_one()
    {
        var store = new BlobSiteSettingsStore(Azurite.NewContainer());
        var r1 = (await store.PublishAsync(WithBanner("Election period"), null, "sam", null)).Live!;
        var r2 = (await store.PublishAsync(WithBanner("Wildfire emergency"), r1.ETag, "sam", null)).Live!;

        var restored = await store.RestoreAsync(1, r2.ETag, "jane");

        Assert.Equal(PublishStatus.Published, restored.Status);
        Assert.Equal(3, restored.Live!.Settings.Revision);
        Assert.Equal("Election period", restored.Live.Settings.Banner.Message);
        Assert.Equal("Restored revision 1", (await store.ListHistoryAsync())[0].Note);
        Assert.Equal(PublishStatus.NotFound, (await store.RestoreAsync(99, restored.Live.ETag, "jane")).Status);
    }

    [AzuriteFact]
    public async Task Notes_and_names_with_accents_survive_history()
    {
        var store = new BlobSiteSettingsStore(Azurite.NewContainer());

        await store.PublishAsync(WithBanner("x"), null, "andré@gov.bc.ca", "Feux de forêt — mise à jour");

        var entry = Assert.Single(await store.ListHistoryAsync());
        Assert.Equal("andré@gov.bc.ca", entry.PublishedBy);
        Assert.Equal("Feux de forêt — mise à jour", entry.Note);
    }
}
