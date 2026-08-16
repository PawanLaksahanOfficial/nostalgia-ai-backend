using Application.DTOs;

namespace Application.Interfaces
{
    public interface ISubscriptionService
    {
        Task<UsageQuota> GetUsageQuotaAsync(int userId);
        Task IncrementMonthlyUsageAsync(int userId);
        Task ResetMonthlyUsageAsync(int userId);
        Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(int userId, string priceId, string successUrl, string cancelUrl);
        Task CancelSubscriptionAsync(int userId);
        Task HandleSubscriptionCreatedAsync(string stripeCustomerId, string stripeSubscriptionId, DateTime? currentPeriodEnd);
        Task HandleSubscriptionDeletedAsync(string stripeSubscriptionId);
        Task HandleCheckoutSessionCompletedAsync(string sessionId);
        Task HandleSubscriptionUpdatedAsync(string subscriptionId, string status, DateTime? currentPeriodEnd);
        Task HandlePaymentSucceededAsync(string customerId);
        Task HandlePaymentFailedAsync(string customerId);
        Task<bool> TryMarkEventProcessedAsync(string eventId);
    }
}
