using System;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace FileTagger.Data
{
    /// <summary>
    /// Central database context - stores ALL tags and files for ALL directories in one local database
    /// This is the authoritative source of truth; folder databases are just for backup/sync
    /// </summary>
    public class CentralDbContext : DbContext
    {
        private static readonly string DbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "FileTagger", 
            "filetagger.db");

        public DbSet<CentralDirectory> Directories { get; set; }
        public DbSet<CentralTag> Tags { get; set; }
        public DbSet<CentralFileRecord> FileRecords { get; set; }
        public DbSet<CentralFileTag> FileTags { get; set; }

        public static string GetDatabasePath() => DbPath;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var directory = Path.GetDirectoryName(DbPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory!);
            }
            
            optionsBuilder.UseSqlite($"Data Source={DbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configure unique constraints for directories
            modelBuilder.Entity<CentralDirectory>()
                .HasIndex(d => d.DirectoryPath)
                .IsUnique();

            // Configure unique tag names per directory
            modelBuilder.Entity<CentralTag>()
                .HasIndex(t => new { t.Name, t.DirectoryId })
                .IsUnique();

            // Configure unique file paths per directory
            modelBuilder.Entity<CentralFileRecord>()
                .HasIndex(f => new { f.RelativePath, f.DirectoryId })
                .IsUnique();

            // Configure CentralTag relationships
            modelBuilder.Entity<CentralTag>()
                .HasOne(t => t.Directory)
                .WithMany(d => d.Tags)
                .HasForeignKey(t => t.DirectoryId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure CentralFileRecord relationships
            modelBuilder.Entity<CentralFileRecord>()
                .HasOne(f => f.Directory)
                .WithMany(d => d.FileRecords)
                .HasForeignKey(f => f.DirectoryId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure CentralFileTag relationships
            modelBuilder.Entity<CentralFileTag>()
                .HasOne(ft => ft.FileRecord)
                .WithMany(f => f.FileTags)
                .HasForeignKey(ft => ft.FileRecordId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CentralFileTag>()
                .HasOne(ft => ft.Tag)
                .WithMany(t => t.FileTags)
                .HasForeignKey(ft => ft.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            // Ensure unique file-tag combinations
            modelBuilder.Entity<CentralFileTag>()
                .HasIndex(ft => new { ft.FileRecordId, ft.TagId })
                .IsUnique();
        }
    }
}

