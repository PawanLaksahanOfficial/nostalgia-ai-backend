using System.ComponentModel.DataAnnotations;

namespace Domain.Entities
{
    public class Like
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public int MemoryId { get; set; }
        public UserMemory Memory { get; set; } = null!;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}