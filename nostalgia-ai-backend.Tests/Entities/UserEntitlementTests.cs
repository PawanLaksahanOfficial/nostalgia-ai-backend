using Domain.Entities;

namespace nostalgia_ai_backend.Tests.Entities
{
    public class UserEntitlementTests
    {
        private static User Premium(DateTime? subscriptionEndDate) => new()
        {
            UserId = 1,
            Tier = UserTier.Premium,
            SubscriptionEndDate = subscriptionEndDate
        };

        [Fact]
        public void Free_tier_is_never_premium()
        {
            var user = new User { Tier = UserTier.Free, SubscriptionEndDate = DateTime.UtcNow.AddYears(1) };
            Assert.False(user.IsPremiumActive());
        }

        [Fact]
        public void Premium_with_a_future_period_end_is_active()
        {
            Assert.True(Premium(DateTime.UtcNow.AddDays(10)).IsPremiumActive());
        }

        [Fact]
        public void Premium_with_no_end_date_is_treated_as_a_non_expiring_grant()
        {
            Assert.True(Premium(null).IsPremiumActive());
        }

        [Fact]
        public void Premium_stays_active_inside_the_grace_period()
        {
            // Renewal already charged but the webhook has not landed yet.
            var justLapsed = DateTime.UtcNow.Add(-User.EntitlementGracePeriod).AddHours(1);
            Assert.True(Premium(justLapsed).IsPremiumActive());
        }

        [Fact]
        public void Premium_expires_once_the_grace_period_has_passed()
        {
            // This is the case that a dropped subscription.deleted webhook used to leave
            // premium forever.
            var wellPast = DateTime.UtcNow.Add(-User.EntitlementGracePeriod).AddDays(-1);
            Assert.False(Premium(wellPast).IsPremiumActive());
        }
    }
}
