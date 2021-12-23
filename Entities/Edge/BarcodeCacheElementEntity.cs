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
    public class BarcodeCacheElementEntity
    {
        [Key]
        public long BarcodeCacheElementId { get; set; }

        [Required]
        public string CachePath { get; set; }

        [Required]
        public string UrlPath { get; set; }

        [Required]
        public string QueryString { get; set; }

        public long LastAccessedUtc { get; set; }

        public long FileSizeBytes { get; set; }

        [DefaultValue(typeof(DateTime), "")]        
        public DateTime Updated { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime Inserted { get; set; } = DateTime.UtcNow;
    }
}
