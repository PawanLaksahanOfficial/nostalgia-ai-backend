using Application.DTOs;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace nostalgia_ai_backend.Tests.Repositories
{
    public class EfUserRepositoryVerificationTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<EFDbContext> _options;

        public EfUserRepositoryVerificationTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _options = new DbContextOptionsBuilder<EFDbContext>().UseSqlite(_connection).Options;
            using var context = new EFDbContext(_options);
            context.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();

        private async Task<User> RegisterAsync(string email = "Jane.Doe+news@gmail.com", string? ipHash = "network-a")
        {
            await using var context = new EFDbContext(_options);
            var request = new RegisterRequest { FirstName = "Jane", LastName = "Doe", Email = email, Password = "secret123" };
            return (await new EfUserRepository(context).RegisterUserAsync(request, "hash", ipHash))!;
        }

        private async Task AddTokenAsync(int userId, string token, DateTime expiresAt)
        {
            await using var context = new EFDbContext(_options);
            await new EfUserRepository(context).CreateEmailVerificationTokenAsync(new EmailVerificationToken
            {
                UserId = userId,
                Token = token,
                ExpiresAt = expiresAt
            });
        }

        private async Task<User> ReloadAsync(int userId)
        {
            await using var context = new EFDbContext(_options);
            return await context.Users.AsNoTracking().SingleAsync(u => u.UserId == userId);
        }

        [Fact]
        public async Task New_password_accounts_start_unverified_with_a_canonical_email()
        {
            var user = await ReloadAsync((await RegisterAsync()).UserId);

            Assert.False(user.EmailVerified);
            Assert.Equal("jane.doe+news@gmail.com", user.Email);
            Assert.Equal("janedoe@gmail.com", user.CanonicalEmail);
            Assert.Equal("network-a", user.SignupIpHash);
        }

        [Fact]
        public async Task Finds_an_account_by_any_alias_of_its_inbox()
        {
            var user = await RegisterAsync();
            await using var context = new EFDbContext(_options);

            var match = await new EfUserRepository(context).GetUserByCanonicalEmailAsync("JANEDOE@googlemail.com");

            Assert.Equal(user.UserId, match?.UserId);
        }

        [Fact]
        public async Task A_valid_link_verifies_the_email_and_opening_it_again_still_succeeds()
        {
            var user = await RegisterAsync();
            await AddTokenAsync(user.UserId, "good-token", DateTime.UtcNow.AddHours(24));
            await using var context = new EFDbContext(_options);
            var repository = new EfUserRepository(context);

            Assert.True(await repository.VerifyEmailAsync("JANE.DOE+news@gmail.com", "good-token"));
            Assert.True(await repository.VerifyEmailAsync("jane.doe+news@gmail.com", "good-token"));
            Assert.True((await ReloadAsync(user.UserId)).EmailVerified);
        }

        [Fact]
        public async Task Expired_or_wrong_links_leave_the_email_unverified()
        {
            var user = await RegisterAsync();
            await AddTokenAsync(user.UserId, "old-token", DateTime.UtcNow.AddMinutes(-1));
            await using var context = new EFDbContext(_options);
            var repository = new EfUserRepository(context);

            Assert.False(await repository.VerifyEmailAsync("jane.doe+news@gmail.com", "old-token"));
            Assert.False(await repository.VerifyEmailAsync("jane.doe+news@gmail.com", "made-up-token"));
            Assert.False(await repository.VerifyEmailAsync("someone.else@gmail.com", "old-token"));
            Assert.False((await ReloadAsync(user.UserId)).EmailVerified);
        }

        [Fact]
        public async Task Counts_sign_ups_from_one_network_in_the_window()
        {
            await RegisterAsync("a@example.com", "network-a");
            await RegisterAsync("b@example.com", "network-a");
            await RegisterAsync("c@example.com", "network-b");
            await RegisterAsync("d@example.com", null);
            await using var context = new EFDbContext(_options);
            var repository = new EfUserRepository(context);

            Assert.Equal(2, await repository.CountUsersCreatedFromIpSinceAsync("network-a", DateTime.UtcNow.AddDays(-1)));
            Assert.Equal(0, await repository.CountUsersCreatedFromIpSinceAsync("network-a", DateTime.UtcNow.AddMinutes(1)));
        }

        [Fact]
        public async Task Social_sign_ups_count_as_verified()
        {
            await using var context = new EFDbContext(_options);
            var user = await new EfUserRepository(context).CreateSocialLoginUserAsync(
                new UserModel { FirstName = "Ravi", LastName = "Perera", Email = "Ravi.Perera@gmail.com" });

            var saved = await ReloadAsync(user!.UserId);
            Assert.True(saved.EmailVerified);
            Assert.Equal("raviperera@gmail.com", saved.CanonicalEmail);
        }
    }
}
