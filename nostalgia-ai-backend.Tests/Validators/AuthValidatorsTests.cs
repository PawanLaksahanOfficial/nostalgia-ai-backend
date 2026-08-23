using Application.DTOs;
using Application.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace nostalgia_ai_backend.Tests.Validators
{
    public class RegisterRequestValidatorTests
    {
        private readonly RegisterRequestValidator _validator = new();

        [Fact]
        public void Passes_for_a_well_formed_request()
        {
            var request = new RegisterRequest
            {
                FirstName = "Jane",
                LastName = "Doe",
                Email = "jane@example.com",
                Password = "Password123"
            };

            var result = _validator.TestValidate(request);
            result.ShouldNotHaveAnyValidationErrors();
        }

        [Fact]
        public void Fails_when_names_are_empty()
        {
            var request = new RegisterRequest { FirstName = "", LastName = "", Email = "jane@example.com", Password = "Password123" };
            var result = _validator.TestValidate(request);
            result.ShouldHaveValidationErrorFor(x => x.FirstName);
            result.ShouldHaveValidationErrorFor(x => x.LastName);
        }

        [Theory]
        [InlineData("not-an-email")]
        [InlineData("missing-domain@")]
        [InlineData("")]
        public void Fails_for_an_invalid_email(string email)
        {
            var request = new RegisterRequest { FirstName = "Jane", LastName = "Doe", Email = email, Password = "Password123" };
            var result = _validator.TestValidate(request);
            result.ShouldHaveValidationErrorFor(x => x.Email);
        }

        [Theory]
        [InlineData("short1")] // too short
        [InlineData("alllettersnodigit")] // no digit
        [InlineData("12345678")]// no letter
        [InlineData("")]  // empty
        public void Fails_for_a_password_that_violates_the_policy(string password)
        {
            var request = new RegisterRequest { FirstName = "Jane", LastName = "Doe", Email = "jane@example.com", Password = password };
            var result = _validator.TestValidate(request);
            result.ShouldHaveValidationErrorFor(x => x.Password);
        }
    }

    public class LoginRequestValidatorTests
    {
        private readonly LoginRequestValidator _validator = new();

        [Fact]
        public void Passes_for_a_well_formed_request()
        {
            var result = _validator.TestValidate(new LoginRequest { Email = "jane@example.com", Password = "whatever-the-user-actually-set" });
            result.ShouldNotHaveAnyValidationErrors();
        }

        [Fact]
        public void Does_not_enforce_password_complexity_on_login()
        {
            var result = _validator.TestValidate(new LoginRequest { Email = "jane@example.com", Password = "short" });
            result.ShouldNotHaveValidationErrorFor(x => x.Password);
        }

        [Fact]
        public void Fails_when_password_is_empty()
        {
            var result = _validator.TestValidate(new LoginRequest { Email = "jane@example.com", Password = "" });
            result.ShouldHaveValidationErrorFor(x => x.Password);
        }

        [Fact]
        public void Fails_for_an_invalid_email()
        {
            var result = _validator.TestValidate(new LoginRequest { Email = "not-an-email", Password = "whatever" });
            result.ShouldHaveValidationErrorFor(x => x.Email);
        }
    }

    public class ForgotPasswordRequestValidatorTests
    {
        private readonly ForgotPasswordRequestValidator _validator = new();

        [Fact]
        public void Passes_for_a_valid_email()
        {
            var result = _validator.TestValidate(new ForgotPasswordRequest { Email = "jane@example.com" });
            result.ShouldNotHaveAnyValidationErrors();
        }

        [Fact]
        public void Fails_for_an_empty_email()
        {
            var result = _validator.TestValidate(new ForgotPasswordRequest { Email = "" });
            result.ShouldHaveValidationErrorFor(x => x.Email);
        }
    }

    public class ResetPasswordRequestValidatorTests
    {
        private readonly ResetPasswordRequestValidator _validator = new();

        [Fact]
        public void Passes_for_a_well_formed_request()
        {
            var request = new ResetPasswordRequest { Email = "jane@example.com", Token = "some-reset-token", NewPassword = "Password123" };
            var result = _validator.TestValidate(request);
            result.ShouldNotHaveAnyValidationErrors();
        }

        [Fact]
        public void Fails_when_token_is_empty()
        {
            var request = new ResetPasswordRequest { Email = "jane@example.com", Token = "", NewPassword = "Password123" };
            var result = _validator.TestValidate(request);
            result.ShouldHaveValidationErrorFor(x => x.Token);
        }

        [Fact]
        public void Fails_when_new_password_violates_the_policy()
        {
            var request = new ResetPasswordRequest { Email = "jane@example.com", Token = "some-reset-token", NewPassword = "short" };
            var result = _validator.TestValidate(request);
            result.ShouldHaveValidationErrorFor(x => x.NewPassword);
        }
    }
}
