using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Marmotte.ChatUi.Application;
using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;
using Marmotte.ChatUi.Persistence.PostgreSql;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Marmotte.ChatUi.Web.Tests;

/// <summary>Verifies real PostgreSQL isolation, atomicity, upgrades, protected secrets, and authenticated host restart behavior.</summary>
public sealed class PostgreSqlTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    public static bool Enabled => PostgreSqlFixture.Enabled;

    [Fact(Skip = "Requires Docker and MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1.", SkipUnless = nameof(Enabled))]
    public async Task Authenticated_history_settings_and_credentials_survive_host_and_database_restart()
    {
        var subject = Guid.NewGuid().ToString();
        Guid conversationId;
        Guid profileId;
        Guid responseId;
        var submission = new RecordMessageRequest(Guid.NewGuid(), "Persisted question");
        await using (var host = new OidcTestHost(fixture.ConnectionString, fixture.KeyDirectory))
        {
            using var browser = host.Browser();
            var (principal, _) = await host.LoginAsync(browser, subject);
            await using var scope = host.Services.CreateAsyncScope();
            SetIdentity(scope, principal);
            var conversations = scope.ServiceProvider.GetRequiredService<ConversationService>();
            conversationId = (await conversations.CreateAsync("Private history", Token)).Summary.Id;
            var sent = await conversations.RecordMessageAsync(conversationId, submission, Token);
            responseId = sent.Messages[1].Id;
            await conversations.RecordResponseAsync(conversationId, responseId, new ResponseDelta("Partial response", MessageState.Streaming), Token);
            var profile = await scope.ServiceProvider.GetRequiredService<IConnectionProfileService>()
                .CreateAsync(new SaveProfileRequest("Local", new Uri("http://localhost/custom/api"), "model", 0.5, 100), Token);
            profileId = profile.Id;
            await scope.ServiceProvider.GetRequiredService<IUserPreferencesService>()
                .SaveAsync(new PreferencesView(ThemePreference.Dark, profileId, "selected-model"), Token);
            await scope.ServiceProvider.GetRequiredService<ModelCredentialService>().SaveAsync(profileId, "private-model-key", Token);
        }

        await fixture.RestartAsync(Token);
        await using var restarted = new OidcTestHost(fixture.ConnectionString, fixture.KeyDirectory);
        using var alice = restarted.Browser();
        using var bob = restarted.Browser();
        var (alicePrincipal, _) = await restarted.LoginAsync(alice, subject);
        var (bobPrincipal, _) = await restarted.LoginAsync(bob, Guid.NewGuid().ToString());
        await using var restartedScope = restarted.Services.CreateAsyncScope();
        SetIdentity(restartedScope, alicePrincipal);
        var history = restartedScope.ServiceProvider.GetRequiredService<ConversationService>();
        var loaded = await history.LoadAsync(conversationId, Token);
        Assert.Equal("Partial response", loaded.Messages[1].Content);
        var repeated = await history.RecordMessageAsync(conversationId, submission, Token);
        Assert.Equal(loaded.Summary.Revision, repeated.Summary.Revision);
        Assert.Equal(2, repeated.Messages.Count);
        var cancelled = await history.CancelResponseAsync(conversationId, responseId, Token);
        Assert.Equal("Partial response", cancelled.Messages[1].Content);
        Assert.Equal(GenerationStatus.Cancelled, cancelled.Messages[1].Status);
        var profiles = restartedScope.ServiceProvider.GetRequiredService<IConnectionProfileService>();
        var preferences = restartedScope.ServiceProvider.GetRequiredService<IUserPreferencesService>();
        var credentials = restartedScope.ServiceProvider.GetRequiredService<ModelCredentialService>();
        Assert.Equal(0.5, (await profiles.LoadAsync(profileId, Token)).Temperature);
        Assert.Equal(new PreferencesView(ThemePreference.Dark, profileId, "selected-model"), await preferences.LoadAsync(Token));
        Assert.Equal("private-model-key", (await credentials.ResolveAsync(profileId, Token))!.Value);

        SetIdentity(restartedScope, bobPrincipal);
        await ErrorAsync(ChatErrorCode.NotFound, () => history.LoadAsync(conversationId, Token));
        await ErrorAsync(ChatErrorCode.NotFound, () => history.DeleteAsync(conversationId, Token));
        await ErrorAsync(ChatErrorCode.NotFound, () => history.RecordMessageAsync(conversationId, submission, Token));
        await ErrorAsync(ChatErrorCode.NotFound, () => profiles.LoadAsync(profileId, Token));
        await ErrorAsync(ChatErrorCode.NotFound, () => profiles.UpdateAsync(profileId, new SaveProfileRequest("Injected", new Uri("http://localhost/api"), "other"), Token));
        await ErrorAsync(ChatErrorCode.NotFound, () => profiles.DeleteAsync(profileId, Token));
        await ErrorAsync(ChatErrorCode.NotFound, () => preferences.SaveAsync(new PreferencesView(ThemePreference.Light, profileId, null), Token));
        await ErrorAsync(ChatErrorCode.NotFound, () => credentials.ResolveAsync(profileId, Token));
        await ErrorAsync(ChatErrorCode.NotFound, () => credentials.SaveAsync(profileId, "replacement", Token));
        await ErrorAsync(ChatErrorCode.NotFound, () => credentials.DeleteAsync(profileId, Token));
        Assert.Empty((await history.ListAsync(new PageRequest(), Token)).Items);
        Assert.Empty((await profiles.ListAsync(new PageRequest(), Token)).Items);
        Assert.Equal(ThemePreference.System, (await preferences.LoadAsync(Token)).Theme);
    }

    [Fact(Skip = "Requires Docker and MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1.", SkipUnless = nameof(Enabled))]
    public async Task Concurrent_revisions_have_one_winner_and_stale_deletes_cannot_remove_it()
    {
        await using var provider = fixture.Services();
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
        var owner = Owner();
        var original = Conversation.Create(Guid.NewGuid(), owner, "Concurrent", Now);
        await repository.SaveAsync(owner, original, 0, Token);
        var left = original.RecordMessage(Guid.NewGuid(), "Left", Now);
        var right = original.RecordMessage(Guid.NewGuid(), "Right", Now);
        var attempts = await Task.WhenAll(
            Record.ExceptionAsync(() => repository.SaveAsync(owner, left, 1, Token)).AsTask(),
            Record.ExceptionAsync(() => repository.SaveAsync(owner, right, 1, Token)).AsTask());
        Assert.Single(attempts, error => error is null);
        Assert.Equal(ChatErrorCode.Conflict, Assert.IsType<ChatOperationException>(Assert.Single(attempts, error => error is not null)).Code);
        var stored = (await repository.FindAsync(owner, original.Id, Token))!;
        Assert.Equal(2, stored.Revision);
        Assert.Equal(new long[] { 1, 2 }, stored.Messages.Select(message => message.Sequence));
        await ErrorAsync(ChatErrorCode.Conflict, () => repository.DeleteAsync(owner, original.Id, 1, Token));
        await ErrorAsync(ChatErrorCode.Conflict, () => repository.SaveAsync(owner, original, 0, Token));
        Assert.Null(await repository.FindAsync(Owner(), original.Id, Token));
        await ErrorAsync(ChatErrorCode.NotFound, () => repository.SaveAsync(Owner(), left, 1, Token));
        await ErrorAsync(ChatErrorCode.Conflict, () => repository.DeleteAsync(Owner(), original.Id, 2, Token));
        await repository.DeleteAsync(owner, original.Id, 2, Token);
        Assert.Null(await repository.FindAsync(owner, original.Id, Token));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM chat.messages WHERE conversation_id = $1", original.Id));
    }

    [Fact(Skip = "Requires Docker and MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1.", SkipUnless = nameof(Enabled))]
    public async Task Navigation_is_owner_scoped_and_deterministic_for_equal_timestamps()
    {
        await using var provider = fixture.Services();
        await using var scope = provider.CreateAsyncScope();
        var conversations = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
        var profiles = scope.ServiceProvider.GetRequiredService<IConnectionProfileRepository>();
        var owner = Owner();
        Guid[] ids = [Guid.Parse("10000000-0000-0000-0000-000000000001"), Guid.Parse("20000000-0000-0000-0000-000000000001")];
        foreach (var id in ids.Reverse())
        {
            await conversations.SaveAsync(owner, Conversation.Create(id, owner, "Page", Now), 0, Token);
            await profiles.SaveAsync(owner, Profile(owner, id), true, Token);
        }

        var foreign = Owner();
        await conversations.SaveAsync(foreign, Conversation.Create(Guid.NewGuid(), foreign, "Foreign", Now), 0, Token);
        await profiles.SaveAsync(foreign, Profile(foreign), true, Token);
        Assert.Equal(ids[0], Assert.Single((await conversations.ListAsync(owner, new PageRequest(0, 1), Token)).Items).Id);
        Assert.True((await conversations.ListAsync(owner, new PageRequest(0, 1), Token)).HasMore);
        Assert.Equal(ids[1], Assert.Single((await conversations.ListAsync(owner, new PageRequest(1, 1), Token)).Items).Id);
        Assert.False((await conversations.ListAsync(owner, new PageRequest(1, 1), Token)).HasMore);
        Assert.Empty((await conversations.ListAsync(owner, new PageRequest(int.MaxValue, 1), Token)).Items);
        Assert.Equal(ids[0], Assert.Single((await profiles.ListAsync(owner, new PageRequest(0, 1), Token)).Items).Id);
        Assert.True((await profiles.ListAsync(owner, new PageRequest(0, 1), Token)).HasMore);
        Assert.Equal(ids[1], Assert.Single((await profiles.ListAsync(owner, new PageRequest(1, 1), Token)).Items).Id);
        Assert.False((await profiles.ListAsync(owner, new PageRequest(1, 1), Token)).HasMore);
    }

    [Fact(Skip = "Requires Docker and MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1.", SkipUnless = nameof(Enabled))]
    public async Task Attachment_references_require_matching_owned_ready_metadata_and_rollback_atomically()
    {
        await using var provider = fixture.Services();
        await using var scope = provider.CreateAsyncScope();
        var conversations = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
        var attachments = scope.ServiceProvider.GetRequiredService<IAttachmentMetadataRepository>();
        var owner = Owner();
        var original = Conversation.Create(Guid.NewGuid(), owner, "Attachments", Now);
        await conversations.SaveAsync(owner, original, 0, Token);
        var file = new Attachment(Guid.NewGuid(), owner, "image.png", "image/png", 42, Now, AttachmentState.Ready, 10, 20);
        var message = original.RecordMessage(Guid.NewGuid(), "Image", Now, [file]);
        await ErrorAsync(ChatErrorCode.NotFound, () => conversations.SaveAsync(owner, message, 1, Token));
        Assert.Empty((await conversations.FindAsync(owner, original.Id, Token))!.Messages);
        Assert.Equal(1, (await conversations.FindAsync(owner, original.Id, Token))!.Revision);
        var foreign = Owner();
        await attachments.AddAsync(foreign, new Attachment(file.Id, foreign, file.FileName, file.MediaType, file.SizeBytes, Now, AttachmentState.Ready, 10, 20), Token);
        await ErrorAsync(ChatErrorCode.NotFound, () => conversations.SaveAsync(owner, message, 1, Token));
        Assert.Null(await attachments.FindAsync(owner, file.Id, Token));
        await ErrorAsync(ChatErrorCode.NotFound, () => attachments.AddAsync(foreign, file, Token));

        file = new Attachment(Guid.NewGuid(), owner, "image.png", "image/png", 42, Now, AttachmentState.Ready, 10, 20);
        await attachments.AddAsync(owner, file, Token);
        var altered = new Attachment(file.Id, owner, "forged.png", "image/png", 42, Now, AttachmentState.Ready, 10, 20);
        await ErrorAsync(ChatErrorCode.NotFound, () => conversations.SaveAsync(owner, original.RecordMessage(Guid.NewGuid(), "Image", Now, [altered]), 1, Token));
        message = original.RecordMessage(Guid.NewGuid(), "Image", Now, [file]);
        await conversations.SaveAsync(owner, message, 1, Token);
        Assert.Equal(file.Id, Assert.Single((await conversations.FindAsync(owner, original.Id, Token))!.Messages[0].Attachments).Id);
        await conversations.DeleteAsync(owner, original.Id, 2, Token);
        Assert.NotNull(await attachments.FindAsync(owner, file.Id, Token));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM chat.message_attachments WHERE conversation_id = $1", original.Id));
    }

    [Fact(Skip = "Requires Docker and MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1.", SkipUnless = nameof(Enabled))]
    public async Task Profile_deletion_clears_selection_and_credentials_even_when_writes_race()
    {
        await using var provider = fixture.Services();
        await using var scope = provider.CreateAsyncScope();
        var profiles = scope.ServiceProvider.GetRequiredService<IConnectionProfileRepository>();
        var preferences = scope.ServiceProvider.GetRequiredService<IUserPreferencesRepository>();
        var credentials = scope.ServiceProvider.GetRequiredService<IModelCredentialStore>();
        var owner = Owner();
        var profile = Profile(owner);
        await profiles.SaveAsync(owner, profile, true, Token);
        await ErrorAsync(ChatErrorCode.Conflict, () => profiles.SaveAsync(owner, profile, true, Token));
        await preferences.SaveAsync(owner, new UserPreferences(owner, ThemeMode.Dark, profile.Id, "override"), Token);
        await credentials.SaveAsync(owner, profile.Id, new ModelCredential("a-secret"), Token);
        var attempts = await Task.WhenAll(
            Record.ExceptionAsync(() => preferences.SaveAsync(owner, new UserPreferences(owner, ThemeMode.Dark, profile.Id, "new-model"), Token)).AsTask(),
            Record.ExceptionAsync(() => credentials.SaveAsync(owner, profile.Id, new ModelCredential("new-secret"), Token)).AsTask(),
            Record.ExceptionAsync(() => profiles.DeleteAsync(owner, profile.Id, Token)).AsTask());
        foreach (var error in attempts.Where(error => error is not null))
        {
            Assert.Equal(ChatErrorCode.NotFound, Assert.IsType<ChatOperationException>(error).Code);
        }

        var saved = (await preferences.FindAsync(owner, Token))!;
        Assert.Equal(ThemeMode.Dark, saved.Theme);
        Assert.Null(saved.SelectedProfileId);
        Assert.Null(saved.SelectedModel);
        Assert.Null(await profiles.FindAsync(owner, profile.Id, Token));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM chat.profile_credentials WHERE profile_id = $1", profile.Id));
        await ErrorAsync(ChatErrorCode.NotFound, () => profiles.SaveAsync(owner, profile, false, Token));
        await ErrorAsync(ChatErrorCode.NotFound, () => preferences.SaveAsync(owner, new UserPreferences(owner, selectedProfileId: profile.Id), Token));
        await ErrorAsync(ChatErrorCode.NotFound, () => preferences.SaveAsync(Owner(), saved, Token));
    }

    [Fact(Skip = "Requires Docker and MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1.", SkipUnless = nameof(Enabled))]
    public async Task Credentials_are_encrypted_bound_to_owner_and_profile_and_fail_closed_on_tampering()
    {
        await using var provider = fixture.Services();
        await using var scope = provider.CreateAsyncScope();
        var profiles = scope.ServiceProvider.GetRequiredService<IConnectionProfileRepository>();
        var credentials = scope.ServiceProvider.GetRequiredService<IModelCredentialStore>();
        var owner = Owner();
        var first = Profile(owner);
        var second = Profile(owner);
        var foreign = Profile(Owner());
        foreach (var profile in new[] { first, second, foreign })
        {
            await profiles.SaveAsync(profile.OwnerId, profile, true, Token);
        }

        Assert.Null(await credentials.FindAsync(owner, first.Id, Token));
        var secret = new ModelCredential("never-persist-this-plaintext");
        Assert.DoesNotContain(secret.Value, secret.ToString(), StringComparison.Ordinal);
        await credentials.SaveAsync(owner, first.Id, secret, Token);
        var missingKeys = new ServiceCollection();
        missingKeys.AddDataProtection().UseEphemeralDataProtectionProvider();
        missingKeys.AddPostgreSqlPersistence(fixture.ConnectionString);
        await using (var otherProvider = missingKeys.BuildServiceProvider())
        await using (var otherScope = otherProvider.CreateAsyncScope())
        {
            await ErrorAsync(ChatErrorCode.StorageUnavailable,
                () => otherScope.ServiceProvider.GetRequiredService<IModelCredentialStore>().FindAsync(owner, first.Id, Token));
        }

        var encrypted = Assert.IsType<byte[]>(await ScalarAsync("SELECT protected_value FROM chat.profile_credentials WHERE profile_id = $1", first.Id));
        Assert.DoesNotContain(secret.Value, Encoding.UTF8.GetString(encrypted), StringComparison.Ordinal);
        foreach (var profile in new[] { second, foreign })
        {
            await credentials.SaveAsync(profile.OwnerId, profile.Id, new ModelCredential("placeholder"), Token);
            await ScalarAsync("UPDATE chat.profile_credentials SET protected_value = $2 WHERE profile_id = $1", profile.Id, encrypted);
            await ErrorAsync(ChatErrorCode.StorageUnavailable, () => credentials.FindAsync(profile.OwnerId, profile.Id, Token));
        }

        encrypted[^1] ^= 1;
        await ScalarAsync("UPDATE chat.profile_credentials SET protected_value = $2 WHERE profile_id = $1", first.Id, encrypted);
        await ErrorAsync(ChatErrorCode.StorageUnavailable, () => credentials.FindAsync(owner, first.Id, Token));
        await credentials.DeleteAsync(owner, first.Id, Token);
        await credentials.DeleteAsync(owner, first.Id, Token);
        Assert.Null(await credentials.FindAsync(owner, first.Id, Token));
    }

    [Fact(Skip = "Requires Docker and MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1.", SkipUnless = nameof(Enabled))]
    public async Task Cancellation_while_waiting_for_a_write_lock_leaves_history_unchanged()
    {
        await using var provider = fixture.Services();
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
        var owner = Owner();
        var original = Conversation.Create(Guid.NewGuid(), owner, "Cancellation", Now);
        await repository.SaveAsync(owner, original, 0, Token);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Token);
        await using var transaction = await connection.BeginTransactionAsync(Token);
        await using var command = new NpgsqlCommand("SELECT id FROM chat.conversations WHERE id = $1 FOR UPDATE", connection);
        command.Parameters.AddWithValue(original.Id);
        await command.ExecuteScalarAsync(Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.SaveAsync(owner, original.RecordMessage(Guid.NewGuid(), "Cancelled", Now), 1, cancellation.Token));
        await transaction.RollbackAsync(Token);
        Assert.Equal(1, (await repository.FindAsync(owner, original.Id, Token))!.Revision);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.ListAsync(owner, new PageRequest(), cancellation.Token));
    }

    [Fact(Skip = "Requires Docker and MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1.", SkipUnless = nameof(Enabled))]
    public async Task Migrations_upgrade_existing_data_serialize_concurrent_starts_and_reject_drift()
    {
        var databaseName = $"upgrade_{Guid.NewGuid():N}";
        await ScalarAsync($"CREATE DATABASE {databaseName}");
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = databaseName }.ConnectionString;
        await using var provider = fixture.Services(connectionString);
        await using (var context = await provider.GetRequiredService<IDbContextFactory<ChatDbContext>>().CreateDbContextAsync(Token))
        {
            var first = context.Database.GetMigrations().First();
            await context.GetService<IMigrator>().MigrateAsync(first, Token);
            await context.Database.ExecuteSqlRawAsync("INSERT INTO chat.preferences VALUES ('upgrade-user', 2, NULL, NULL)", Token);
        }

        var migrations = provider.GetRequiredService<PostgreSqlMigrations>();
        await Task.WhenAll(migrations.ApplyAsync(Token), migrations.ApplyAsync(Token));
        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(ThemeMode.Dark, (await scope.ServiceProvider.GetRequiredService<IUserPreferencesRepository>().FindAsync(new UserId("upgrade-user"), Token))!.Theme);
        await using var upgraded = new NpgsqlConnection(connectionString);
        await upgraded.OpenAsync(Token);
        await using var count = new NpgsqlCommand("SELECT count(*) FROM chat.\"__EFMigrationsHistory\"", upgraded);
        Assert.Equal(2L, await count.ExecuteScalarAsync(Token));
        await using var drift = new NpgsqlCommand("INSERT INTO chat.\"__EFMigrationsHistory\" VALUES ('99999999999999_UnknownMigration', '10.0.12')", upgraded);
        await drift.ExecuteNonQueryAsync(Token);
        await ErrorAsync(ChatErrorCode.StorageUnavailable, () => migrations.ApplyAsync(Token));
    }

    [Theory(Skip = "Requires Docker and MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1.", SkipUnless = nameof(Enabled))]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Legacy_SQL_databases_adopt_EF_history_without_losing_owned_data(int versionCount)
    {
        var connectionString = await CreateLegacyDatabaseAsync(versionCount);
        await using var provider = fixture.Services(connectionString);
        await using var scope = provider.CreateAsyncScope();
        var owner = Owner();
        var conversations = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
        var profiles = scope.ServiceProvider.GetRequiredService<IConnectionProfileRepository>();
        var preferences = scope.ServiceProvider.GetRequiredService<IUserPreferencesRepository>();
        var credentials = scope.ServiceProvider.GetRequiredService<IModelCredentialStore>();
        var conversation = Conversation.Create(Guid.NewGuid(), owner, "Legacy history", Now);
        await conversations.SaveAsync(owner, conversation, 0, Token);
        var submitted = conversation.RecordMessage(Guid.NewGuid(), "Preserve this message", Now);
        await conversations.SaveAsync(owner, submitted, 1, Token);
        var profile = Profile(owner);
        await profiles.SaveAsync(owner, profile, true, Token);
        await preferences.SaveAsync(owner, new UserPreferences(owner, ThemeMode.Dark, profile.Id, "selected"), Token);
        if (versionCount == 2)
        {
            await credentials.SaveAsync(owner, profile.Id, new ModelCredential("legacy-secret"), Token);
        }

        var migrations = provider.GetRequiredService<PostgreSqlMigrations>();
        await Task.WhenAll(migrations.ApplyAsync(Token), migrations.ApplyAsync(Token));
        Assert.Equal("Preserve this message", (await conversations.FindAsync(owner, conversation.Id, Token))!.Messages[0].Content);
        Assert.Equal(profile.Model, (await profiles.FindAsync(owner, profile.Id, Token))!.Model);
        Assert.Equal(profile.Id, (await preferences.FindAsync(owner, Token))!.SelectedProfileId);
        if (versionCount == 2)
        {
            Assert.Equal("legacy-secret", (await credentials.FindAsync(owner, profile.Id, Token))!.Value);
        }
        else
        {
            Assert.Null(await credentials.FindAsync(owner, profile.Id, Token));
            await credentials.SaveAsync(owner, profile.Id, new ModelCredential("new-secret"), Token);
        }

        await using var context = await provider.GetRequiredService<IDbContextFactory<ChatDbContext>>().CreateDbContextAsync(Token);
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync(Token)).Count());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Theory(Skip = "Requires Docker and MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1.", SkipUnless = nameof(Enabled))]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_baseline_rejects_changed_ledgers_or_missing_columns_without_recording_EF_history(bool changeSchema)
    {
        var connectionString = await CreateLegacyDatabaseAsync(2);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using var alter = new NpgsqlCommand(changeSchema
            ? "ALTER TABLE chat.profiles DROP COLUMN model"
            : "UPDATE chat.schema_migrations SET checksum = 'changed'", connection);
        await alter.ExecuteNonQueryAsync(Token);
        await using var provider = fixture.Services(connectionString);
        await ErrorAsync(ChatErrorCode.StorageUnavailable, () => provider.GetRequiredService<PostgreSqlMigrations>().ApplyAsync(Token));
        await using var history = new NpgsqlCommand("SELECT to_regclass('chat.\"__EFMigrationsHistory\"') IS NULL", connection);
        Assert.Equal(true, await history.ExecuteScalarAsync(Token));
    }

    /// <summary>Reproduces the previously shipped SQL schema/ledger only in an isolated test database.</summary>
    private async Task<string> CreateLegacyDatabaseAsync(int versionCount)
    {
        var name = $"legacy_{Guid.NewGuid():N}";
        await ScalarAsync($"CREATE DATABASE {name}");
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = name }.ConnectionString;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using var ledger = new NpgsqlCommand("""
            CREATE SCHEMA chat;
            CREATE TABLE chat.schema_migrations (name text PRIMARY KEY, checksum text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now())
            """, connection);
        await ledger.ExecuteNonQueryAsync(Token);
        var assembly = typeof(PostgreSqlTests).Assembly;
        var resources = assembly.GetManifestResourceNames().Where(resource => resource.Contains(".LegacySql.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).Take(versionCount);
        foreach (var resource in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(Token);
            await using var migration = new NpgsqlCommand(sql, connection);
            await migration.ExecuteNonQueryAsync(Token);
            var originalName = "Marmotte.ChatUi.Persistence.PostgreSql.Migrations." + resource.Split(".LegacySql.")[1];
            await using var record = new NpgsqlCommand("INSERT INTO chat.schema_migrations (name, checksum) VALUES ($1, $2)", connection);
            record.Parameters.AddWithValue(originalName);
            record.Parameters.AddWithValue(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))));
            await record.ExecuteNonQueryAsync(Token);
        }

        return connectionString;
    }

    /// <summary>Installs a principal issued by the real OIDC handler into the host's circuit authentication state.</summary>
    private static void SetIdentity(AsyncServiceScope scope, ClaimsPrincipal principal) =>
        ((IHostEnvironmentAuthenticationStateProvider)scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>())
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(principal)));

    /// <summary>Creates a distinct trusted test identity so tests share a database without sharing records.</summary>
    private static UserId Owner() => new(Guid.NewGuid().ToString());

    /// <summary>Builds valid secret-free settings at a deterministic timestamp.</summary>
    private static ConnectionProfile Profile(UserId owner, Guid? id = null) => new(id ?? Guid.NewGuid(), owner, "Local",
        new Uri("http://localhost/custom/api"), "model", new GenerationOptions(), Now, Now);

    /// <summary>Asserts only the safe error category; provider and credential details must not escape through inner exceptions.</summary>
    private static async Task ErrorAsync(ChatErrorCode code, Func<Task> operation)
    {
        var error = await Assert.ThrowsAsync<ChatOperationException>(operation);
        Assert.Equal(code, error.Code);
        Assert.Null(error.InnerException);
    }

    /// <summary>Runs fixed parameterized test probes against only the disposable database.</summary>
    private async Task<object?> ScalarAsync(string sql, params object[] values)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
        {
            command.Parameters.AddWithValue(value);
        }

        return await command.ExecuteScalarAsync(Token);
    }
}
