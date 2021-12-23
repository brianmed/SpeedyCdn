using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

namespace SpeedyCdn.Server.Entities.Edge
{
    public class TableSequenceEntity
    {
        [Key]
        public string Name { get; set; }

        public long Sequence { get; set; }
    }
}
