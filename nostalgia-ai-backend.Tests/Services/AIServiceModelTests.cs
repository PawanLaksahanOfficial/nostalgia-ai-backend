using Infrastructure.AI;
using Xunit;

namespace nostalgia_ai_backend.Tests.Services
{
    public class AIServiceModelTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" , ")]
        public void Falls_back_to_the_built_in_free_models_when_nothing_is_configured(string? configured)
        {
            var models = AIService.ParseModels(configured);

            Assert.NotEmpty(models);
            Assert.All(models, model => Assert.EndsWith(":free", model));
        }

        [Fact]
        public void Splits_trims_and_deduplicates_a_comma_separated_list()
        {
            var models = AIService.ParseModels(" a/one:free , b/two:free,a/one:free ");

            Assert.Equal(new[] { "a/one:free", "b/two:free" }, models);
        }

        [Fact]
        public void Keeps_a_single_configured_model()
        {
            Assert.Equal(new[] { "x/only" }, AIService.ParseModels("x/only"));
        }
    }
}
