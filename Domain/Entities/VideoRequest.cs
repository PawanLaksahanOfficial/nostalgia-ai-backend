using System.ComponentModel.DataAnnotations;

namespace Domain.Entities
{
    public class VideoRequest
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        // Keyed hash of the requesting address; null when the real client address was unknown.
        public string? IpHash { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
