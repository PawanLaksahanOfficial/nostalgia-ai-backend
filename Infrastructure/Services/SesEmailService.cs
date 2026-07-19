using Amazon;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Services
{
    public class SesEmailService : IEmailService
    {
        private readonly IAmazonSimpleEmailService _sesClient;
        private readonly string _fromAddress;
        private readonly string _frontendBaseUrl;

        public SesEmailService(IConfiguration configuration)
        {
            var accessKey = configuration["AWS:AccessKey"] ?? string.Empty;
            var secretKey = configuration["AWS:SecretKey"] ?? string.Empty;
            var region = configuration["AWS:Region"] ?? "us-east-1";
            _fromAddress = configuration["Email:FromAddress"] ?? "noreply@nostalgia-ai.com";
            _frontendBaseUrl = configuration["Frontend:BaseUrl"] ?? "http://localhost:5173";

            _sesClient = string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey)
                ? new AmazonSimpleEmailServiceClient(RegionEndpoint.GetBySystemName(region))
                : new AmazonSimpleEmailServiceClient(accessKey, secretKey, RegionEndpoint.GetBySystemName(region));
        }

        public async Task<bool> SendPasswordResetEmailAsync(string email, string resetToken, string userName)
        {
            var resetLink = $"{_frontendBaseUrl}/reset-password?token={resetToken}&email={Uri.EscapeDataString(email)}";
            var subject = "Reset Your Password - Nostalgia AI";
            var body = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='utf-8'>
                </head>
                <body style='font-family: Arial, sans-serif; padding: 20px; background-color: #f4f4f4;'>
                    <div style='max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; padding: 30px;'>
                        <h1 style='color: #333;'>Password Reset Request</h1>
                        <p>Hi {userName},</p>
                        <p>We received a request to reset your password for your Nostalgia AI account.</p>
                        <p>Click the button below to reset your password. This link is valid for 1 hour.</p>
                        <div style='text-align: center; margin: 30px 0;'>
                            <a href='{resetLink}' 
                               style='background-color: #007bff; color: #ffffff; padding: 12px 30px; 
                                      text-decoration: none; border-radius: 5px; font-size: 16px;'>
                                Reset Password
                            </a>
                        </div>
                        <p>If you didn't request this, you can safely ignore this email.</p>
                        <p>If the button doesn't work, copy and paste this link into your browser:</p>
                        <p style='word-break: break-all; color: #666;'>{resetLink}</p>
                        <hr style='border: none; border-top: 1px solid #eee; margin: 20px 0;'>
                        <p style='color: #999; font-size: 12px;'>Nostalgia AI - Preserving your precious memories</p>
                    </div>
                </body>
                </html>";
            return await SendEmailAsync(email, subject, body);
        }

        public async Task<bool> SendEmailAsync(string to, string subject, string body)
        {
            try
            {
                var sendRequest = new SendEmailRequest
                {
                    Source = _fromAddress,
                    Destination = new Destination
                    {
                        ToAddresses = new List<string> { to }
                    },
                    Message = new Message
                    {
                        Subject = new Content(subject),
                        Body = new Body
                        {
                            Html = new Content(body),
                            Text = new Content("Please view this email in an HTML-compatible email client.")
                        }
                    }
                };

                var response = await _sesClient.SendEmailAsync(sendRequest);
                return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}