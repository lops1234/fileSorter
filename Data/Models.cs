using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FileTagger.Data
{
    public class FileRecord
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string FilePath { get; set; } = string.Empty;
        
        [Required]
        public string FileName { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        
        // Navigation property
        public virtual ICollection<FileTag> FileTags { get; set; } = new List<FileTag>();
    }

    public class Tag
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;
        
        [MaxLength(500)]
        public string Description { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation property
        public virtual ICollection<FileTag> FileTags { get; set; } = new List<FileTag>();
    }

    public class FileTag
    {
        [Key]
        public int Id { get; set; }
        
        [ForeignKey(nameof(FileRecord))]
        public int FileRecordId { get; set; }
        
        [ForeignKey(nameof(Tag))]
        public int TagId { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation properties
        public virtual FileRecord FileRecord { get; set; } = null!;
        public virtual Tag Tag { get; set; } = null!;
    }

    public class WatchedDirectory
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string DirectoryPath { get; set; } = string.Empty;
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
