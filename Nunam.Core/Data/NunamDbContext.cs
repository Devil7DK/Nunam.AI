using Microsoft.EntityFrameworkCore;
using Nunam.Core.Entities;

namespace Nunam.Core.Data
{
    public class NunamDbContext(DbContextOptions<NunamDbContext> options) : DbContext(options)
    {
        public DbSet<Machine> Machines => Set<Machine>();
        public DbSet<IndexedFile> Files => Set<IndexedFile>();
        public DbSet<IndexJob> IndexJobs => Set<IndexJob>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);

            optionsBuilder.AddInterceptors(new TouchEntityInterceptor());
            optionsBuilder.UseSnakeCaseNamingConvention();
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            base.OnModelCreating(model);

            model.HasPostgresExtension("vector");

            model.Entity<Machine>(e =>
            {
                e.HasIndex(m => m.Hostname).IsUnique();
            });

            model.Entity<IndexedFile>(e =>
            {
                e.HasAlternateKey(f => new { f.MachineId, f.Path });

                e.HasIndex(f => f.MachineId);
                e.HasIndex(f => f.Extension);
                e.HasIndex(f => f.FileType);
                e.HasIndex(f => f.FileModifiedAt);
                e.HasIndex(f => f.Fts).HasMethod("gin");
                e.HasIndex(f => f.Embedding)
                    .HasMethod("hnsw")
                    .HasOperators("vector_cosine_ops");

                e.Property(f => f.FileType).HasConversion<string>();
                e.Property(f => f.Metadata).HasColumnType("jsonb");
                e.Property(f => f.Embedding).HasColumnType("vector(768)");

                e.HasOne(f => f.Machine)
                    .WithMany(m => m.Files)
                    .HasForeignKey(f => f.MachineId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            model.Entity<IndexJob>(e =>
            {
                e.HasIndex(j => new { j.Kind, j.Status });
                e.HasIndex(j => j.MachineId);

                e.Property(j => j.Kind).HasConversion<string>();
                e.Property(j => j.Status).HasConversion<string>();
                e.Property(j => j.Stage).HasConversion<string>();

                e.HasOne(j => j.Machine)
                    .WithMany(m => m.Jobs)
                    .HasForeignKey(j => j.MachineId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
