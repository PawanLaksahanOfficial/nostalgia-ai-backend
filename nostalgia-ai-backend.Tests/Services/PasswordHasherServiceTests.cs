using Infrastructure.Services;
using Xunit;

namespace nostalgia_ai_backend.Tests.Services
{
    public class PasswordHasherServiceTests
    {
        private readonly PasswordHasherService _hasher = new();

        [Fact]
        public void Verify_succeeds_for_the_password_that_was_hashed()
        {
            var hash = _hasher.Hash("CorrectHorseBatteryStaple1");
            Assert.True(_hasher.Verify("CorrectHorseBatteryStaple1", hash));
        }

        [Fact]
        public void Verify_fails_for_a_different_password()
        {
            var hash = _hasher.Hash("CorrectHorseBatteryStaple1");
            Assert.False(_hasher.Verify("SomeOtherPassword1", hash));
        }

        [Fact]
        public void Hash_never_stores_the_plaintext_password()
        {
            const string password = "CorrectHorseBatteryStaple1";
            var hash = _hasher.Hash(password);
            Assert.DoesNotContain(password, hash);
        }

        [Fact]
        public void Hash_is_salted_so_the_same_password_hashes_differently_each_time()
        {
            var hash1 = _hasher.Hash("CorrectHorseBatteryStaple1");
            var hash2 = _hasher.Hash("CorrectHorseBatteryStaple1");
            Assert.NotEqual(hash1, hash2);
            Assert.True(_hasher.Verify("CorrectHorseBatteryStaple1", hash1));
            Assert.True(_hasher.Verify("CorrectHorseBatteryStaple1", hash2));
        }
    }
}
