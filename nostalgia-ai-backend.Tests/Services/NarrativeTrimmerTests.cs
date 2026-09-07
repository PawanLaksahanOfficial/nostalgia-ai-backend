using Infrastructure.Services;
using Xunit;

namespace nostalgia_ai_backend.Tests.Services
{
    public class NarrativeTrimmerTests
    {
        [Fact]
        public void Short_text_is_returned_unchanged()
        {
            const string text = "A short and complete memory.";
            Assert.Equal(text, NarrativeTrimmer.Trim(text, 50));
        }

        [Fact]
        public void Empty_input_returns_empty()
        {
            Assert.Equal(string.Empty, NarrativeTrimmer.Trim(null, 50));
            Assert.Equal(string.Empty, NarrativeTrimmer.Trim("   ", 50));
        }

        [Fact]
        public void Trims_on_a_sentence_boundary()
        {
            const string text = "First sentence here. Second sentence here. Third sentence here.";

            // Each sentence is 3 words, so a budget of 8 fits two but not all three.
            var result = NarrativeTrimmer.Trim(text, 8);

            Assert.Equal("First sentence here. Second sentence here.", result);
        }

        [Fact]
        public void Never_exceeds_the_word_budget()
        {
            const string text = "One two three. Four five six. Seven eight nine. Ten eleven twelve.";

            foreach (var budget in new[] { 3, 6, 9, 12 })
            {
                var words = NarrativeTrimmer.Trim(text, budget)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

                Assert.True(words <= budget, $"Budget {budget} produced {words} words.");
            }
        }

        [Fact]
        public void Falls_back_to_a_word_cut_when_the_first_sentence_is_too_long()
        {
            const string text = "This single opening sentence runs well past any reasonable budget we set.";

            var result = NarrativeTrimmer.Trim(text, 4);

            Assert.Equal("This single opening sentence", result);
        }

        [Fact]
        public void Collapses_whitespace()
        {
            Assert.Equal("A tidy line.", NarrativeTrimmer.Trim("  A   tidy\n\nline.  ", 50));
        }

        [Fact]
        public void Word_budget_grows_with_the_allowed_duration()
        {
            var shortBudget = NarrativeTrimmer.WordBudgetForSeconds(30);
            var longBudget = NarrativeTrimmer.WordBudgetForSeconds(60);

            Assert.True(longBudget > shortBudget);
            // 30s minus a 2s tail, at ~2.5 words/second.
            Assert.Equal(70, shortBudget);
        }

        [Fact]
        public void Word_budget_never_collapses_to_nothing()
        {
            Assert.True(NarrativeTrimmer.WordBudgetForSeconds(0) >= 10);
            Assert.True(NarrativeTrimmer.WordBudgetForSeconds(1) >= 10);
        }
    }
}
