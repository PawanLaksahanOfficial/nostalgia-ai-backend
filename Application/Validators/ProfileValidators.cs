using Application.DTOs;
using FluentValidation;

namespace Application.Validators
{
    public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
    {
        public UpdateProfileRequestValidator()
        {
            RuleFor(x => x.FirstName)
                .MaximumLength(50).WithMessage("First name must be 50 characters or fewer.")
                .When(x => x.FirstName != null);

            RuleFor(x => x.LastName)
                .MaximumLength(50).WithMessage("Last name must be 50 characters or fewer.")
                .When(x => x.LastName != null);

            RuleFor(x => x.AvatarUrl)
                .Must(BeAValidHttpUrl).WithMessage("Avatar URL must be a valid http(s) URL.")
                .When(x => !string.IsNullOrEmpty(x.AvatarUrl));
        }

        private static bool BeAValidHttpUrl(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
    {
        public ChangePasswordRequestValidator()
        {
            RuleFor(x => x.CurrentPassword)
                .NotEmpty().WithMessage("Current password is required.");

            RuleFor(x => x.NewPassword).ValidPassword();

            RuleFor(x => x.NewPassword)
                .NotEqual(x => x.CurrentPassword)
                .WithMessage("New password must be different from the current password.");
        }
    }
}
