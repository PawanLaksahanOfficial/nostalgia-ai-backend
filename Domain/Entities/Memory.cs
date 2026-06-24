namespace Domain.Entities
{
    public class Memory
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Story { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}