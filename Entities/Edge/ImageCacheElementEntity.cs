using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

namespace SpeedyCdn.Server.Entities.Edge
{
    [Index(nameof(UrlPath), nameof(QueryString), IsUnique=true)]
    [Index(nameof(LastAccessedUtc))]
    public class ImageCacheElementEntity
    {
        [Key]
        public long ImageCacheElementId { get; set; }

        [Required]
        public string CachePath { get; set; }

        [Required]
        public string UrlPath { get; set; }

        [Required]
        public string QueryString { get; set; }

        [Required]
        public long FileSizeBytes { get; set; }

        [Required]
        public long LastAccessedUtc { get; set; }

        [DefaultValue(typeof(DateTime), "")]        
        public DateTime Updated { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime Inserted { get; set; } = DateTime.UtcNow;
    }
}
