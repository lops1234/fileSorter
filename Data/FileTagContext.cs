using System;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace FileTagger.Data
{
    public class FileTagContext : DbContext
    {
        public DbSet<FileRecord> FileRecords { get; set; }
        public DbSet<Tag> Tags { get; set; }
        public DbSet<FileTag> FileTags { get; set; }
        public DbSet<WatchedDirectory> WatchedDirectories { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileTagger", "filetagger.db");
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
            modelBuilder.Entity<Tag>()
                .HasIndex(t => t.Name)
                .IsUnique();

            modelBuilder.Entity<FileRecord>()
                .HasIndex(f => f.FilePath)
                .IsUnique();

            modelBuilder.Entity<WatchedDirectory>()
                .HasIndex(w => w.DirectoryPath)
                .IsUnique();

            // Configure FileTag relationships
            modelBuilder.Entity<FileTag>()
                .HasOne(ft => ft.FileRecord)
                .WithMany(f => f.FileTags)
                .HasForeignKey(ft => ft.FileRecordId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<FileTag>()
                .HasOne(ft => ft.Tag)
                .WithMany(t => t.FileTags)
                .HasForeignKey(ft => ft.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            // Ensure unique file-tag combinations
            modelBuilder.Entity<FileTag>()
                .HasIndex(ft => new { ft.FileRecordId, ft.TagId })
                .IsUnique();
        }
    }
}
