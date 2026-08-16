using System.ComponentModel.DataAnnotations;

namespace Domain.Entities
{
    public class ProcessedStripeEvent
    {
        [Key]
        public int Id { get; set; }
        public string EventId { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }
}
