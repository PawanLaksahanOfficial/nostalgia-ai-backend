using Application.DTOs;
using Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace nostalgia_ai_backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IAuthenticationService _authenticationService;
        private readonly IUserRepository _userRepository;

        public UserController(IAuthenticationService authenticationService, IUserRepository userRepository)
        {
            _authenticationService = authenticationService;
            _userRepository = userRepository;
        }

        [HttpPost("socialLoginValidate")]
        public async Task<ActionResult<ApiResponse<string>>> ValidateSocialAuthentication(LoginToken token, string provider)
        {
            try
            {
                if (string.IsNullOrEmpty(token.TokenId))
                {
                    return BadRequest(ApiResponse<string>.Fail("Invalid token."));
                }

                SocialProfile? socialProfile = null;

                if (provider.ToLower() == "google")
                {
                    var googleData = await _authenticationService.ValidateGoogleAuthenticationTokenAsync(token.TokenId);
                    if (googleData != null)
                    {
                        socialProfile = new SocialProfile
                        {
                            Email = googleData.Email,
                            GivenName = googleData.GivenName,
                            FamilyName = googleData.FamilyName
                        };
                    }
                }
                else if (provider.ToLower() == "meta")
                {
                    var metaData = await _authenticationService.ValidateMetaAuthenticationTokenAsync(token.TokenId);
                    if (metaData != null)
                    {
                        var names = metaData.Name.Split(' ', 2);
                        socialProfile = new SocialProfile
                        {
                            Email = metaData.Email,
                            GivenName = names[0],
                            FamilyName = names.Length > 1 ? names[1] : ""
                        };
                    }
                }
                else
                {
                    return BadRequest(ApiResponse<string>.Fail("Unsupported provider."));
                }

                if (socialProfile == null)
                {
                    return Unauthorized(ApiResponse<string>.Fail("Invalid social authentication token."));
                }
                var jwtToken = string.Empty;
                var user = await _userRepository.GetUserByEmailAsync(socialProfile.Email);
                if (user != null)
                {
                    var status = await _userRepository.UpdateUserLoginStatusAsync(user);
                    if (!status)
                    {
                        return BadRequest(ApiResponse<string>.Fail("Failed to update login status."));
                    }
                    jwtToken = _authenticationService.GenerateJwtToken(user.UserId);
                }
                else
                {
                    var userModel = new UserModel
                    {
                        FirstName = socialProfile.GivenName ?? "",
                        LastName = socialProfile.FamilyName ?? "",
                        Email = socialProfile.Email
                    };

                    var newUser = await _userRepository.CreateSocialLoginUserAsync(userModel);
                    if (newUser == null)
                    {
                        return BadRequest(ApiResponse<string>.Fail("Failed to create user account."));
                    }

                    jwtToken = _authenticationService.GenerateJwtToken(newUser.UserId);
                }

                return Ok(ApiResponse<string>.Ok(jwtToken, "Authentication successful."));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<string>.Fail(ex.Message));
            }
        }
    }
}