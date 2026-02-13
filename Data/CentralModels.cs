using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FileTagger.Data
{
    /// <summary>
    /// Central database models - stores ALL tags and files for ALL directories in one local database
    /// </summary>
    
    /// <summary>
    /// Represents a watched directory in the central database
    /// </summary>
    public class CentralDirectory
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string DirectoryPath { get; set; } = string.Empty;
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime LastSyncAt { get; set; } = DateTime.UtcNow;
        
        // Navigation properties
        public virtual ICollection<CentralTag> Tags { get; set; } = new List<CentralTag>();
        public virtual ICollection<CentralFileRecord> FileRecords { get; set; } = new List<CentralFileRecord>();
    }

    /// <summary>
    /// Represents a tag in the central database, linked to a specific directory
    /// </summary>
    public class CentralTag
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;
        
        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;
        
        [ForeignKey(nameof(Directory))]
        public int DirectoryId { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation properties
        public virtual CentralDirectory Directory { get; set; } = null!;
        public virtual ICollection<CentralFileTag> FileTags { get; set; } = new List<CentralFileTag>();
    }

    /// <summary>
    /// Represents a file record in the central database, linked to a specific directory
    /// </summary>
    public class CentralFileRecord
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [MaxLength(500)]
        public string FileName { get; set; } = string.Empty;
        
        [Required]
        public string RelativePath { get; set; } = string.Empty; // Relative to directory root
        
        [ForeignKey(nameof(Directory))]
        public int DirectoryId { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        
        public long FileSize { get; set; } = 0;
        
        // Navigation properties
        public virtual CentralDirectory Directory { get; set; } = null!;
        public virtual ICollection<CentralFileTag> FileTags { get; set; } = new List<CentralFileTag>();
    }

    /// <summary>
    /// Represents the many-to-many relationship between files and tags
    /// </summary>
    public class CentralFileTag
    {
        [Key]
        public int Id { get; set; }
        
        [ForeignKey(nameof(FileRecord))]
        public int FileRecordId { get; set; }
        
        [ForeignKey(nameof(Tag))]
        public int TagId { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation properties
        public virtual CentralFileRecord FileRecord { get; set; } = null!;
        public virtual CentralTag Tag { get; set; } = null!;
    }
}

