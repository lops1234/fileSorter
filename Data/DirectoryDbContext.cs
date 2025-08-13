using System;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace FileTagger.Data
{
    /// <summary>
    /// Directory-specific database context - stores local tags and file associations for a specific directory
    /// </summary>
    public class DirectoryDbContext : DbContext
    {
        private readonly string _directoryPath;

        public DbSet<LocalFileRecord> LocalFileRecords { get; set; }
        public DbSet<LocalTag> LocalTags { get; set; }
        public DbSet<LocalFileTag> LocalFileTags { get; set; }

        public DirectoryDbContext(string directoryPath)
        {
            _directoryPath = directoryPath;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var dbPath = Path.Combine(_directoryPath, ".filetagger", "tags.db");
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
            modelBuilder.Entity<LocalTag>()
                .HasIndex(t => t.Name)
                .IsUnique();

            modelBuilder.Entity<LocalFileRecord>()
                .HasIndex(f => f.RelativePath)
                .IsUnique();

            // Configure LocalFileTag relationships
            modelBuilder.Entity<LocalFileTag>()
                .HasOne(lft => lft.LocalFileRecord)
                .WithMany(f => f.LocalFileTags)
                .HasForeignKey(lft => lft.LocalFileRecordId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<LocalFileTag>()
                .HasOne(lft => lft.LocalTag)
                .WithMany(t => t.LocalFileTags)
                .HasForeignKey(lft => lft.LocalTagId)
                .OnDelete(DeleteBehavior.Cascade);

            // Ensure unique file-tag combinations
            modelBuilder.Entity<LocalFileTag>()
                .HasIndex(lft => new { lft.LocalFileRecordId, lft.LocalTagId })
                .IsUnique();
        }
    }
}
