using Application.DTOs;
using Microsoft.AspNetCore.Diagnostics;

namespace nostalgia_ai_backend.Middleware
{
    public class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<GlobalExceptionHandler> _logger;

        public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
        {
            _logger = logger;
        }

        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            var (statusCode, message) = exception switch
            {
                UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Invalid or missing authentication token."),
                _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred. Please try again.")
            };

            if (statusCode == StatusCodes.Status500InternalServerError)
            {
                _logger.LogError(exception, "Unhandled exception processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            }
            else
            {
                _logger.LogWarning(exception, "Handled exception ({StatusCode}) processing {Method} {Path}", statusCode, httpContext.Request.Method, httpContext.Request.Path);
            }

            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.WriteAsJsonAsync(ApiResponse.Fail(message), cancellationToken);
            return true;
        }
    }
}
