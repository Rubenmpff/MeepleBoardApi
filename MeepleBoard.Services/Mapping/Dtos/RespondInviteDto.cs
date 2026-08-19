using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeepleBoard.Services.Mapping.Dtos
{
    public class RespondInviteDto
    {
        [Required]
        public bool Accept { get; set; }
    }
}
