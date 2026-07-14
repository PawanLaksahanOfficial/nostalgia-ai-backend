using System.ComponentModel.DataAnnotations;

namespace Domain.Entities
{
    public class Comment
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public int MemoryId { get; set; }
        public UserMemory Memory { get; set; } = null!;
        public string Text { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}