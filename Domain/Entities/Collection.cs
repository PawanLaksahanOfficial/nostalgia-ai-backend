using System.ComponentModel.DataAnnotations;

namespace Domain.Entities
{
    public class Collection
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public ICollection<UserMemory> Memories { get; set; } = new List<UserMemory>();
    }
}