using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ShoutingIguana.Data;

public interface ISqliteDbContextFactory
{
    SqliteShoutingIguanaDbContext CreateDbContext(string projectPath);
}

public class SqliteDbContextFactory : ISqliteDbContextFactory
{
    public SqliteShoutingIguanaDbContext CreateDbContext(string projectPath)
    {
        var connectionString = $"Data Source={projectPath}";
        var optionsBuilder = new DbContextOptionsBuilder<SqliteShoutingIguanaDbContext>();
        
        return new SqliteShoutingIguanaDbContext(optionsBuilder.Options, connectionString);
    }
}

