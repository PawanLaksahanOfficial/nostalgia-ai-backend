using Infrastructure.Services;
using Xunit;

namespace nostalgia_ai_backend.Tests.Services
{
    public class ShareTokenGeneratorTests
    {
        [Fact]
        public void Token_is_twenty_two_characters()
        {
            Assert.Equal(22, ShareTokenGenerator.Create().Length);
        }

        [Fact]
        public void Token_only_uses_url_safe_characters()
        {
            for (var i = 0; i < 200; i++)
            {
                var token = ShareTokenGenerator.Create();
                Assert.DoesNotContain('+', token);
                Assert.DoesNotContain('/', token);
                Assert.DoesNotContain('=', token);
                Assert.All(token, c =>
                    Assert.True(char.IsLetterOrDigit(c) || c == '-' || c == '_', $"Unexpected character '{c}'."));
            }
        }

        [Fact]
        public void Tokens_do_not_repeat()
        {
            var tokens = new HashSet<string>();
            for (var i = 0; i < 10_000; i++)
            {
                Assert.True(tokens.Add(ShareTokenGenerator.Create()), "Generated a duplicate share token.");
            }
        }
    }
}
