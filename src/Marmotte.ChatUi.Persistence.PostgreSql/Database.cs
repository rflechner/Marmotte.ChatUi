using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;
using Npgsql;
using NpgsqlTypes;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Owns the connection pool and contains provider error translation and bounded SQL execution.</summary>
/// <remarks>Singleton; connections, commands, and transactions belong to each operation and are never shared by circuits.</remarks>
internal sealed class Database : IAsyncDisposable
{
    private readonly NpgsqlDataSource source;

    /// <summary>Builds a pool without parameter logging or server error details; malformed configuration fails at composition.</summary>
    internal Database(string connectionString)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = 15,
            CommandTimeout = 30,
            IncludeErrorDetail = false,
            LogParameters = false,
            Options = "-c statement_timeout=30000 -c lock_timeout=10000"
        };
        source = NpgsqlDataSource.Create(settings.ConnectionString);
    }

    /// <summary>Leases a connection; the caller must dispose it. Cancellation reaches pool waits and connection establishment.</summary>
    internal ValueTask<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken) => source.OpenConnectionAsync(cancellationToken);

    /// <summary>Creates a positional-parameter command. Nulls have an explicit type; SQL strings must be adapter-owned constants.</summary>
    internal static NpgsqlCommand Command(NpgsqlConnection connection, string sql, params object?[] values)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
        {
            command.Parameters.Add(value is SqlNull missing
                ? new NpgsqlParameter { NpgsqlDbType = missing.Type, Value = DBNull.Value }
                : new NpgsqlParameter { Value = value ?? throw new ArgumentException("Use a typed SQL null.", nameof(values)) });
        }

        return command;
    }

    /// <summary>Returns a typed database null when the supplied nullable scalar has no value.</summary>
    internal static object Nullable<T>(T? value, NpgsqlDbType type) where T : struct => value.HasValue ? value.Value : new SqlNull(type);

    /// <summary>Returns a typed text null when an optional string is absent.</summary>
    internal static object Text(string? value) => value is null ? new SqlNull(NpgsqlDbType.Text) : value;

    /// <summary>Rejects foreign snapshots before opening a connection, without revealing their existence.</summary>
    internal static void RequireOwner(UserId expected, UserId actual)
    {
        if (expected != actual)
        {
            throw new ChatOperationException(ChatErrorCode.NotFound);
        }
    }

    /// <summary>Executes an operation without retries and suppresses SQL diagnostics that could contain private values.</summary>
    /// <remarks>Cancellation and safe application failures propagate; known constraint/concurrency failures retain safe categories.</remarks>
    internal static async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState is "23505" or "40001" or "40P01")
        {
            throw new ChatOperationException(ChatErrorCode.Conflict);
        }
        catch (PostgresException exception) when (exception.SqlState == "23503")
        {
            throw new ChatOperationException(ChatErrorCode.NotFound);
        }
        catch (PostgresException exception) when (exception.SqlState is "23514" or "22001")
        {
            throw new ChatOperationException(ChatErrorCode.InvalidRequest);
        }
        catch (NpgsqlException)
        {
            throw new ChatOperationException(ChatErrorCode.StorageUnavailable);
        }
    }

    /// <summary>Closes the pool when the owning service provider shuts down.</summary>
    public ValueTask DisposeAsync() => source.DisposeAsync();

    /// <summary>Carries only the PostgreSQL type of an absent parameter; never contains application data.</summary>
    private sealed record SqlNull(NpgsqlDbType Type);
}
