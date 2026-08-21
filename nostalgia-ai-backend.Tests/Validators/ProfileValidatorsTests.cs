using Application.DTOs;
using Application.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace nostalgia_ai_backend.Tests.Validators
{
    public class UpdateProfileRequestValidatorTests
    {
        private readonly UpdateProfileRequestValidator _validator = new();

        [Fact]
        public void Passes_when_all_fields_are_omitted()
        {
            var result = _validator.TestValidate(new UpdateProfileRequest());
            result.ShouldNotHaveAnyValidationErrors();
        }

        [Fact]
        public void Passes_for_a_valid_https_avatar_url()
        {
            var result = _validator.TestValidate(new UpdateProfileRequest { AvatarUrl = "https://cdn.example.com/avatar.png" });
            result.ShouldNotHaveValidationErrorFor(x => x.AvatarUrl);
        }

        [Theory]
        [InlineData("not-a-url")]
        [InlineData("ftp://example.com/avatar.png")]
        [InlineData("javascript:alert(1)")]
        public void Fails_for_a_non_http_avatar_url(string avatarUrl)
        {
            var result = _validator.TestValidate(new UpdateProfileRequest { AvatarUrl = avatarUrl });
            result.ShouldHaveValidationErrorFor(x => x.AvatarUrl);
        }

        [Fact]
        public void Fails_when_first_name_exceeds_the_length_limit()
        {
            var result = _validator.TestValidate(new UpdateProfileRequest { FirstName = new string('a', 51) });
            result.ShouldHaveValidationErrorFor(x => x.FirstName);
        }
    }

    public class ChangePasswordRequestValidatorTests
    {
        private readonly ChangePasswordRequestValidator _validator = new();

        [Fact]
        public void Passes_for_a_valid_new_password_that_differs_from_the_current_one()
        {
            var request = new ChangePasswordRequest { CurrentPassword = "OldPassword1", NewPassword = "NewPassword1" };
            var result = _validator.TestValidate(request);
            result.ShouldNotHaveAnyValidationErrors();
        }

        [Fact]
        public void Fails_when_current_password_is_empty()
        {
            var request = new ChangePasswordRequest { CurrentPassword = "", NewPassword = "NewPassword1" };
            var result = _validator.TestValidate(request);
            result.ShouldHaveValidationErrorFor(x => x.CurrentPassword);
        }

        [Fact]
        public void Fails_when_new_password_equals_current_password()
        {
            var request = new ChangePasswordRequest { CurrentPassword = "SamePassword1", NewPassword = "SamePassword1" };
            var result = _validator.TestValidate(request);
            result.ShouldHaveValidationErrorFor(x => x.NewPassword);
        }

        [Fact]
        public void Fails_when_new_password_violates_the_policy()
        {
            var request = new ChangePasswordRequest { CurrentPassword = "OldPassword1", NewPassword = "short" };
            var result = _validator.TestValidate(request);
            result.ShouldHaveValidationErrorFor(x => x.NewPassword);
        }
    }
}
