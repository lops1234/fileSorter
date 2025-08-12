using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FileTagger.Data
{
    /// <summary>
    /// Main database models - stores watched directories and aggregated tag information
    /// </summary>
    
    public class WatchedDirectory
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string DirectoryPath { get; set; } = string.Empty;
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime LastSyncAt { get; set; } = DateTime.UtcNow;
        
        // Navigation property
        public virtual ICollection<AggregatedTag> AggregatedTags { get; set; } = new List<AggregatedTag>();
    }

    public class AggregatedTag
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;
        
        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;
        
        [ForeignKey(nameof(SourceDirectory))]
        public int SourceDirectoryId { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
        
        // How many files use this tag across all directories
        public int UsageCount { get; set; } = 0;
        
        // Navigation properties
        public virtual WatchedDirectory SourceDirectory { get; set; } = null!;
    }
}
