using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartSchool.Infrastructure.Persistence;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Design-time tools always use this disposable local placeholder.
        // There is no environment switch that points `dotnet ef` at a remote database.
        const string localPlaceholder =
            "Host=127.0.0.1;Database=smart_school;Username=smart_school_app;Password=unused";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSchoolPostgres(PostgresConnectionStrings.ToNpgsql(localPlaceholder))
            .Options;
        return new AppDbContext(options, new DbSession());
    }
}
