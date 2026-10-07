using System.Net;
using Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace nostalgia_ai_backend.Tests.Services
{
    public class AbuseProtectionTests
    {
        [Theory]
        [InlineData("John.Smith@Gmail.com", "johnsmith@gmail.com")]
        [InlineData("j.o.h.n.smith+promo@gmail.com", "johnsmith@gmail.com")]
        [InlineData("johnsmith@googlemail.com", "johnsmith@gmail.com")]
        [InlineData("  jane+news@outlook.com ", "jane@outlook.com")]
        [InlineData("jane.doe@outlook.com", "jane.doe@outlook.com")]
        public void Aliases_of_one_inbox_share_a_canonical_email(string email, string expected)
        {
            Assert.Equal(expected, EmailCanonicalizer.Canonicalize(email));
        }

        [Theory]
        [InlineData("someone@mailinator.com", true)]
        [InlineData("someone@inbox.mailinator.com", true)]
        [InlineData("Someone@MAILINATOR.COM", true)]
        [InlineData("someone@gmail.com", false)]
        [InlineData("not-an-email", false)]
        public void Recognises_throwaway_inboxes_including_their_subdomains(string email, bool expected)
        {
            var domains = new DisposableEmailDomains(new[] { "mailinator.com", "# comment", "" });

            Assert.Equal(expected, domains.IsDisposable(email));
        }

        [Fact]
        public void The_bundled_throwaway_list_loads()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "nostalgia-ai-backend", "assets", "disposable-email-domains.txt");
            var domains = DisposableEmailDomains.FromFile(path, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

            Assert.True(domains.Count > 1000);
            Assert.True(domains.IsDisposable("someone@mailinator.com"));
            Assert.False(domains.IsDisposable("someone@gmail.com"));
        }

        private static IpAddressHasher Hasher(string secret = "test-secret") =>
            new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Abuse:IpHashSecret"] = secret
            }).Build());

        [Fact]
        public void Hashes_public_addresses_consistently_without_storing_them()
        {
            var hash = Hasher().Hash(IPAddress.Parse("203.0.113.7"));

            Assert.NotNull(hash);
            Assert.Equal(64, hash!.Length);
            Assert.DoesNotContain("203.0.113.7", hash);
            Assert.Equal(hash, Hasher().Hash(IPAddress.Parse("::ffff:203.0.113.7")));
            Assert.NotEqual(hash, Hasher("another-secret").Hash(IPAddress.Parse("203.0.113.7")));
        }

        [Fact]
        public void Treats_addresses_in_one_ipv6_slash_64_as_one_network()
        {
            var hasher = Hasher();

            Assert.Equal(
                hasher.Hash(IPAddress.Parse("2001:db8:1234:5678::1")),
                hasher.Hash(IPAddress.Parse("2001:db8:1234:5678:abcd:ef01:2345:6789")));
            Assert.NotEqual(
                hasher.Hash(IPAddress.Parse("2001:db8:1234:5678::1")),
                hasher.Hash(IPAddress.Parse("2001:db8:1234:9999::1")));
        }

        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("10.4.2.1")]
        [InlineData("172.20.0.5")]
        [InlineData("192.168.1.10")]
        [InlineData("100.64.0.1")]
        [InlineData("::1")]
        [InlineData("fd00::1")]
        public void Skips_private_addresses_so_one_proxy_never_stands_in_for_everyone(string address)
        {
            Assert.Null(Hasher().Hash(IPAddress.Parse(address)));
        }
    }
}
