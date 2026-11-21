using Microsoft.EntityFrameworkCore;
using Nunam.Core.Entities;

namespace Nunam.Core.Data
{
    public class NunamDbContext(DbContextOptions<NunamDbContext> options) : DbContext(options)
    {
        public DbSet<Machine> Machines => Set<Machine>();
        public DbSet<IndexedFile> Files => Set<IndexedFile>();
        public DbSet<IndexJob> IndexJobs => Set<IndexJob>();
        public DbSet<User> Users => Set<User>();
        public DbSet<UserSession> UserSessions => Set<UserSession>();
        public DbSet<MachineAccess> MachineAccess => Set<MachineAccess>();
        public DbSet<IndexerStatus> IndexerStatus => Set<IndexerStatus>();
        public DbSet<IndexerSettings> IndexerSettings => Set<IndexerSettings>();
        public DbSet<IndexerCommand> IndexerCommands => Set<IndexerCommand>();
        public DbSet<IndexerLogEntry> IndexerLogs => Set<IndexerLogEntry>();

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
                e.Property(m => m.DbRole).HasDefaultValueSql("current_user").ValueGeneratedOnAdd();
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
                e.Property(f => f.Embedding).HasColumnType("vector(1024)");

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

            model.Entity<User>(e =>
            {
                e.HasIndex(u => u.UserName).IsUnique();
                e.Property(u => u.Role).HasConversion<string>();
            });

            model.Entity<UserSession>(e =>
            {
                e.HasIndex(s => s.TokenHash).IsUnique();
                e.HasIndex(s => s.UserId);

                e.HasOne(s => s.User)
                    .WithMany(u => u.Sessions)
                    .HasForeignKey(s => s.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            model.Entity<MachineAccess>(e =>
            {
                e.HasIndex(a => new { a.UserId, a.MachineId }).IsUnique();
                e.HasIndex(a => a.MachineId);
                e.Property(a => a.Role).HasConversion<string>();

                e.HasOne(a => a.Machine)
                    .WithMany(m => m.Access)
                    .HasForeignKey(a => a.MachineId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(a => a.User)
                    .WithMany(u => u.MachineAccess)
                    .HasForeignKey(a => a.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            model.Entity<IndexerStatus>(e =>
            {
                e.ToTable("indexer_status");
                e.HasKey(s => s.MachineId);
                e.Property(s => s.State).HasConversion<string>();
                e.Property(s => s.Config).HasColumnType("jsonb");
                e.Property(s => s.WatchPaths).HasColumnType("jsonb");
                e.HasOne(s => s.Machine).WithOne().HasForeignKey<IndexerStatus>(s => s.MachineId).OnDelete(DeleteBehavior.Cascade);
            });

            model.Entity<IndexerSettings>(e =>
            {
                e.HasKey(s => s.MachineId);
                e.Property(s => s.Settings).HasColumnType("jsonb");
                e.HasOne(s => s.Machine).WithOne().HasForeignKey<IndexerSettings>(s => s.MachineId).OnDelete(DeleteBehavior.Cascade);
            });

            model.Entity<IndexerCommand>(e =>
            {
                e.HasIndex(c => new { c.MachineId, c.Status });
                e.Property(c => c.Kind).HasConversion<string>();
                e.Property(c => c.Status).HasConversion<string>();
                e.HasOne(c => c.Machine).WithMany().HasForeignKey(c => c.MachineId).OnDelete(DeleteBehavior.Cascade);
            });

            model.Entity<IndexerLogEntry>(e =>
            {
                e.ToTable("indexer_logs");
                e.HasIndex(l => new { l.MachineId, l.At });
                e.Property(l => l.Level).HasConversion<string>();
                e.HasOne(l => l.Machine).WithMany().HasForeignKey(l => l.MachineId).OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
