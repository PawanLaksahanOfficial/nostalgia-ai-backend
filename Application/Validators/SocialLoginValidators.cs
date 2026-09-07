using Application.DTOs;
using FluentValidation;

namespace Application.Validators
{
    public class LoginTokenValidator : AbstractValidator<LoginToken>
    {
        private static readonly string[] SupportedProviders = { "google", "meta" };

        public LoginTokenValidator()
        {
            RuleFor(x => x.TokenId)
                .NotEmpty().WithMessage("Social token is required.");

            RuleFor(x => x.Provider)
                .NotEmpty().WithMessage("Provider is required.")
                .Must(p => SupportedProviders.Contains(p.ToLowerInvariant()))
                .WithMessage("Unsupported provider.");
        }
    }
}
