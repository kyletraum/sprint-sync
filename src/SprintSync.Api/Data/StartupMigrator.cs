using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SprintSync.Api.Data;

/// <summary>
/// Applies EF Core migrations at startup under a SQL Server application lock
/// (T050 — the P1-4 hardening the previous session deferred).
///
/// ACA runs the API in Single revision mode, so a rolling update briefly has the
/// new revision starting while the old one is still serving. MaxReplicas = 1
/// rules out a race WITHIN a revision but not ACROSS revisions, leaving a window
/// in which two processes could apply DDL concurrently. A session-scoped
/// <c>sp_getapplock</c> closes it: the second process blocks until the first has
/// finished, then finds nothing left to apply.
///
/// The lock is taken on the very connection EF migrates over, and is session
/// scoped rather than transaction scoped so it spans every per-migration
/// transaction. SQL Server drops a session lock automatically when its
/// connection dies, so a crashed deploy cannot strand the schema behind a lock
/// nobody holds.
/// </summary>
public static class StartupMigrator
{
    /// <summary>
    /// Application-lock name. Application locks are scoped to the database they
    /// are taken in, so this one name serializes every revision migrating this
    /// database and nothing else.
    /// </summary>
    private const string LockResource = "SprintSync:schema-migration";

    /// <summary>
    /// Long enough to outwait another revision applying a real migration against
    /// a resuming (auto-paused) Azure SQL database; short enough that a genuinely
    /// stuck lock fails the start — which ACA retries — instead of hanging the
    /// container indefinitely.
    /// </summary>
    private const int LockTimeoutMilliseconds = 180_000;

    /// <summary>
    /// Acquires the migration lock, migrates, and releases. Safe to call from
    /// every replica and every revision concurrently.
    /// </summary>
    public static async Task MigrateAsync(
        AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        // The retrying execution strategy covers the transient faults that the
        // deliberate scale-to-zero + auto-pause topology makes routine rather
        // than exceptional (P1-2). Connection, lock and migration all sit inside
        // it so a retried attempt starts from a clean connection and re-acquires
        // the lock rather than resuming half-way through.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // Opened through EF so EF knows the connection is externally owned
            // and leaves it open for the whole migration — the lock and the DDL
            // must share one session or the lock guards nothing.
            await db.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                var connection = db.Database.GetDbConnection();
                await AcquireAsync(connection, logger, cancellationToken);
                try
                {
                    await db.Database.MigrateAsync(cancellationToken);
                }
                finally
                {
                    await ReleaseAsync(connection, logger);
                }
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        });
    }

    private static async Task AcquireAsync(
        DbConnection connection, ILogger logger, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "sp_getapplock";

        // Must outlast @LockTimeout, otherwise the command would time out before
        // the procedure gets its chance to report the wait as -1.
        command.CommandTimeout = (LockTimeoutMilliseconds / 1000) + 30;

        AddParameter(command, "@Resource", LockResource);
        AddParameter(command, "@LockMode", "Exclusive");
        AddParameter(command, "@LockOwner", "Session");
        AddParameter(command, "@LockTimeout", LockTimeoutMilliseconds);

        var result = command.CreateParameter();
        result.ParameterName = "@Result";
        result.DbType = DbType.Int32;
        result.Direction = ParameterDirection.ReturnValue;
        command.Parameters.Add(result);

        await command.ExecuteNonQueryAsync(cancellationToken);

        // sp_getapplock: 0 granted immediately, 1 granted after waiting; negative
        // is timeout (-1), cancellation (-2), deadlock victim (-3) or a call
        // error (-999).
        var code = result.Value is int value ? value : -999;
        if (code < 0)
        {
            throw new InvalidOperationException(
                $"Could not acquire the '{LockResource}' application lock: sp_getapplock returned {code}. "
                + "Another revision is most likely still applying migrations. Failing startup so the "
                + "platform restarts this replica and tries again.");
        }

        if (code == 1)
        {
            logger.LogInformation(
                "Waited for another instance to finish migrating before acquiring the schema lock.");
        }
    }

    private static async Task ReleaseAsync(DbConnection connection, ILogger logger)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "sp_releaseapplock";
            AddParameter(command, "@Resource", LockResource);
            AddParameter(command, "@LockOwner", "Session");

            // Deliberately NOT the caller's token: releasing is cleanup, and a
            // cancelled or failed migration is exactly when it still needs to run.
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            // Best effort by design. The lock is session scoped, so closing the
            // connection drops it either way; never let cleanup mask a migration
            // failure that is already on its way up.
            logger.LogWarning(
                ex, "Releasing the schema migration lock failed; it drops with the connection.");
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
