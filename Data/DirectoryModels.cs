using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FileTagger.Data
{
    /// <summary>
    /// Directory-specific database models - stores local tags and file associations for each directory
    /// </summary>
    
    public class LocalFileRecord
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [MaxLength(500)]
        public string FileName { get; set; } = string.Empty;
        
        [Required]
        public string RelativePath { get; set; } = string.Empty; // Relative to directory root
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        
        public long FileSize { get; set; } = 0;
        
        // Navigation property
        public virtual ICollection<LocalFileTag> LocalFileTags { get; set; } = new List<LocalFileTag>();
    }

    public class LocalTag
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;
        
        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation property
        public virtual ICollection<LocalFileTag> LocalFileTags { get; set; } = new List<LocalFileTag>();
    }

    public class LocalFileTag
    {
        [Key]
        public int Id { get; set; }
        
        [ForeignKey(nameof(LocalFileRecord))]
        public int LocalFileRecordId { get; set; }
        
        [ForeignKey(nameof(LocalTag))]
        public int LocalTagId { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation properties
        public virtual LocalFileRecord LocalFileRecord { get; set; } = null!;
        public virtual LocalTag LocalTag { get; set; } = null!;
    }
}
