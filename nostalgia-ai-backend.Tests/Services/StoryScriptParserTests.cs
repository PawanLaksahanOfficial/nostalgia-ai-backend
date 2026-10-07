using Infrastructure.Services;
using Xunit;

namespace nostalgia_ai_backend.Tests.Services
{
    public class StoryScriptParserTests
    {
        [Fact]
        public void Reads_narration_and_scenes_from_a_json_reply()
        {
            var script = StoryScriptParser.Parse(
                "{\"narration\": \"I remember the smell of rain.\", \"scenes\": [\"rain on window\", \"old kitchen\"]}", 4);

            Assert.Equal("I remember the smell of rain.", script.Narration);
            Assert.Equal(new[] { "rain on window", "old kitchen" }, script.Scenes);
        }

        [Fact]
        public void Accepts_json_wrapped_in_a_markdown_fence_with_raw_line_breaks()
        {
            var reply = "```json\n{\n  \"narration\": \"First line.\nSecond line.\",\n  \"scenes\": [\"beach at dusk\"],\n}\n```";

            var script = StoryScriptParser.Parse(reply, 4);

            Assert.Equal("First line. Second line.", script.Narration);
            Assert.Equal(new[] { "beach at dusk" }, script.Scenes);
        }

        [Fact]
        public void Treats_a_plain_prose_reply_as_the_narration()
        {
            var script = StoryScriptParser.Parse("Those summers felt endless.", 4);

            Assert.Equal("Those summers felt endless.", script.Narration);
            Assert.Empty(script.Scenes);
        }

        [Fact]
        public void Never_narrates_a_broken_json_reply()
        {
            var script = StoryScriptParser.Parse("{\"narration\": \"cut off mid", 4);

            Assert.Equal(string.Empty, script.Narration);
        }

        [Fact]
        public void Caps_trims_and_deduplicates_scenes()
        {
            var script = StoryScriptParser.Parse(
                "{\"narration\": \"x\", \"scenes\": [\" a \", \"A\", \"\", \"b\", \"c\", \"d\", \"" + new string('z', 80) + "\"]}", 3);

            Assert.Equal(new[] { "a", "b", "c" }, script.Scenes);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("   ")]
        public void Returns_an_empty_script_for_an_empty_reply(string? reply)
        {
            var script = StoryScriptParser.Parse(reply, 4);

            Assert.Equal(string.Empty, script.Narration);
            Assert.Empty(script.Scenes);
        }

        [Fact]
        public void Fallback_scenes_start_with_the_title_and_follow_the_music_mood()
        {
            var scenes = StoryScriptParser.FallbackScenes("Grandma's kitchen", "melancholy", 4);

            Assert.Equal("Grandma's kitchen", scenes[0]);
            Assert.Contains("rain on window", scenes);
            Assert.Equal(4, scenes.Count);
        }

        [Fact]
        public void The_prompt_asks_for_json_with_the_requested_scene_count_and_word_budget()
        {
            var prompt = StoryScriptParser.BuildPrompt("Title", "Story", wordBudget: 70, sceneCount: 4);

            Assert.Contains("\"narration\"", prompt);
            Assert.Contains("about 70 words", prompt);
            Assert.Contains("scenes: 4 short", prompt);
            Assert.Contains("Story", prompt);
        }
    }
}
