using Marmotte.ChatUi.Application;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;
using static Marmotte.ChatUi.Core.Tests.ConversationTests;

namespace Marmotte.ChatUi.Core.Tests;

/// <summary>Verifies profile and preference validation, ownership, selection consistency, paging, and cancellation.</summary>
public sealed class SettingsTests
{
    private readonly TestIdentity identity = new(new UserId("alice"));
    private readonly TestStore store = new();
    private readonly TestClock clock = new();
    private ConnectionProfileService Profiles => new(identity, store, clock);
    private UserPreferencesService Preferences => new(identity, store, store);
    private static SaveProfileRequest Profile => new("Local", new Uri("http://localhost/custom/api"), "local-model");

    [Fact]
    public async Task Profile_and_preferences_round_trip_and_deletion_clears_selection()
    {
        Assert.Equal(new PreferencesView(ThemePreference.System, null, null), await Preferences.LoadAsync(TestContext.Current.CancellationToken));
        var profile = await Profiles.CreateAsync(Profile, TestContext.Current.CancellationToken);
        Assert.Null(profile.Temperature);
        Assert.Null(profile.MaxOutputTokens);
        clock.Now = clock.Now.AddSeconds(1);
        var updated = await Profiles.UpdateAsync(profile.Id, Profile with { Model = "other", Temperature = 0.5, MaxOutputTokens = 100 }, TestContext.Current.CancellationToken);
        Assert.Equal(profile.CreatedAt, updated.CreatedAt);
        Assert.Equal(clock.Now, updated.UpdatedAt);
        Assert.Equal(updated, await Profiles.LoadAsync(profile.Id, TestContext.Current.CancellationToken));
        clock.Now = clock.Now.AddTicks(-1);
        await AssertError(ChatErrorCode.InvalidRequest, () => Profiles.UpdateAsync(profile.Id, Profile, TestContext.Current.CancellationToken));
        Assert.Equal(updated, await Profiles.LoadAsync(profile.Id, TestContext.Current.CancellationToken));
        clock.Now = clock.Now.AddTicks(1);
        var settings = new PreferencesView(ThemePreference.Dark, profile.Id, "override-model");
        await Preferences.SaveAsync(settings, TestContext.Current.CancellationToken);
        Assert.Equal(settings, await Preferences.LoadAsync(TestContext.Current.CancellationToken));
        await Profiles.DeleteAsync(profile.Id, TestContext.Current.CancellationToken);
        Assert.Equal(new PreferencesView(ThemePreference.Dark, null, null), await Preferences.LoadAsync(TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.NotFound, () => Profiles.LoadAsync(profile.Id, TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.NotFound, () => Preferences.SaveAsync(settings, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Foreign_profiles_and_preferences_cannot_be_read_modified_deleted_or_selected(bool faultyAdapter)
    {
        var profile = await Profiles.CreateAsync(Profile, TestContext.Current.CancellationToken);
        await Preferences.SaveAsync(new PreferencesView(ThemePreference.Dark, profile.Id, null), TestContext.Current.CancellationToken);
        identity.User = new UserId("bob");
        store.ReturnForeignData = faultyAdapter;
        await AssertError(ChatErrorCode.NotFound, () => Profiles.LoadAsync(profile.Id, TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.NotFound, () => Profiles.UpdateAsync(profile.Id, Profile, TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.NotFound, () => Profiles.DeleteAsync(profile.Id, TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.NotFound, () => Preferences.SaveAsync(new PreferencesView(ThemePreference.Light, profile.Id, null), TestContext.Current.CancellationToken));
        if (faultyAdapter)
        {
            await AssertError(ChatErrorCode.NotFound, () => Profiles.ListAsync(new PageRequest(), TestContext.Current.CancellationToken));
            await AssertError(ChatErrorCode.NotFound, () => Preferences.LoadAsync(TestContext.Current.CancellationToken));
        }
        else
        {
            Assert.Empty((await Profiles.ListAsync(new PageRequest(), TestContext.Current.CancellationToken)).Items);
            Assert.Equal(ThemePreference.System, (await Preferences.LoadAsync(TestContext.Current.CancellationToken)).Theme);
        }
    }

    [Fact]
    public async Task Anonymous_settings_access_is_rejected_and_identity_is_rechecked_each_call()
    {
        var profile = await Profiles.CreateAsync(Profile, TestContext.Current.CancellationToken);
        identity.User = null;
        await AssertError(ChatErrorCode.Unauthenticated, () => Profiles.CreateAsync(Profile, TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.Unauthenticated, () => Profiles.LoadAsync(profile.Id, TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.Unauthenticated, () => Profiles.UpdateAsync(profile.Id, Profile, TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.Unauthenticated, () => Profiles.DeleteAsync(profile.Id, TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.Unauthenticated, () => Profiles.ListAsync(new PageRequest(), TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.Unauthenticated, () => Preferences.LoadAsync(TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.Unauthenticated, () => Preferences.SaveAsync(new PreferencesView(ThemePreference.Dark, null, null), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Profile_paging_and_preferences_are_user_scoped()
    {
        var first = await Profiles.CreateAsync(Profile, TestContext.Current.CancellationToken);
        clock.Now = clock.Now.AddSeconds(1);
        var second = await Profiles.CreateAsync(Profile with { Name = "Second" }, TestContext.Current.CancellationToken);
        await Preferences.SaveAsync(new PreferencesView(ThemePreference.Dark, null, null), TestContext.Current.CancellationToken);
        identity.User = new UserId("bob");
        await Profiles.CreateAsync(Profile, TestContext.Current.CancellationToken);
        await Preferences.SaveAsync(new PreferencesView(ThemePreference.Light, null, null), TestContext.Current.CancellationToken);
        identity.User = new UserId("alice");
        var page = await Profiles.ListAsync(new PageRequest(0, 1), TestContext.Current.CancellationToken);
        Assert.Equal(first.Id, Assert.Single(page.Items).Id);
        Assert.True(page.HasMore);
        var last = await Profiles.ListAsync(new PageRequest(1, 1), TestContext.Current.CancellationToken);
        Assert.Equal(second.Id, Assert.Single(last.Items).Id);
        Assert.False(last.HasMore);
        Assert.Equal(ThemePreference.Dark, (await Preferences.LoadAsync(TestContext.Current.CancellationToken)).Theme);
    }

    [Theory]
    [InlineData("ftp://localhost/models")]
    [InlineData("https://name:secret@localhost/api")]
    [InlineData("https://localhost/api?key=secret")]
    [InlineData("https://localhost/api#secret")]
    public async Task Unsafe_endpoint_forms_are_rejected_without_echoing_input(string endpoint)
    {
        var exception = await Assert.ThrowsAsync<ChatOperationException>(() => Profiles.CreateAsync(Profile with { Endpoint = new Uri(endpoint) }, TestContext.Current.CancellationToken));
        Assert.Equal(ChatErrorCode.InvalidRequest, exception.Code);
        Assert.DoesNotContain("secret", exception.ToString(), StringComparison.Ordinal);
        Assert.Empty((await Profiles.ListAsync(new PageRequest(), TestContext.Current.CancellationToken)).Items);
    }

    [Fact]
    public async Task Invalid_settings_fail_before_writes_and_cancellation_is_forwarded()
    {
        await AssertError(ChatErrorCode.InvalidRequest, () => Profiles.CreateAsync(Profile with { Model = " " }, TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.InvalidRequest, () => Profiles.CreateAsync(Profile with { Temperature = double.NaN }, TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.InvalidRequest, () => Profiles.CreateAsync(Profile with { MaxOutputTokens = 0 }, TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.InvalidRequest, () => Preferences.SaveAsync(new PreferencesView((ThemePreference)42, null, null), TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.InvalidRequest, () => Preferences.SaveAsync(new PreferencesView(ThemePreference.System, null, "model"), TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource();
        await Preferences.SaveAsync(new PreferencesView(ThemePreference.Light, null, null), cancellation.Token);
        Assert.Equal(cancellation.Token, store.LastToken);
        await Profiles.CreateAsync(Profile, cancellation.Token);
        Assert.Equal(cancellation.Token, store.LastToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Profiles.ListAsync(new PageRequest(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Preferences.LoadAsync(cancellation.Token));
    }
}
