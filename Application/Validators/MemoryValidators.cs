using Application.DTOs;
using FluentValidation;

namespace Application.Validators
{
    public class GenerateRequestValidator : AbstractValidator<GenerateRequest>
    {
        public const int MaxPromptLength = 2000;

        public GenerateRequestValidator()
        {
            RuleFor(x => x.Text)
                .NotEmpty().WithMessage("Tell us about your memory before generating.")
                .MaximumLength(MaxPromptLength)
                .WithMessage($"Your memory must be {MaxPromptLength} characters or fewer.");
        }
    }
}
