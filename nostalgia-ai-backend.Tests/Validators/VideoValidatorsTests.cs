using Application.DTOs;
using Application.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace nostalgia_ai_backend.Tests.Validators
{
    public class CreateVideoRequestValidatorTests
    {
        private readonly CreateVideoRequestValidator _validator = new();

        private static CreateVideoRequest Valid() => new()
        {
            Title = "Summer at the lake",
            StoryText = "We drove out to the lake every single weekend that summer.",
            MusicMood = "warm"
        };

        [Fact]
        public void Passes_for_a_well_formed_request()
        {
            _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
        }

        [Fact]
        public void Fails_when_the_title_is_missing()
        {
            var request = Valid();
            request.Title = "";

            _validator.TestValidate(request).ShouldHaveValidationErrorFor(x => x.Title);
        }

        [Fact]
        public void Fails_when_the_title_is_too_long()
        {
            var request = Valid();
            request.Title = new string('a', 121);

            _validator.TestValidate(request).ShouldHaveValidationErrorFor(x => x.Title);
        }

        [Fact]
        public void Fails_when_the_story_is_too_short_to_work_with()
        {
            var request = Valid();
            request.StoryText = "Too short.";

            _validator.TestValidate(request).ShouldHaveValidationErrorFor(x => x.StoryText);
        }

        [Fact]
        public void Fails_when_the_story_is_too_long()
        {
            var request = Valid();
            request.StoryText = new string('a', 4001);

            _validator.TestValidate(request).ShouldHaveValidationErrorFor(x => x.StoryText);
        }

        [Fact]
        public void Allows_an_absent_music_mood()
        {
            var request = Valid();
            request.MusicMood = null;

            _validator.TestValidate(request).ShouldNotHaveValidationErrorFor(x => x.MusicMood);
        }

        [Theory]
        [InlineData("warm")]
        [InlineData("melancholy")]
        [InlineData("hopeful")]
        [InlineData("playful")]
        public void Allows_every_supported_music_mood(string mood)
        {
            var request = Valid();
            request.MusicMood = mood;

            _validator.TestValidate(request).ShouldNotHaveValidationErrorFor(x => x.MusicMood);
        }

        [Fact]
        public void Rejects_an_unsupported_music_mood()
        {
            var request = Valid();
            request.MusicMood = "chaotic";

            _validator.TestValidate(request).ShouldHaveValidationErrorFor(x => x.MusicMood);
        }
    }

    public class UpdateVideoRequestValidatorTests
    {
        private readonly UpdateVideoRequestValidator _validator = new();

        [Fact]
        public void Passes_for_a_reasonable_title()
        {
            _validator.TestValidate(new UpdateVideoRequest { Title = "A new name" })
                .ShouldNotHaveAnyValidationErrors();
        }

        [Fact]
        public void Fails_for_an_empty_title()
        {
            _validator.TestValidate(new UpdateVideoRequest { Title = "  " })
                .ShouldHaveValidationErrorFor(x => x.Title);
        }
    }

    public class CreateShareLinkRequestValidatorTests
    {
        private readonly CreateShareLinkRequestValidator _validator = new();

        [Fact]
        public void Passes_when_no_expiry_is_requested()
        {
            _validator.TestValidate(new CreateShareLinkRequest())
                .ShouldNotHaveAnyValidationErrors();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(366)]
        [InlineData(-1)]
        public void Rejects_an_expiry_outside_the_allowed_range(int days)
        {
            _validator.TestValidate(new CreateShareLinkRequest { ExpiresInDays = days })
                .ShouldHaveValidationErrorFor(x => x.ExpiresInDays);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(30)]
        [InlineData(365)]
        public void Accepts_an_expiry_inside_the_allowed_range(int days)
        {
            _validator.TestValidate(new CreateShareLinkRequest { ExpiresInDays = days })
                .ShouldNotHaveValidationErrorFor(x => x.ExpiresInDays);
        }

        [Fact]
        public void Rejects_an_overlong_label()
        {
            _validator.TestValidate(new CreateShareLinkRequest { Label = new string('a', 61) })
                .ShouldHaveValidationErrorFor(x => x.Label);
        }
    }
}
