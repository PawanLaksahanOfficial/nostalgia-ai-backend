using Amazon;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class SesEmailService : IEmailService
    {
        private readonly IAmazonSimpleEmailService _sesClient;
        private readonly ILogger<SesEmailService> _logger;
        private readonly string _fromAddress;
        private readonly string _frontendBaseUrl;

        public SesEmailService(IConfiguration configuration, ILogger<SesEmailService> logger)
        {
            _logger = logger;
            var accessKey = configuration["AWS:AccessKey"] ?? string.Empty;
            var secretKey = configuration["AWS:SecretKey"] ?? string.Empty;
            var region = configuration["AWS:Region"] ?? "us-east-1";
            _fromAddress = configuration["Email:FromAddress"] ?? string.Empty;
            _frontendBaseUrl = configuration["Frontend:BaseUrl"] ?? "http://localhost:5173";

            _sesClient = string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey)
                ? new AmazonSimpleEmailServiceClient(RegionEndpoint.GetBySystemName(region))
                : new AmazonSimpleEmailServiceClient(accessKey, secretKey, RegionEndpoint.GetBySystemName(region));
        }

        public async Task<bool> SendPasswordResetEmailAsync(string email, string resetToken, string userName)
        {
            var resetLink = EmailTemplates.PasswordResetLink(_frontendBaseUrl, resetToken, email);
            var content = EmailTemplates.PasswordReset(resetLink, userName);
            return await SendAsync(email, content.Subject, content.HtmlBody, content.TextBody);
        }

        public async Task<bool> SendEmailVerificationAsync(string email, string verificationToken, string userName)
        {
            var link = EmailTemplates.EmailVerificationLink(_frontendBaseUrl, verificationToken, email);
            var content = EmailTemplates.EmailVerification(link, userName);
            return await SendAsync(email, content.Subject, content.HtmlBody, content.TextBody);
        }

        public Task<bool> SendEmailAsync(string to, string subject, string body) =>
            SendAsync(to, subject, body, "Please view this email in an HTML-compatible email client.");

        private async Task<bool> SendAsync(string to, string subject, string htmlBody, string textBody)
        {
            if (string.IsNullOrWhiteSpace(_fromAddress))
            {
                _logger.LogError("SES email not sent: Email:FromAddress is not configured.");
                return false;
            }
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
                            Html = new Content(htmlBody),
                            Text = new Content(textBody)
                        }
                    }
                };

                var response = await _sesClient.SendEmailAsync(sendRequest);
                return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                // Common causes: an unverified sender address, or an account still in the SES sandbox.
                _logger.LogError(ex, "SES failed to send email '{Subject}'.", subject);
                return false;
            }
        }
    }
}
