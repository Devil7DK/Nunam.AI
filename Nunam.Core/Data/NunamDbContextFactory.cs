using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nunam.Core.Data
{
    /// <summary>
    /// Design-time factory used by <c>dotnet ef</c> to construct a <see cref="NunamDbContext"/>
    /// without needing a real connection or a configured DI container in the startup project.
    /// Connection string is only used when actually applying a migration
    /// (<c>dotnet ef database update</c>); pure schema generation does not connect.
    /// </summary>
    public class NunamDbContextFactory : IDesignTimeDbContextFactory<NunamDbContext>
    {
        public NunamDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<NunamDbContext>()
                .UseNpgsql(
                    "Host=localhost;Port=5442;Database=nunam_dev;Username=postgres;Password=postgres",
                    npg => npg.UseVector())
                .Options;

            return new NunamDbContext(options);
        }
    }
}
