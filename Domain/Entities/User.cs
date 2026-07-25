using System.ComponentModel.DataAnnotations;

namespace Domain.Entities
{
    public class User
    {
        [Key]
        public int UserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PasswordHash { get; set; }
        public string? AvatarUrl { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastUpdatedDate { get; set; }
        public DateTime LastSignInDate { get; set; }
        public bool Active { get; set; }
        public bool Deleted { get; set; }
        public UserTier Tier { get; set; } = UserTier.Free;

        public ICollection<UserMemory> Memories { get; set; } = new List<UserMemory>();
    }
}
