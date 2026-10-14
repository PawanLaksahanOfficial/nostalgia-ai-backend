using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Application.DTOs;
using Domain.Entities;
using Google.Apis.Auth;

namespace Application.Interfaces
{
    public interface IAuthenticationService
    {
        Task<GoogleJsonWebSignature.Payload?> ValidateGoogleAuthenticationTokenAsync(string token);
        Task<MetaUserDto?> ValidateMetaAuthenticationTokenAsync(string token);
        string GenerateJwtToken(int userId);
        AuthResponse BuildAuthResponse(User user, string refreshToken);
        // Starts a new session: a fresh access token plus the first refresh token.
        Task<AuthResponse> SignInAsync(User user);
    }
}
