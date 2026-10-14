using Application.DTOs;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace nostalgia_ai_backend.Tests.Services
{
    public class RefreshTokenServiceTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<EFDbContext> _options;
        private readonly IConfiguration _configuration =
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:RefreshTokenDays"] = "30"
            }).Build();

        public RefreshTokenServiceTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _options = new DbContextOptionsBuilder<EFDbContext>().UseSqlite(_connection).Options;
            using var context = new EFDbContext(_options);
            context.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();

        // A fresh context per call, like separate HTTP requests.
        private RefreshTokenService Service() =>
            new(new EFDbContext(_options), _configuration, NullLogger<RefreshTokenService>.Instance);

        private async Task<int> RegisterAsync(string email = "jane@example.com")
        {
            await using var context = new EFDbContext(_options);
            var request = new RegisterRequest { FirstName = "Jane", LastName = "Doe", Email = email, Password = "secret123" };
            return (await new EfUserRepository(context).RegisterUserAsync(request, "hash", null))!.UserId;
        }

        private async Task ExpireAsync(string token)
        {
            await using var context = new EFDbContext(_options);
            var hash = RefreshTokenService.Hash(token);
            var past = DateTime.UtcNow.AddMinutes(-1);
            await context.RefreshTokens
                .Where(t => t.TokenHash == hash)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, past));
        }

        [Fact]
        public async Task Rotating_a_live_token_returns_a_successor_for_the_same_user()
        {
            var userId = await RegisterAsync();
            var first = await Service().IssueAsync(userId);

            var rotated = await Service().RotateAsync(first);

            Assert.NotNull(rotated);
            Assert.Equal(userId, rotated!.UserId);
            Assert.NotEqual(first, rotated.RefreshToken);
            Assert.NotNull(await Service().RotateAsync(rotated.RefreshToken));
        }

        [Fact]
        public async Task A_rotated_token_cannot_be_used_again()
        {
            var userId = await RegisterAsync();
            var first = await Service().IssueAsync(userId);
            await Service().RotateAsync(first);

            Assert.Null(await Service().RotateAsync(first));
        }

        [Fact]
        public async Task Reusing_a_rotated_token_ends_the_whole_session()
        {
            var userId = await RegisterAsync();
            var first = await Service().IssueAsync(userId);
            var second = (await Service().RotateAsync(first))!.RefreshToken;

            // An attacker replays the stolen first token after the real user has moved on to the second.
            Assert.Null(await Service().RotateAsync(first));

            Assert.Null(await Service().RotateAsync(second));
        }

        [Fact]
        public async Task Expired_and_unknown_tokens_are_rejected()
        {
            var userId = await RegisterAsync();
            var token = await Service().IssueAsync(userId);
            await ExpireAsync(token);

            Assert.Null(await Service().RotateAsync(token));
            Assert.Null(await Service().RotateAsync("made-up-token"));
        }

        [Fact]
        public async Task Signing_out_ends_only_that_session()
        {
            var userId = await RegisterAsync();
            var laptop = await Service().IssueAsync(userId);
            var phone = await Service().IssueAsync(userId);

            await Service().RevokeAsync(laptop);

            Assert.Null(await Service().RotateAsync(laptop));
            Assert.NotNull(await Service().RotateAsync(phone));
        }

        [Fact]
        public async Task Revoking_all_ends_every_session_of_that_user_only()
        {
            var userId = await RegisterAsync();
            var otherUserId = await RegisterAsync("someone.else@example.com");
            var laptop = await Service().IssueAsync(userId);
            var phone = await Service().IssueAsync(userId);
            var other = await Service().IssueAsync(otherUserId);

            await Service().RevokeAllAsync(userId);

            Assert.Null(await Service().RotateAsync(laptop));
            Assert.Null(await Service().RotateAsync(phone));
            Assert.NotNull(await Service().RotateAsync(other));
        }

        [Fact]
        public async Task Signing_out_with_an_unknown_token_does_nothing()
        {
            var userId = await RegisterAsync();
            var token = await Service().IssueAsync(userId);

            await Service().RevokeAsync("made-up-token");

            Assert.NotNull(await Service().RotateAsync(token));
        }

        [Fact]
        public async Task Only_a_hash_of_the_token_is_stored()
        {
            var userId = await RegisterAsync();
            var token = await Service().IssueAsync(userId);

            await using var context = new EFDbContext(_options);
            var stored = await context.RefreshTokens.AsNoTracking().SingleAsync();
            Assert.Equal(43, token.Length);
            Assert.NotEqual(token, stored.TokenHash);
            Assert.Equal(RefreshTokenService.Hash(token), stored.TokenHash);
            Assert.InRange(stored.ExpiresAt, DateTime.UtcNow.AddDays(29), DateTime.UtcNow.AddDays(31));
        }

        [Fact]
        public async Task Issuing_a_token_clears_out_the_users_expired_ones()
        {
            var userId = await RegisterAsync();
            var old = await Service().IssueAsync(userId);
            await ExpireAsync(old);

            await Service().IssueAsync(userId);

            await using var context = new EFDbContext(_options);
            Assert.Equal(1, await context.RefreshTokens.CountAsync());
        }
    }
}
