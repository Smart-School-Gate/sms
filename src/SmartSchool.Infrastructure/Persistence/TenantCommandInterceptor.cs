using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SmartSchool.Infrastructure.Persistence;

public static class DbRoundTrips
{
    private static readonly AsyncLocal<Counts> Current = new();

    public static void Begin() => Current.Value = new Counts();

    public static void Query()
    {
        if (Current.Value is { } counts)
            counts.Queries++;
    }

    public static void Session()
    {
        if (Current.Value is { } counts)
            counts.Sessions++;
    }

    public static (int Queries, int Sessions) Read() =>
        Current.Value is { } counts ? (counts.Queries, counts.Sessions) : (0, 0);

    private sealed class Counts
    {
        public int Queries { get; set; }
        public int Sessions { get; set; }
    }
}

public sealed class TenantCommandInterceptor : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Apply(command, eventData);
        DbRoundTrips.Query();
        return base.ReaderExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await ApplyAsync(command, eventData, cancellationToken);
        DbRoundTrips.Query();
        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Apply(command, eventData);
        DbRoundTrips.Query();
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await ApplyAsync(command, eventData, cancellationToken);
        DbRoundTrips.Query();
        return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Apply(command, eventData);
        DbRoundTrips.Query();
        return base.ScalarExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await ApplyAsync(command, eventData, cancellationToken);
        DbRoundTrips.Query();
        return await base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    internal static void Invalidate(DbConnection connection) => SessionStamp.Invalidate(connection);

    private static void Apply(DbCommand command, CommandEventData eventData)
    {
        if (command.Connection is null)
            return;
        var (user, school) = CurrentSession(eventData);
        if (command.Transaction is null
            && command.Connection.State == ConnectionState.Open
            && SessionStamp.Matches(command.Connection, user, school))
            return;
        var set = CreateSessionCommand(command, user, school);
        using (set)
            set.ExecuteNonQuery();
        SessionStamp.Mark(command.Connection, user, school);
        DbRoundTrips.Session();
    }

    private static async Task ApplyAsync(DbCommand command, CommandEventData eventData, CancellationToken cancellationToken)
    {
        if (command.Connection is null)
            return;
        var (user, school) = CurrentSession(eventData);
        if (command.Transaction is null
            && command.Connection.State == ConnectionState.Open
            && SessionStamp.Matches(command.Connection, user, school))
            return;
        var set = CreateSessionCommand(command, user, school);
        await using (set)
            await set.ExecuteNonQueryAsync(cancellationToken);
        SessionStamp.Mark(command.Connection, user, school);
        DbRoundTrips.Session();
    }

    private static (string User, string School) CurrentSession(CommandEventData eventData)
    {
        if (eventData.Context is AppDbContext db)
        {
            var user = db.SessionUserId == Guid.Empty ? "" : db.SessionUserId.ToString("D");
            var school = db.SessionSchoolId == Guid.Empty ? "" : db.SessionSchoolId.ToString("D");
            return (user, school);
        }

        var session = DbSessionAmbient.Session;
        var ambientUser = session?.HasUser == true ? session.UserId.ToString("D") : "";
        var ambientSchool = session?.HasSchool == true ? session.SchoolId.ToString("D") : "";
        return (ambientUser, ambientSchool);
    }

    private static DbCommand CreateSessionCommand(DbCommand command, string user, string school)
    {
        var set = command.Connection!.CreateCommand();
        set.Transaction = command.Transaction;
        set.CommandText = "SELECT set_config('app.user_id', @user_id, false), set_config('app.school_id', @school_id, false)";
        Add(set, "user_id", user);
        Add(set, "school_id", school);
        if (command.Connection.State != ConnectionState.Open)
            command.Connection.Open();
        return set;
    }

    private static void Add(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static class SessionStamp
    {
        private static readonly ConditionalWeakTable<DbConnection, Values> Applied = new();

        public static void Invalidate(DbConnection connection)
        {
            Applied.GetOrCreateValue(connection).Ready = false;
        }

        public static bool Matches(DbConnection connection, string user, string school)
        {
            return Applied.TryGetValue(connection, out var values)
                && values.Ready
                && values.UserId == user
                && values.SchoolId == school;
        }

        public static void Mark(DbConnection connection, string user, string school)
        {
            var values = Applied.GetOrCreateValue(connection);
            values.Ready = true;
            values.UserId = user;
            values.SchoolId = school;
        }

        private sealed class Values
        {
            public bool Ready { get; set; }
            public string UserId { get; set; } = "";
            public string SchoolId { get; set; } = "";
        }
    }
}

public sealed class TenantTransactionInterceptor : DbTransactionInterceptor
{
    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        Invalidate(transaction);
        base.TransactionRolledBack(transaction, eventData);
    }

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Invalidate(transaction);
        return base.TransactionRolledBackAsync(transaction, eventData, cancellationToken);
    }

    private static void Invalidate(DbTransaction transaction)
    {
        if (transaction.Connection is DbConnection connection)
            TenantCommandInterceptor.Invalidate(connection);
    }
}

public sealed class TenantConnectionInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        TenantCommandInterceptor.Invalidate(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        TenantCommandInterceptor.Invalidate(connection);
        return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData)
    {
        TenantCommandInterceptor.Invalidate(connection);
        base.ConnectionClosed(connection, eventData);
    }

    public override Task ConnectionClosedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        TenantCommandInterceptor.Invalidate(connection);
        return base.ConnectionClosedAsync(connection, eventData);
    }
}
