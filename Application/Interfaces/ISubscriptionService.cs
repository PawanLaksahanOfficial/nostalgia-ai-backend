using Application.DTOs;

namespace Application.Interfaces
{
    public interface ISubscriptionService
    {
        Task<UsageQuota> GetUsageQuotaAsync(int userId);
        Task<bool> TryConsumeQuotaAsync(int userId);
        Task RefundQuotaAsync(int userId);

        Task ResetMonthlyUsageAsync(int userId);
        Task<IReadOnlyList<PlanOption>> GetPlansAsync();
        Task<SubscriptionStatusResponse> GetSubscriptionStatusAsync(int userId);
        Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(int userId, string priceId, string successUrl, string cancelUrl);
        Task<PortalSessionResponse> CreatePortalSessionAsync(int userId, string returnUrl);
        Task CancelSubscriptionAsync(int userId);
        Task ResumeSubscriptionAsync(int userId);
        Task HandleSubscriptionCreatedAsync(string stripeCustomerId, string stripeSubscriptionId, string? priceId, DateTime? currentPeriodEnd);
        Task HandleSubscriptionDeletedAsync(string stripeSubscriptionId);
        Task HandleCheckoutSessionCompletedAsync(string sessionId);
        Task HandleSubscriptionUpdatedAsync(string subscriptionId, string status, string? priceId, DateTime? currentPeriodEnd);
        Task HandlePaymentSucceededAsync(string customerId);
        Task HandlePaymentFailedAsync(string customerId);
        Task<bool> ProcessEventOnceAsync(string eventId, Func<Task> handler);
    }
}
