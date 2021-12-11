using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpeedyCdn.Server.Entities.Origin
{
    public class AppEntity
    {
        [Key]
        public long AppId { get; set; }

        [Required]
        public string JwtSecret { get; set; }

        [Required]
        public string ApiKey { get; set; }

        [DefaultValue(typeof(DateTime), "")]        
        public DateTime Updated { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime Inserted { get; set; } = DateTime.UtcNow;
    }
}

