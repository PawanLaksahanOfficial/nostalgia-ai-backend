using System.ComponentModel.DataAnnotations;

namespace Domain.Entities
{
    public class MemoryShareLink
    {
        [Key]
        public int Id { get; set; }
        public int UserMemoryId { get; set; }
        public UserMemory UserMemory { get; set; } = null!;
        public int CreatedByUserId { get; set; }
        public string Token { get; set; } = string.Empty;
        public string? Label { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool IsRevoked { get; set; }
        public int ViewCount { get; set; }
        public DateTime? LastViewedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
