using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace nostalgia_ai_backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("auth")]
    public class UserController : ControllerBase
    {
        private readonly IAuthenticationService _authenticationService;
        private readonly IUserRepository _userRepository;
        private readonly ILogger<UserController> _logger;

        public UserController(
            IAuthenticationService authenticationService,
            IUserRepository userRepository,
            ILogger<UserController> logger)
        {
            _authenticationService = authenticationService;
            _userRepository = userRepository;
            _logger = logger;
        }

        [HttpPost("socialLoginValidate")]
        public async Task<ActionResult<ApiResponse<AuthResponse>>> ValidateSocialAuthentication([FromBody] LoginToken request)
        {
            var socialProfile = await ResolveSocialProfileAsync(request);
            if (socialProfile == null || string.IsNullOrEmpty(socialProfile.Email))
            {
                return Unauthorized(ApiResponse<AuthResponse>.Fail("Invalid social authentication token."));
            }

            var existingUser = await _userRepository.GetUserByEmailIncludingDeletedAsync(socialProfile.Email);
            if (existingUser != null && !existingUser.Deleted)
            {
                var status = await _userRepository.UpdateUserLoginStatusAsync(existingUser);
                if (!status)
                {
                    return BadRequest(ApiResponse<AuthResponse>.Fail("Failed to update login status."));
                }
                return Ok(ApiResponse<AuthResponse>.Ok(
                    _authenticationService.BuildAuthResponse(existingUser), "Authentication successful."));
            }
            var userModel = new UserModel
            {
                FirstName = socialProfile.GivenName ?? "",
                LastName = socialProfile.FamilyName ?? "",
                Email = socialProfile.Email
            };
            var user = existingUser is { Deleted: true }
                ? await _userRepository.ReactivateSocialLoginUserAsync(existingUser, userModel)
                : await _userRepository.CreateSocialLoginUserAsync(userModel);

            if (user == null)
            {
                return BadRequest(ApiResponse<AuthResponse>.Fail("Failed to create user account."));
            }

            return Ok(ApiResponse<AuthResponse>.Ok(
                _authenticationService.BuildAuthResponse(user), "Authentication successful."));
        }

        private async Task<SocialProfile?> ResolveSocialProfileAsync(LoginToken request)
        {
            switch (request.Provider.ToLowerInvariant())
            {
                case "google":
                {
                    var googleData = await _authenticationService.ValidateGoogleAuthenticationTokenAsync(request.TokenId);
                    if (googleData == null) return null;

                    return new SocialProfile
                    {
                        Email = googleData.Email,
                        GivenName = googleData.GivenName,
                        FamilyName = googleData.FamilyName
                    };
                }

                case "meta":
                {
                    var metaData = await _authenticationService.ValidateMetaAuthenticationTokenAsync(request.TokenId);
                    if (metaData == null) return null;

                    var names = (metaData.Name ?? string.Empty).Split(' ', 2);
                    return new SocialProfile
                    {
                        Email = metaData.Email,
                        GivenName = names.Length > 0 ? names[0] : "",
                        FamilyName = names.Length > 1 ? names[1] : ""
                    };
                }

                default:
                    _logger.LogWarning("Social login attempted with unsupported provider {Provider}.", request.Provider);
                    return null;
            }
        }
    }
}