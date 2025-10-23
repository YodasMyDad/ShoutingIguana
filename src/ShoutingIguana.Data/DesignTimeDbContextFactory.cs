using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ShoutingIguana.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SqliteShoutingIguanaDbContext>
{
    public SqliteShoutingIguanaDbContext CreateDbContext(string[] args)
    {
        var connectionString = "Data Source=shouting_iguana.db";
        var optionsBuilder = new DbContextOptionsBuilder<SqliteShoutingIguanaDbContext>();
        optionsBuilder.UseSqlite(connectionString);

        return new SqliteShoutingIguanaDbContext(optionsBuilder.Options, connectionString);
    }
}

