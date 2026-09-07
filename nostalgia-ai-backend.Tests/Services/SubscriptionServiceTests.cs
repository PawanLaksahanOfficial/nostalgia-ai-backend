using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace nostalgia_ai_backend.Tests.Services
{
    /// <summary>
    /// Backed by a real relational database (SQLite in-memory) because the behaviour under test —
    /// guarded atomic UPDATEs and transaction rollback — does not exist in the InMemory provider.
    /// </summary>
    public class SubscriptionServiceTests : IDisposable
    {
        private const int FreeMonthlyLimit = 3;

        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<EFDbContext> _options;

        public SubscriptionServiceTests()
        {
            // The database lives as long as the connection does.
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _options = new DbContextOptionsBuilder<EFDbContext>()
                .UseSqlite(_connection)
                .Options;

            using var context = new EFDbContext(_options);
            context.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();

        private EFDbContext NewContext() => new(_options);

        private static IConfiguration Config() =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TierLimits:Free:MonthlyMemories"] = FreeMonthlyLimit.ToString(),
                    ["TierLimits:Premium:MonthlyMemories"] = "100",
                    ["Stripe:PremiumPriceId"] = "price_premium",
                    ["AllowedOrigins"] = "http://localhost:3000",
                })
                .Build();

        private static ISubscriptionService NewService(EFDbContext context) =>
            new SubscriptionService(context, Config(), NullLogger<SubscriptionService>.Instance);

        private async Task<int> SeedFreeUserAsync(int usedThisMonth = 0)
        {
            await using var context = NewContext();
            var user = new User
            {
                FirstName = "Jane",
                LastName = "Doe",
                Email = $"jane{Guid.NewGuid():N}@example.com",
                Active = true,
                Tier = UserTier.Free,
                MonthlyMemoryCount = usedThisMonth,
                MonthlyCountResetDate = DateTime.UtcNow,
            };
            context.Users.Add(user);
            await context.SaveChangesAsync();
            return user.UserId;
        }

        private async Task<int> GetUsageAsync(int userId)
        {
            await using var context = NewContext();
            return (await context.Users.SingleAsync(u => u.UserId == userId)).MonthlyMemoryCount;
        }

        // -----------------------------------------------------------------
        // Quota
        // -----------------------------------------------------------------

        [Fact]
        public async Task Consuming_quota_below_the_limit_succeeds_and_increments_once()
        {
            var userId = await SeedFreeUserAsync();
            await using var context = NewContext();

            Assert.True(await NewService(context).TryConsumeQuotaAsync(userId));
            Assert.Equal(1, await GetUsageAsync(userId));
        }

        [Fact]
        public async Task Consuming_quota_at_the_limit_is_refused_and_does_not_increment()
        {
            var userId = await SeedFreeUserAsync(usedThisMonth: FreeMonthlyLimit);
            await using var context = NewContext();

            Assert.False(await NewService(context).TryConsumeQuotaAsync(userId));
            Assert.Equal(FreeMonthlyLimit, await GetUsageAsync(userId));
        }

        [Fact]
        public async Task Concurrent_consumers_cannot_exceed_the_limit()
        {
            // The bug this guards: a read-then-write quota check let every concurrent request
            // pass at the same count, and the increments overwrote each other.
            var userId = await SeedFreeUserAsync();

            var attempts = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
            {
                await using var context = NewContext();
                return await NewService(context).TryConsumeQuotaAsync(userId);
            }));

            Assert.Equal(FreeMonthlyLimit, attempts.Count(granted => granted));
            Assert.Equal(FreeMonthlyLimit, await GetUsageAsync(userId));
        }

        [Fact]
        public async Task Usage_resets_when_the_stored_window_is_from_a_previous_month()
        {
            var userId = await SeedFreeUserAsync(usedThisMonth: FreeMonthlyLimit);
            await using (var seed = NewContext())
            {
                var user = await seed.Users.SingleAsync(u => u.UserId == userId);
                user.MonthlyCountResetDate = DateTime.UtcNow.AddMonths(-2);
                await seed.SaveChangesAsync();
            }

            await using var context = NewContext();
            Assert.True(await NewService(context).TryConsumeQuotaAsync(userId));

            // Reset to zero, then the single consumption applied.
            Assert.Equal(1, await GetUsageAsync(userId));
        }

        [Fact]
        public async Task Refunding_returns_a_credit_but_never_goes_below_zero()
        {
            var userId = await SeedFreeUserAsync(usedThisMonth: 1);
            await using var context = NewContext();
            var service = NewService(context);

            await service.RefundQuotaAsync(userId);
            Assert.Equal(0, await GetUsageAsync(userId));

            await service.RefundQuotaAsync(userId);
            Assert.Equal(0, await GetUsageAsync(userId));
        }

        [Fact]
        public async Task Reading_the_quota_does_not_write_to_the_database()
        {
            var userId = await SeedFreeUserAsync(usedThisMonth: 2);
            await using (var stale = NewContext())
            {
                var user = await stale.Users.SingleAsync(u => u.UserId == userId);
                user.MonthlyCountResetDate = DateTime.UtcNow.AddMonths(-2);
                await stale.SaveChangesAsync();
            }

            await using var context = NewContext();
            var quota = await NewService(context).GetUsageQuotaAsync(userId);

            // Reported as a fresh window...
            Assert.Equal(0, quota.MonthlyMemoriesUsed);
            Assert.Equal(FreeMonthlyLimit, quota.MonthlyMemoriesLimit);
            // ...but the stored counter is untouched, because a GET must not mutate state.
            Assert.Equal(2, await GetUsageAsync(userId));
        }

        // -----------------------------------------------------------------
        // Webhook idempotency
        // -----------------------------------------------------------------

        [Fact]
        public async Task An_event_is_processed_only_once()
        {
            await using var context = NewContext();
            var service = NewService(context);
            var handlerRuns = 0;

            Assert.True(await service.ProcessEventOnceAsync("evt_1", () => { handlerRuns++; return Task.CompletedTask; }));
            Assert.False(await service.ProcessEventOnceAsync("evt_1", () => { handlerRuns++; return Task.CompletedTask; }));

            Assert.Equal(1, handlerRuns);
        }

        [Fact]
        public async Task A_failed_handler_leaves_the_event_unprocessed_so_the_retry_can_succeed()
        {
            // The bug this guards: the event was marked processed *before* the handler ran, so a
            // handler failure made Stripe's retry a silent no-op and the customer was never upgraded.
            await using var context = NewContext();
            var service = NewService(context);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ProcessEventOnceAsync("evt_2", () => throw new InvalidOperationException("boom")));

            await using (var verify = NewContext())
            {
                Assert.False(await verify.ProcessedStripeEvents.AnyAsync(e => e.EventId == "evt_2"));
            }

            var retryRan = false;
            await using var retryContext = NewContext();
            Assert.True(await NewService(retryContext)
                .ProcessEventOnceAsync("evt_2", () => { retryRan = true; return Task.CompletedTask; }));
            Assert.True(retryRan);
        }

        [Fact]
        public async Task A_failed_handler_rolls_back_the_writes_it_already_made()
        {
            var userId = await SeedFreeUserAsync();
            await using var context = NewContext();
            var service = NewService(context);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ProcessEventOnceAsync("evt_3", async () =>
                {
                    var user = await context.Users.SingleAsync(u => u.UserId == userId);
                    user.Tier = UserTier.Premium;
                    await context.SaveChangesAsync();
                    throw new InvalidOperationException("failed after a partial write");
                }));

            await using var verify = NewContext();
            var persisted = await verify.Users.SingleAsync(u => u.UserId == userId);
            Assert.Equal(UserTier.Free, persisted.Tier);
        }

        // -----------------------------------------------------------------
        // Price verification
        // -----------------------------------------------------------------

        [Fact]
        public async Task A_subscription_on_an_unknown_price_does_not_grant_premium()
        {
            var userId = await SeedFreeUserAsync();
            await using (var seed = NewContext())
            {
                var user = await seed.Users.SingleAsync(u => u.UserId == userId);
                user.StripeCustomerId = "cus_123";
                await seed.SaveChangesAsync();
            }

            await using var context = NewContext();
            await NewService(context).HandleSubscriptionCreatedAsync(
                "cus_123", "sub_123", "price_something_else", DateTime.UtcNow.AddMonths(1));

            await using var verify = NewContext();
            Assert.Equal(UserTier.Free, (await verify.Users.SingleAsync(u => u.UserId == userId)).Tier);
        }

        [Fact]
        public async Task A_subscription_on_the_configured_price_grants_premium()
        {
            var userId = await SeedFreeUserAsync();
            await using (var seed = NewContext())
            {
                var user = await seed.Users.SingleAsync(u => u.UserId == userId);
                user.StripeCustomerId = "cus_123";
                await seed.SaveChangesAsync();
            }

            await using var context = NewContext();
            await NewService(context).HandleSubscriptionCreatedAsync(
                "cus_123", "sub_123", "price_premium", DateTime.UtcNow.AddMonths(1));

            await using var verify = NewContext();
            var upgraded = await verify.Users.SingleAsync(u => u.UserId == userId);
            Assert.Equal(UserTier.Premium, upgraded.Tier);
            Assert.True(upgraded.IsPremiumActive());
        }

        [Fact]
        public async Task A_failed_invoice_payment_does_not_downgrade_on_its_own()
        {
            // invoice.payment_failed fires on every dunning retry; downgrading here stripped
            // Premium from customers whose card succeeded on a later attempt.
            var userId = await SeedFreeUserAsync();
            await using (var seed = NewContext())
            {
                var user = await seed.Users.SingleAsync(u => u.UserId == userId);
                user.StripeCustomerId = "cus_123";
                user.Tier = UserTier.Premium;
                user.SubscriptionEndDate = DateTime.UtcNow.AddDays(20);
                await seed.SaveChangesAsync();
            }

            await using var context = NewContext();
            await NewService(context).HandlePaymentFailedAsync("cus_123");

            await using var verify = NewContext();
            Assert.Equal(UserTier.Premium, (await verify.Users.SingleAsync(u => u.UserId == userId)).Tier);
        }

        [Fact]
        public async Task A_cancelled_subscription_downgrades_the_user()
        {
            var userId = await SeedFreeUserAsync();
            await using (var seed = NewContext())
            {
                var user = await seed.Users.SingleAsync(u => u.UserId == userId);
                user.StripeSubscriptionId = "sub_123";
                user.Tier = UserTier.Premium;
                user.SubscriptionEndDate = DateTime.UtcNow.AddDays(20);
                await seed.SaveChangesAsync();
            }

            await using var context = NewContext();
            await NewService(context).HandleSubscriptionUpdatedAsync("sub_123", "canceled", "price_premium", null);

            await using var verify = NewContext();
            var downgraded = await verify.Users.SingleAsync(u => u.UserId == userId);
            Assert.Equal(UserTier.Free, downgraded.Tier);
            Assert.False(downgraded.IsPremiumActive());
        }
    }
}
