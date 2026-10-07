using System.Text.Json;
using Application.DTOs;

namespace Infrastructure.Services
{
    public static class StoryScriptParser
    {
        private const int MaxSceneLength = 60;

        public static string BuildPrompt(string title, string story, int wordBudget, int sceneCount) =>
            "Turn the memory below into a script for a short narrated video.\n" +
            "Reply with JSON only, exactly in this shape:\n" +
            "{\"narration\": \"...\", \"scenes\": [\"...\", \"...\"]}\n\n" +
            $"narration: a warm, first-person nostalgic narration of about {wordBudget} words. " +
            "Retell it in your own words with sensory detail (sounds, light, smells, textures). " +
            "Do not copy sentences from the memory. Flowing prose, no headings, lists or emojis.\n" +
            $"scenes: {sceneCount} short English stock-photo search phrases (2 to 5 words each) " +
            "describing what the viewer should see, in story order. Describe places, objects, seasons, " +
            "weather and moods. Never include people's names.\n\n" +
            $"Title: {title}\n" +
            $"Memory:\n{story}";

        public static StoryScript Parse(string? reply, int maxScenes)
        {
            var script = new StoryScript();
            if (string.IsNullOrWhiteSpace(reply))
            {
                return script;
            }

            var text = reply.Trim();
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                try
                {
                    // Models often put raw line breaks inside strings, which is invalid JSON. Between
                    // tokens a line break is just whitespace, so turning them into spaces is safe.
                    var candidate = text[start..(end + 1)].Replace("\r", " ").Replace("\n", " ");
                    using var document = JsonDocument.Parse(candidate, new JsonDocumentOptions { AllowTrailingCommas = true });
                    var root = document.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("narration", out var narration) &&
                            narration.ValueKind == JsonValueKind.String)
                        {
                            script.Narration = narration.GetString()!.Trim();
                        }
                        if (root.TryGetProperty("scenes", out var scenes) &&
                            scenes.ValueKind == JsonValueKind.Array)
                        {
                            script.Scenes = CleanScenes(
                                scenes.EnumerateArray()
                                    .Where(s => s.ValueKind == JsonValueKind.String)
                                    .Select(s => s.GetString()!),
                                maxScenes);
                        }
                        return script;
                    }
                }
                catch (JsonException)
                {
                    // Fall through: treat the reply as prose if it doesn't look like broken JSON.
                }
            }

            // Plain prose is a usable narration; a half-written JSON object is not.
            if (!text.StartsWith('{') && !text.StartsWith("```", StringComparison.Ordinal))
            {
                script.Narration = text;
            }
            return script;
        }

        public static List<string> FallbackScenes(string title, string? musicMood, int maxScenes)
        {
            var moodScene = (musicMood ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "melancholy" => "rain on window",
                "hopeful" => "sunrise over hills",
                "playful" => "children playing outdoors",
                _ => "golden hour countryside"
            };
            return CleanScenes(new[] { title, moodScene, "vintage family photo album", "old street nostalgia" }, maxScenes);
        }

        private static List<string> CleanScenes(IEnumerable<string> scenes, int maxScenes) =>
            scenes
                .Select(s => s.Trim().Trim('"', '.', ','))
                .Where(s => s.Length is > 0 and <= MaxSceneLength)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(0, maxScenes))
                .ToList();
    }
}
