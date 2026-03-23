using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class SocialProfile
    {
        public string Email { get; set; } = "";
        public string? GivenName { get; set; }
        public string? FamilyName { get; set; }
    }
}
