using System;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace FileTagger.Data
{
    /// <summary>
    /// Main database context - stores watched directories and aggregated tag information
    /// </summary>
    public class MainDbContext : DbContext
    {
        public DbSet<WatchedDirectory> WatchedDirectories { get; set; }
        public DbSet<AggregatedTag> AggregatedTags { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileTagger", "main.db");
            var directory = Path.GetDirectoryName(dbPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory!);
            }
            
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configure unique constraints
            modelBuilder.Entity<WatchedDirectory>()
                .HasIndex(w => w.DirectoryPath)
                .IsUnique();

            // Configure AggregatedTag relationships
            modelBuilder.Entity<AggregatedTag>()
                .HasOne(at => at.SourceDirectory)
                .WithMany(wd => wd.AggregatedTags)
                .HasForeignKey(at => at.SourceDirectoryId)
                .OnDelete(DeleteBehavior.Cascade);

            // Ensure unique tag names per directory
            modelBuilder.Entity<AggregatedTag>()
                .HasIndex(at => new { at.Name, at.SourceDirectoryId })
                .IsUnique();
        }
    }
}
