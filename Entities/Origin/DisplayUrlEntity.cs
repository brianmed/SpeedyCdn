using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

namespace SpeedyCdn.Server.Entities.Origin
{
    [Index(nameof(Display), IsUnique = true)]
    public class DisplayUrlEntity
    {
        [Key]
        public long DisplayUrlId { get; set; }

        [Required]
        public string Display { get; set; }

        [Required]
        public string RedirectPath { get; set; }

        public string QueryString { get; set; }

        [DefaultValue(typeof(DateTime), "")]        
        public DateTime Updated { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime Inserted { get; set; } = DateTime.UtcNow;
    }
}
