using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Stripe;
using Stripe.Checkout;

namespace Infrastructure.Services
{
    public class SubscriptionService : ISubscriptionService
    {
        private readonly EFDbContext _dbContext;
        private readonly IConfiguration _configuration;

        public SubscriptionService(EFDbContext dbContext, IConfiguration configuration)
        {
            _dbContext = dbContext;
            _configuration = configuration;
            StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];
        }

        public async Task<UsageQuota> GetUsageQuotaAsync(int userId)
        {
            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null) 
            {
                throw new UnauthorizedAccessException("User not found.");
            }
            await ResetMonthlyUsageIfNeededAsync(user);
            var isPremium = user.Tier == UserTier.Premium;
            return new UsageQuota
            {
                MonthlyMemoriesUsed = user.MonthlyMemoryCount,
                MonthlyMemoriesLimit = isPremium ? 100 : 3,
                MaxVideoDurationSeconds = isPremium ? 60 : 30,
                Quality = isPremium ? "hd" : "standard",
                HasWatermark = !isPremium
            };
        }

        public async Task<bool> CanCreateMemoryAsync(int userId)
        {
            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null) return false;
            await ResetMonthlyUsageIfNeededAsync(user);
            var limit = user.Tier == UserTier.Premium ? 100 : 3;
            return user.MonthlyMemoryCount < limit;
        }

        public async Task IncrementMonthlyUsageAsync(int userId)
        {
            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null) return;
            await ResetMonthlyUsageIfNeededAsync(user);
            user.MonthlyMemoryCount++;
            await _dbContext.SaveChangesAsync();
        }

        public async Task ResetMonthlyUsageAsync(int userId)
        {
            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null) return;
            user.MonthlyMemoryCount = 0;
            user.MonthlyCountResetDate = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        public async Task<string> CreateCheckoutSessionAsync(int userId, string priceId, string successUrl, string cancelUrl)
        {
            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null) 
            {
                throw new UnauthorizedAccessException("User not found.");
            }
            var options = new SessionCreateOptions
            {
                CustomerEmail = user.Email,
                Metadata = new Dictionary<string, string>
                {
                    { "userId", user.UserId.ToString() }
                },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        Price = priceId,
                        Quantity = 1
                    }
                },
                Mode = "subscription",
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
            };
            if (!string.IsNullOrEmpty(user.StripeCustomerId))
            {
                options.Customer = user.StripeCustomerId;
            }
            var service = new SessionService();
            var session = await service.CreateAsync(options);
            return session.Id;
        }

        public async Task HandleSubscriptionCreatedAsync(string stripeCustomerId, string stripeSubscriptionId)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == stripeCustomerId);
            if (user == null) return;
            user.StripeSubscriptionId = stripeSubscriptionId;
            user.Tier = UserTier.Premium;
            user.SubscriptionEndDate = DateTime.UtcNow.AddMonths(1);
            user.MonthlyMemoryCount = 0;
            await _dbContext.SaveChangesAsync();
        }

        public async Task HandleSubscriptionDeletedAsync(string stripeSubscriptionId)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.StripeSubscriptionId == stripeSubscriptionId);
            if (user == null) return;
            user.Tier = UserTier.Free;
            user.StripeSubscriptionId = null;
            user.SubscriptionEndDate = null;
            await _dbContext.SaveChangesAsync();
        }

        public async Task HandleCheckoutSessionCompletedAsync(string userIdStr)
        {
            if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId)) return;
            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null) return;
            // CustomerId will be set by Stripe webhook on subscription.created
            await _dbContext.SaveChangesAsync();
        }

        public async Task HandleSubscriptionUpdatedAsync(string subscriptionId, string status)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.StripeSubscriptionId == subscriptionId);
            if (user == null) return;
            if (status == "active" || status == "trialing")
            {
                user.Tier = UserTier.Premium;
                user.SubscriptionEndDate = DateTime.UtcNow.AddMonths(1);
            }
            else if (status == "canceled" || status == "unpaid")
            {
                user.Tier = UserTier.Free;
                user.SubscriptionEndDate = null;
            }
            await _dbContext.SaveChangesAsync();
        }

        public async Task HandlePaymentSucceededAsync(string customerId)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == customerId);
            if (user == null) return;
            user.MonthlyMemoryCount = 0;
            user.MonthlyCountResetDate = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        public async Task HandlePaymentFailedAsync(string customerId)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == customerId);
            if (user == null) return;
            user.Tier = UserTier.Free;
            await _dbContext.SaveChangesAsync();
        }

        private async Task ResetMonthlyUsageIfNeededAsync(User user)
        {
            if (user.MonthlyCountResetDate == null || 
                user.MonthlyCountResetDate.Value.Month != DateTime.UtcNow.Month ||
                user.MonthlyCountResetDate.Value.Year != DateTime.UtcNow.Year)
            {
                user.MonthlyMemoryCount = 0;
                user.MonthlyCountResetDate = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }
        }
    }
}