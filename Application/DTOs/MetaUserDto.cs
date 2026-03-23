using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class MetaUserDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public MetaPictureWrapper Picture { get; set; } = new();
    }

    public class MetaPictureWrapper
    {
        public MetaPictureData Data { get; set; } = new();
    }

    public class MetaPictureData
    {
        public string Url { get; set; } = string.Empty;
    }
}
