using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;

namespace Infrastructure.Services
{
    public class SubscriptionService : ISubscriptionService
    {
        private readonly EFDbContext _dbContext;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SubscriptionService> _logger;

        public SubscriptionService(EFDbContext dbContext, IConfiguration configuration, ILogger<SubscriptionService> logger)
        {
            _dbContext = dbContext;
            _configuration = configuration;
            _logger = logger;

            var stripeKey = _configuration["Stripe:SecretKey"];
            if (!string.IsNullOrEmpty(stripeKey))
            {
                StripeConfiguration.ApiKey = stripeKey;
            }
        }

        private int GetMonthlyMemoryLimit(bool isPremium) => isPremium
            ? _configuration.GetValue("TierLimits:Premium:MonthlyMemories", 100)
            : _configuration.GetValue("TierLimits:Free:MonthlyMemories", 3);

        private int GetMaxVideoDuration(bool isPremium) => isPremium
            ? _configuration.GetValue("TierLimits:Premium:MaxVideoDuration", 60)
            : _configuration.GetValue("TierLimits:Free:MaxVideoDuration", 30);

        private string GetQuality(bool isPremium) => isPremium
            ? _configuration.GetValue("TierLimits:Premium:Quality", "hd") ?? "hd"
            : _configuration.GetValue("TierLimits:Free:Quality", "standard") ?? "standard";

        private async Task<User> GetUserOrThrowAsync(int userId, bool tracked = true)
        {
            var query = tracked ? _dbContext.Users : _dbContext.Users.AsNoTracking();
            var user = await query.FirstOrDefaultAsync(u => u.UserId == userId && !u.Deleted);
            if (user == null)
            {
                throw new UnauthorizedAccessException("User not found.");
            }
            return user;
        }

        public async Task<UsageQuota> GetUsageQuotaAsync(int userId)
        {
            var user = await GetUserOrThrowAsync(userId, tracked: false);
            var isPremium = user.IsPremiumActive();
            var used = IsCurrentUsageWindow(user.MonthlyCountResetDate) ? user.MonthlyMemoryCount : 0;

            return new UsageQuota
            {
                MonthlyMemoriesUsed = used,
                MonthlyMemoriesLimit = GetMonthlyMemoryLimit(isPremium),
                MaxVideoDurationSeconds = GetMaxVideoDuration(isPremium),
                Quality = GetQuality(isPremium),
                HasWatermark = !isPremium
            };
        }

