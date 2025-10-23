using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ShoutingIguana.Data;

public class SqliteShoutingIguanaDbContext : ShoutingIguanaDbContextBase, IShoutingIguanaDbContext
{
    private readonly string _connectionString;

    public SqliteShoutingIguanaDbContext(DbContextOptions<SqliteShoutingIguanaDbContext> options, string connectionString)
        : base(options)
    {
        _connectionString = connectionString;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite(_connectionString, builder =>
            {
                builder.MigrationsHistoryTable("DbMigrations");
                builder.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                builder.CommandTimeout(30);
            });
            
#if DEBUG
            optionsBuilder.EnableSensitiveDataLogging();
#endif
            optionsBuilder
                .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        }
    }
}

