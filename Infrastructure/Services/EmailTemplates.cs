using System.Net;

namespace Infrastructure.Services
{
    public sealed record EmailContent(string Subject, string HtmlBody, string TextBody);

    // Shared by every IEmailService implementation so switching provider never changes what users receive.
    public static class EmailTemplates
    {
        public static string PasswordResetLink(string frontendBaseUrl, string resetToken, string email) =>
            $"{frontendBaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(resetToken)}&email={Uri.EscapeDataString(email)}";

        public static EmailContent PasswordReset(string resetLink, string userName)
        {
            var safeName = WebUtility.HtmlEncode(userName);
            var safeLink = WebUtility.HtmlEncode(resetLink);
            var html = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='utf-8'>
                </head>
                <body style='font-family: Arial, sans-serif; padding: 20px; background-color: #f4f4f4;'>
                    <div style='max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; padding: 30px;'>
                        <h1 style='color: #333;'>Password Reset Request</h1>
                        <p>Hi {safeName},</p>
                        <p>We received a request to reset your password for your Nostalgia AI account.</p>
                        <p>Click the button below to reset your password. This link is valid for 1 hour.</p>
                        <div style='text-align: center; margin: 30px 0;'>
                            <a href='{safeLink}'
                               style='background-color: #007bff; color: #ffffff; padding: 12px 30px;
                                      text-decoration: none; border-radius: 5px; font-size: 16px;'>
                                Reset Password
                            </a>
                        </div>
                        <p>If you didn't request this, you can safely ignore this email.</p>
                        <p>If the button doesn't work, copy and paste this link into your browser:</p>
                        <p style='word-break: break-all; color: #666;'>{safeLink}</p>
                        <hr style='border: none; border-top: 1px solid #eee; margin: 20px 0;'>
                        <p style='color: #999; font-size: 12px;'>Nostalgia AI - Preserving your precious memories</p>
                    </div>
                </body>
                </html>";
            var text =
                $"Hi {userName},\n\n" +
                "We received a request to reset your Nostalgia AI password. This link is valid for 1 hour:\n\n" +
                $"{resetLink}\n\n" +
                "If you didn't request this, you can safely ignore this email.";
            return new EmailContent("Reset Your Password - Nostalgia AI", html, text);
        }
    }
}
