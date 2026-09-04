using Application.DTOs;
using FluentValidation;

namespace Application.Validators
{
    public static class MusicMoods
    {
        public const string Default = "warm";
        public static readonly string[] Allowed = { "warm", "melancholy", "hopeful", "playful" };

        public static bool IsAllowed(string? mood) =>
            !string.IsNullOrWhiteSpace(mood) && Allowed.Contains(mood.Trim().ToLowerInvariant());
    }

    public class CreateVideoRequestValidator : AbstractValidator<CreateVideoRequest>
    {
        public CreateVideoRequestValidator()
        {
            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Title is required.")
                .MaximumLength(120).WithMessage("Title must be 120 characters or fewer.");

            RuleFor(x => x.StoryText)
                .NotEmpty().WithMessage("Please describe your memory.")
                .MinimumLength(20).WithMessage("Please write at least 20 characters so we have something to work with.")
                .MaximumLength(4000).WithMessage("Your story must be 4000 characters or fewer.");

            RuleFor(x => x.MusicMood)
                .Must(MusicMoods.IsAllowed)
                .WithMessage($"Music mood must be one of: {string.Join(", ", MusicMoods.Allowed)}.")
                .When(x => !string.IsNullOrEmpty(x.MusicMood));
        }
    }

    public class UpdateVideoRequestValidator : AbstractValidator<UpdateVideoRequest>
    {
        public UpdateVideoRequestValidator()
        {
            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Title is required.")
                .MaximumLength(120).WithMessage("Title must be 120 characters or fewer.");
        }
    }

    public class CreateShareLinkRequestValidator : AbstractValidator<CreateShareLinkRequest>
    {
        public CreateShareLinkRequestValidator()
        {
            RuleFor(x => x.ExpiresInDays)
                .InclusiveBetween(1, 365).WithMessage("Expiry must be between 1 and 365 days.")
                .When(x => x.ExpiresInDays.HasValue);

            RuleFor(x => x.Label)
                .MaximumLength(60).WithMessage("Label must be 60 characters or fewer.")
                .When(x => !string.IsNullOrEmpty(x.Label));
        }
    }
}
