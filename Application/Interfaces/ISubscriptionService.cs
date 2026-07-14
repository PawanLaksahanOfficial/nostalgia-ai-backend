using Application.DTOs;

namespace Application.Interfaces
{
    public interface ISubscriptionService
    {
        Task<UsageQuota> GetUsageQuotaAsync(int userId);
        Task<bool> CanCreateMemoryAsync(int userId);
        Task IncrementMonthlyUsageAsync(int userId);
        Task ResetMonthlyUsageAsync(int userId);
        Task<string> CreateCheckoutSessionAsync(int userId, string priceId, string successUrl, string cancelUrl);
        Task HandleSubscriptionCreatedAsync(string stripeCustomerId, string stripeSubscriptionId);
        Task HandleSubscriptionDeletedAsync(string stripeSubscriptionId);
        Task HandleCheckoutSessionCompletedAsync(string userIdStr);
        Task HandleSubscriptionUpdatedAsync(string subscriptionId, string status);
        Task HandlePaymentSucceededAsync(string customerId);
        Task HandlePaymentFailedAsync(string customerId);
    }
}