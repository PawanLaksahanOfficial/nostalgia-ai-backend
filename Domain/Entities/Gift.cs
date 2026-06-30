using System.ComponentModel.DataAnnotations;

namespace Domain.Entities
{
    public class Gift
    {
        [Key]
        public int Id { get; set; }
        public int SenderId { get; set; }
        public User Sender { get; set; } = null!;
        public int MemoryId { get; set; }
        public UserMemory Memory { get; set; } = null!;
        public string RecipientEmail { get; set; } = string.Empty;
        public string? Message { get; set; }
        public bool IsOpened { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? OpenedAt { get; set; }
    }
}