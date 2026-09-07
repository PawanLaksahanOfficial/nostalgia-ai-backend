namespace Application.DTOs
{
    public class RegisterRequest
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class AuthResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = null!;
    }

    public class UserDto
    {
        public int UserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public string Tier { get; set; } = "free";
        public int MonthlyMemoriesUsed { get; set; }
        public int MonthlyMemoriesLimit { get; set; }
    }

    public class UpdateProfileRequest
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? AvatarUrl { get; set; }
    }

    public class ChangePasswordRequest
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class ForgotPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class UsageQuota
    {
        public int MonthlyMemoriesUsed { get; set; }
        public int MonthlyMemoriesLimit { get; set; }
        public int MaxVideoDurationSeconds { get; set; }
        public string Quality { get; set; } = "standard";
        public bool HasWatermark { get; set; } = true;
    }

    public class CreateCheckoutSessionRequest
    {
        public string PriceId { get; set; } = string.Empty;
        public string SuccessUrl { get; set; } = string.Empty;
        public string CancelUrl { get; set; } = string.Empty;
    }

    public class CheckoutSessionResponse
    {
        public string SessionUrl { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
    }

    public class GenerateRequest
    {
        public string Text { get; set; } = string.Empty;
    }

    public class PlanOption
    {
        public string Tier { get; set; } = "free";
        public string Name { get; set; } = string.Empty;
        public string? PriceId { get; set; }
        public long? AmountMinorUnits { get; set; }
        public string? Currency { get; set; }
        public string? Interval { get; set; }
        public int MonthlyMemories { get; set; }
        public int MaxVideoDurationSeconds { get; set; }
        public string Quality { get; set; } = "standard";
        public bool HasWatermark { get; set; }
    }

    public class SubscriptionStatusResponse
    {
        public string Tier { get; set; } = "free";
        public bool HasActiveSubscription { get; set; }
        public bool CancelAtPeriodEnd { get; set; }
        public DateTime? CurrentPeriodEnd { get; set; }
    }

    public class PortalSessionRequest
    {
        public string ReturnUrl { get; set; } = string.Empty;
    }

    public class PortalSessionResponse
    {
        public string PortalUrl { get; set; } = string.Empty;
    }
}