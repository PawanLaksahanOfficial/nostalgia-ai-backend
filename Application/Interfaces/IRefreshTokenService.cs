using Application.DTOs;

namespace Application.Interfaces
{
    public interface IRefreshTokenService
    {
        // Starts a new sign-in session and returns its first refresh token.
        Task<string> IssueAsync(int userId);

        // Swaps a live refresh token for its successor.
        Task<RotatedRefreshToken?> RotateAsync(string refreshToken);

        // Ends the session the token belongs to (sign-out on one device).
        Task RevokeAsync(string refreshToken);

        // Ends every session the user has (password change or reset).
        Task RevokeAllAsync(int userId);
    }
}
