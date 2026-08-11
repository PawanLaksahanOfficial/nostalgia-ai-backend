using Application.DTOs;
using Application.Interfaces;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

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

        // --- Custom ALB Health Check Endpoint ---
        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            return Ok(new { status = "Healthy", timestamp = DateTime.UtcNow });
        }

        [HttpPost("socialLoginValidate")]
        public async Task<ActionResult<string>> ValidateSocialAuthentication(LoginToken token, String provider)
        {
            try
            {
                if (!string.IsNullOrEmpty(token.TokenId))
                {
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
                    else if(provider.ToLower() == "meta")
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
                    if (socialProfile != null)
                    {
                        var jwtToken = string.Empty;
                        var user = await _userRepository.GetUserByEmailAsync(socialProfile.Email);
                        if (user != null)
                        {
                            var status = await _userRepository.UpdateUserLoginStatusAsync(user);
                            if (status)
                            {
                                jwtToken = _authenticationService.GenerateJwtToken(user.UserId);
                            }
                            else
                            {
                                return BadRequest();
                            }
                        }
                        else
                        {
                            var userModel = new UserModel
                            {
                                FirstName = socialProfile.GivenName,
                                LastName = socialProfile.FamilyName,
                                Email = socialProfile.Email
                            };
                            var status = await _userRepository.CreateNewUserAsync(userModel);
                            if (status)
                            {
                                var userRecord = await _userRepository.GetUserByEmailAsync(socialProfile.Email);
                                if (userRecord != null)
                                {
                                    jwtToken = _authenticationService.GenerateJwtToken(userRecord.UserId);
                                }                               
                            }
                            else
                            {
                                return BadRequest();
                            }
                        }
                        return Ok(jwtToken);
                    }
                    else
                    {
                        return BadRequest();
                    }
                }
                return BadRequest();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
