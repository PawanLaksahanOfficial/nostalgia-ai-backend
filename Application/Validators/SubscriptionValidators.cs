using Application.DTOs;
using FluentValidation;

namespace Application.Validators
{
    public class CreateCheckoutSessionRequestValidator : AbstractValidator<CreateCheckoutSessionRequest>
    {
        public CreateCheckoutSessionRequestValidator()
        {
            RuleFor(x => x.PriceId)
                .NotEmpty().WithMessage("A plan must be selected.");

            RuleFor(x => x.SuccessUrl)
                .NotEmpty().WithMessage("Success URL is required.");

            RuleFor(x => x.CancelUrl)
                .NotEmpty().WithMessage("Cancel URL is required.");
        }
    }

    public class PortalSessionRequestValidator : AbstractValidator<PortalSessionRequest>
    {
        public PortalSessionRequestValidator()
        {
            RuleFor(x => x.ReturnUrl)
                .NotEmpty().WithMessage("Return URL is required.");
        }
    }
}
