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

            var stripeKey = _configuration["Stripe:SecretKey"];
            if (!string.IsNullOrEmpty(stripeKey))
            {
                StripeConfiguration.ApiKey = stripeKey;
            }
        }

        public async Task<UsageQuota> GetUsageQuotaAsync(int userId)
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.UserId == userId && !u.Deleted);

            if (user == null)
            {
                throw new UnauthorizedAccessException("User not found.");
            }

            await ResetMonthlyUsageIfNeededAsync(user);
            var isPremium = user.Tier == UserTier.Premium;

            return new UsageQuota
            {
                MonthlyMemoriesUsed = user.MonthlyMemoryCount,
                MonthlyMemoriesLimit = isPremium
                    ? _configuration.GetValue<int>("TierLimits:Premium:MonthlyMemories", 100)
                    : _configuration.GetValue<int>("TierLimits:Free:MonthlyMemories", 3),
                MaxVideoDurationSeconds = isPremium
                    ? _configuration.GetValue<int>("TierLimits:Premium:MaxVideoDuration", 60)
                    : _configuration.GetValue<int>("TierLimits:Free:MaxVideoDuration", 30),
                Quality = isPremium
                    ? _configuration.GetValue<string>("TierLimits:Premium:Quality", "hd") ?? "hd"
                    : _configuration.GetValue<string>("TierLimits:Free:Quality", "standard") ?? "standard",
                HasWatermark = !isPremium
            };
        }

        public async Task IncrementMonthlyUsageAsync(int userId)
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.UserId == userId && !u.Deleted);
            if (user == null) return;

            await ResetMonthlyUsageIfNeededAsync(user);
            user.MonthlyMemoryCount++;
            await _dbContext.SaveChangesAsync();
        }

        public async Task ResetMonthlyUsageAsync(int userId)
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.UserId == userId && !u.Deleted);
            if (user == null) return;

            user.MonthlyMemoryCount = 0;
            user.MonthlyCountResetDate = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        public async Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(int userId, string priceId, string successUrl, string cancelUrl)
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.UserId == userId && !u.Deleted);
            if (user == null)
            {
                throw new UnauthorizedAccessException("User not found.");
            }
            // Create Stripe Customer if the user doesn't have one yet
            if (string.IsNullOrEmpty(user.StripeCustomerId))
            {
                var customerOptions = new CustomerCreateOptions
                {
                    Email = user.Email,
                    Name = $"{user.FirstName} {user.LastName}",
                    Metadata = new Dictionary<string, string>
                    {
                        { "userId", user.UserId.ToString() }
                    }
                };
                var customerService = new CustomerService();
                var customer = await customerService.CreateAsync(customerOptions);
                user.StripeCustomerId = customer.Id;
                await _dbContext.SaveChangesAsync();
            }
            var options = new SessionCreateOptions
            {
                Customer = user.StripeCustomerId,
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
            var service = new SessionService();
            var session = await service.CreateAsync(options);
            return new CheckoutSessionResponse
            {
                SessionId = session.Id,
                SessionUrl = session.Url
            };
        }

        public async Task HandleSubscriptionCreatedAsync(string stripeCustomerId, string stripeSubscriptionId)
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.StripeCustomerId == stripeCustomerId && !u.Deleted);
            if (user == null) return;

            user.StripeSubscriptionId = stripeSubscriptionId;
            user.Tier = UserTier.Premium;
            user.SubscriptionEndDate = DateTime.UtcNow.AddMonths(1);
            user.MonthlyMemoryCount = 0;
            await _dbContext.SaveChangesAsync();
        }

        public async Task HandleSubscriptionDeletedAsync(string stripeSubscriptionId)
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.StripeSubscriptionId == stripeSubscriptionId && !u.Deleted);
            if (user == null) return;

            user.Tier = UserTier.Free;
            user.StripeSubscriptionId = null;
            user.SubscriptionEndDate = null;
            await _dbContext.SaveChangesAsync();
        }

        public async Task HandleCheckoutSessionCompletedAsync(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;

            var service = new SessionService();
            var session = await service.GetAsync(sessionId);
            if (session == null) return;

            var userIdStr = session.Metadata?.GetValueOrDefault("userId");
            if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId)) return;

            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null) return;

            // Save the Stripe Customer ID on the user record
            user.StripeCustomerId = session.CustomerId;
            await _dbContext.SaveChangesAsync();
        }

        public async Task HandleSubscriptionUpdatedAsync(string subscriptionId, string status)
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.StripeSubscriptionId == subscriptionId && !u.Deleted);
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
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.StripeCustomerId == customerId && !u.Deleted);
            if (user == null) return;

            user.MonthlyMemoryCount = 0;
            user.MonthlyCountResetDate = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        public async Task HandlePaymentFailedAsync(string customerId)
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.StripeCustomerId == customerId && !u.Deleted);
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