        private static DateTime CurrentMonthStartUtc()
        {
            var now = DateTime.UtcNow;
            return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        private static bool IsCurrentUsageWindow(DateTime? resetDate) =>
            resetDate != null && resetDate.Value >= CurrentMonthStartUtc();

        public async Task<bool> TryConsumeQuotaAsync(int userId)
        {
            var user = await GetUserOrThrowAsync(userId, tracked: false);
            var limit = GetMonthlyMemoryLimit(user.IsPremiumActive());
            var monthStart = CurrentMonthStartUtc();
            await _dbContext.Users
                .Where(u => u.UserId == userId
                            && !u.Deleted
                            && (u.MonthlyCountResetDate == null || u.MonthlyCountResetDate < monthStart))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.MonthlyMemoryCount, 0)
                    .SetProperty(u => u.MonthlyCountResetDate, DateTime.UtcNow));
            var rowsAffected = await _dbContext.Users
                .Where(u => u.UserId == userId && !u.Deleted && u.MonthlyMemoryCount < limit)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.MonthlyMemoryCount, u => u.MonthlyMemoryCount + 1));

            return rowsAffected == 1;
        }

        public async Task RefundQuotaAsync(int userId)
        {
            await _dbContext.Users
                .Where(u => u.UserId == userId && !u.Deleted && u.MonthlyMemoryCount > 0)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.MonthlyMemoryCount, u => u.MonthlyMemoryCount - 1));
        }

        public async Task ResetMonthlyUsageAsync(int userId)
        {
            await _dbContext.Users
                .Where(u => u.UserId == userId && !u.Deleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.MonthlyMemoryCount, 0)
                    .SetProperty(u => u.MonthlyCountResetDate, DateTime.UtcNow));
        }

        public async Task<IReadOnlyList<PlanOption>> GetPlansAsync()
        {
            var plans = new List<PlanOption>
            {
                new PlanOption
                {
                    Tier = "free",
                    Name = "Free",
                    PriceId = null,
                    MonthlyMemories = GetMonthlyMemoryLimit(false),
                    MaxVideoDurationSeconds = GetMaxVideoDuration(false),
                    Quality = GetQuality(false),
                    HasWatermark = true
                }
            };

            var priceId = _configuration["Stripe:PremiumPriceId"];
            var premium = new PlanOption
            {
                Tier = "premium",
                Name = "Premium",
                PriceId = priceId,
                MonthlyMemories = GetMonthlyMemoryLimit(true),
                MaxVideoDurationSeconds = GetMaxVideoDuration(true),
                Quality = GetQuality(true),
                HasWatermark = false
            };
            if (!string.IsNullOrEmpty(priceId) && !string.IsNullOrEmpty(StripeConfiguration.ApiKey))
            {
                try
                {
                    var price = await new PriceService().GetAsync(priceId);
                    premium.AmountMinorUnits = price.UnitAmount;
                    premium.Currency = price.Currency;
                    premium.Interval = price.Recurring?.Interval;
                }
                catch (StripeException ex)
                {
                    _logger.LogError(ex, "Could not load Stripe price {PriceId} for the plans endpoint.", priceId);
                }
            }

            plans.Add(premium);
            return plans;
        }
        public async Task<SubscriptionStatusResponse> GetSubscriptionStatusAsync(int userId)
        {
            var user = await GetUserOrThrowAsync(userId, tracked: false);

            var status = new SubscriptionStatusResponse
            {
                Tier = user.IsPremiumActive() ? "premium" : "free",
                CurrentPeriodEnd = user.SubscriptionEndDate,
                HasActiveSubscription = !string.IsNullOrEmpty(user.StripeSubscriptionId)
            };

            if (!string.IsNullOrEmpty(user.StripeSubscriptionId) && !string.IsNullOrEmpty(StripeConfiguration.ApiKey))
            {
                try
                {
                    var subscription = await new Stripe.SubscriptionService().GetAsync(user.StripeSubscriptionId);
                    status.CancelAtPeriodEnd = subscription.CancelAtPeriodEnd;
                }
                catch (StripeException ex)
                {
                    _logger.LogError(ex, "Could not load Stripe subscription {SubscriptionId} for user {UserId}.",
                        user.StripeSubscriptionId, userId);
                }
            }

            return status;
        }

        public async Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(int userId, string priceId, string successUrl, string cancelUrl)
        {
            var user = await GetUserOrThrowAsync(userId);

            var allowedPriceId = _configuration["Stripe:PremiumPriceId"];
            if (string.IsNullOrEmpty(allowedPriceId) || priceId != allowedPriceId)
            {
                throw new InvalidOperationException("Invalid price selection.");
            }

            if (!IsAllowedRedirectUrl(successUrl) || !IsAllowedRedirectUrl(cancelUrl))
            {
                throw new InvalidOperationException("Invalid redirect URL.");
            }

            await EnsureStripeCustomerAsync(user);

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
            var session = await new SessionService().CreateAsync(options);
            return new CheckoutSessionResponse
            {
                SessionId = session.Id,
                SessionUrl = session.Url
            };
        }

        public async Task<PortalSessionResponse> CreatePortalSessionAsync(int userId, string returnUrl)
        {
            var user = await GetUserOrThrowAsync(userId);

            if (!IsAllowedRedirectUrl(returnUrl))
            {
                throw new InvalidOperationException("Invalid return URL.");
            }

            if (string.IsNullOrEmpty(user.StripeCustomerId))
            {
                throw new InvalidOperationException("No billing account exists for this user yet.");
            }

            var session = await new Stripe.BillingPortal.SessionService().CreateAsync(
                new Stripe.BillingPortal.SessionCreateOptions
                {
                    Customer = user.StripeCustomerId,
                    ReturnUrl = returnUrl
                });

            return new PortalSessionResponse { PortalUrl = session.Url };
        }

        private async Task EnsureStripeCustomerAsync(User user)
        {
            if (!string.IsNullOrEmpty(user.StripeCustomerId))
            {
                return;
            }

            var customer = await new CustomerService().CreateAsync(new CustomerCreateOptions
            {
                Email = user.Email,
                Name = $"{user.FirstName} {user.LastName}",
                Metadata = new Dictionary<string, string>
                {
                    { "userId", user.UserId.ToString() }
                }
            });
            user.StripeCustomerId = customer.Id;
            await _dbContext.SaveChangesAsync();
        }

        public async Task CancelSubscriptionAsync(int userId)
        {
            var user = await GetUserOrThrowAsync(userId, tracked: false);
            if (string.IsNullOrEmpty(user.StripeSubscriptionId))
            {
                throw new InvalidOperationException("No active subscription to cancel.");
            }

            await new Stripe.SubscriptionService().UpdateAsync(user.StripeSubscriptionId, new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = true
            });
        }

        public async Task ResumeSubscriptionAsync(int userId)
        {
            var user = await GetUserOrThrowAsync(userId, tracked: false);
            if (string.IsNullOrEmpty(user.StripeSubscriptionId))
            {
                throw new InvalidOperationException("No subscription to resume.");
            }
            await new Stripe.SubscriptionService().UpdateAsync(user.StripeSubscriptionId, new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = false
            });
        }

        private bool IsAllowedRedirectUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }
            var allowedOrigins = (_configuration["AllowedOrigins"] ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return allowedOrigins.Any(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var originUri) &&
                originUri.Scheme.Equals(uri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                originUri.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase) &&
                originUri.Port == uri.Port);
        }

        public async Task<bool> ProcessEventOnceAsync(string eventId, Func<Task> handler)
        {
            if (await _dbContext.ProcessedStripeEvents.AnyAsync(e => e.EventId == eventId))
            {
                return false;
            }
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            var marker = new ProcessedStripeEvent { EventId = eventId, ProcessedAt = DateTime.UtcNow };
            _dbContext.ProcessedStripeEvents.Add(marker);
            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {            
                _dbContext.Entry(marker).State = EntityState.Detached;
                await transaction.RollbackAsync();
                return false;
            }

            await handler();
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }

        private bool IsPremiumPrice(string? priceId)
        {
            var expected = _configuration["Stripe:PremiumPriceId"];
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(priceId))
            {
                return false;
            }
            return string.Equals(priceId, expected, StringComparison.Ordinal);
        }

        public async Task HandleSubscriptionCreatedAsync(string stripeCustomerId, string stripeSubscriptionId, string? priceId, DateTime? currentPeriodEnd)
        {
            if (!IsPremiumPrice(priceId))
            {
                _logger.LogWarning(
                    "Ignoring subscription {SubscriptionId} for customer {CustomerId}: price {PriceId} is not the configured Premium price.",
                    stripeSubscriptionId, stripeCustomerId, priceId ?? "(none)");
                return;
            }

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.StripeCustomerId == stripeCustomerId && !u.Deleted);
            if (user == null)
            {
                _logger.LogWarning("No user matches Stripe customer {CustomerId} for subscription.created.", stripeCustomerId);
                return;
            }

            user.StripeSubscriptionId = stripeSubscriptionId;
            user.Tier = UserTier.Premium;
            user.SubscriptionEndDate = currentPeriodEnd ?? DateTime.UtcNow.AddMonths(1);
            user.MonthlyMemoryCount = 0;
            user.MonthlyCountResetDate = DateTime.UtcNow;
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

            var session = await new SessionService().GetAsync(sessionId);
            if (session == null) return;

            if (!string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(session.PaymentStatus, "no_payment_required", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Checkout session {SessionId} completed with payment status {PaymentStatus}; not provisioning.",
                    sessionId, session.PaymentStatus);
                return;
            }

            var userIdStr = session.Metadata?.GetValueOrDefault("userId");
            if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId)) return;

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId && !u.Deleted);
            if (user == null) return;

            if (string.IsNullOrEmpty(user.StripeCustomerId) && !string.IsNullOrEmpty(session.CustomerId))
            {
                user.StripeCustomerId = session.CustomerId;
                await _dbContext.SaveChangesAsync();
            }
        }

        public async Task HandleSubscriptionUpdatedAsync(string subscriptionId, string status, string? priceId, DateTime? currentPeriodEnd)
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.StripeSubscriptionId == subscriptionId && !u.Deleted);
            if (user == null) return;

            switch (status)
            {
                case "active":
                case "trialing":
                    if (!IsPremiumPrice(priceId))
                    {
                        _logger.LogWarning("Subscription {SubscriptionId} is {Status} on non-Premium price {PriceId}; downgrading.",
                            subscriptionId, status, priceId ?? "(none)");
                        user.Tier = UserTier.Free;
                        user.SubscriptionEndDate = null;
                        break;
                    }
                    user.Tier = UserTier.Premium;
                    user.SubscriptionEndDate = currentPeriodEnd ?? DateTime.UtcNow.AddMonths(1);
                    break;
                case "past_due":
                    _logger.LogInformation("Subscription {SubscriptionId} is past_due; retaining tier until the period lapses.", subscriptionId);
                    if (currentPeriodEnd != null)
                    {
                        user.SubscriptionEndDate = currentPeriodEnd;
                    }
                    break;
                case "canceled":
                case "unpaid":
                case "incomplete_expired":
                    user.Tier = UserTier.Free;
                    user.SubscriptionEndDate = null;
                    break;
                case "incomplete":
                case "paused":
                    user.Tier = UserTier.Free;
                    user.SubscriptionEndDate = null;
                    break;

                default:
                    _logger.LogWarning("Unhandled Stripe subscription status {Status} for {SubscriptionId}.", status, subscriptionId);
                    break;
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

        public Task HandlePaymentFailedAsync(string customerId)
        {
            _logger.LogWarning("Invoice payment failed for Stripe customer {CustomerId}; awaiting subscription status change.", customerId);
            return Task.CompletedTask;
        }
    }
}
