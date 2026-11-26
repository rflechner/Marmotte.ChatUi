using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Configures bounded EF operations and translates storage errors without leaking provider diagnostics.</summary>
internal static class Database
{
    /// <summary>Applies identical runtime/design-time provider settings without sensitive logging or automatic retries.</summary>
    internal static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = 15,
            CommandTimeout = 30,
            IncludeErrorDetail = false,
            LogParameters = false,
            Options = "-c statement_timeout=30000 -c lock_timeout=10000"
        };
        options.UseNpgsql(settings.ConnectionString, provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", "chat"))
            .EnableSensitiveDataLogging(false)
            .EnableDetailedErrors(false)
            .UseLoggerFactory(NullLoggerFactory.Instance);
    }

    /// <summary>Rejects foreign snapshots before database access, without revealing their existence.</summary>
    internal static void RequireOwner(UserId expected, UserId actual)
    {
        if (expected != actual)
        {
            throw new ChatOperationException(ChatErrorCode.NotFound);
        }
    }

    /// <summary>Executes once, propagates cancellation, and translates direct or EF-wrapped database failures.</summary>
    internal static async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ChatOperationException(ChatErrorCode.Conflict);
        }
        catch (DbUpdateException exception)
        {
            throw Translate(exception.InnerException);
        }
        catch (NpgsqlException exception)
        {
            throw Translate(exception);
        }
    }

    /// <summary>Maps known constraint/concurrency failures without retaining private provider diagnostics.</summary>
    private static ChatOperationException Translate(Exception? exception) => new(exception is PostgresException postgres
        ? postgres.SqlState switch
        {
            "23505" or "40001" or "40P01" => ChatErrorCode.Conflict,
            "23503" => ChatErrorCode.NotFound,
            "23514" or "22001" => ChatErrorCode.InvalidRequest,
            _ => ChatErrorCode.StorageUnavailable
        }
        : ChatErrorCode.StorageUnavailable);
}
