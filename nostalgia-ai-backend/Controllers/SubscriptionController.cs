using Application.DTOs;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace nostalgia_ai_backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SubscriptionController : ControllerBase
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly ILogger<SubscriptionController> _logger;

        public SubscriptionController(ISubscriptionService subscriptionService, ILogger<SubscriptionController> logger)
        {
            _subscriptionService = subscriptionService;
            _logger = logger;
        }

        [HttpGet("quota")]
        public async Task<ActionResult<ApiResponse<UsageQuota>>> GetQuota()
        {
            try
            {
                var userId = GetUserId();
                var quota = await _subscriptionService.GetUsageQuotaAsync(userId);
                return Ok(ApiResponse<UsageQuota>.Ok(quota));
            }
            catch (UnauthorizedAccessException)
            {
                return NotFound(ApiResponse<UsageQuota>.NotFound("User not found."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in GetQuota.");
                return BadRequest(ApiResponse<UsageQuota>.Fail("An unexpected error occurred. Please try again."));
            }
        }

        [HttpPost("checkout")]
        public async Task<ActionResult<ApiResponse<CheckoutSessionResponse>>> CreateCheckoutSession([FromBody] CreateCheckoutSessionRequest request)
        {
            try
            {
                var userId = GetUserId();
                var sessionResponse = await _subscriptionService.CreateCheckoutSessionAsync(
                    userId,
                    request.PriceId,
                    request.SuccessUrl,
                    request.CancelUrl
                );

                return Ok(ApiResponse<CheckoutSessionResponse>.Ok(sessionResponse));
            }
            catch (UnauthorizedAccessException)
            {
                return NotFound(ApiResponse<CheckoutSessionResponse>.NotFound("User not found."));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<CheckoutSessionResponse>.Fail(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in CreateCheckoutSession.");
                return BadRequest(ApiResponse<CheckoutSessionResponse>.Fail("An unexpected error occurred. Please try again."));
            }
        }

        [HttpPost("cancel")]
        public async Task<ActionResult<ApiResponse<object>>> CancelSubscription()
        {
            try
            {
                var userId = GetUserId();
                await _subscriptionService.CancelSubscriptionAsync(userId);
                return Ok(ApiResponse<object>.Ok(new { }, "Your subscription will remain active until the end of the current billing period, then it will not renew."));
            }
            catch (UnauthorizedAccessException)
            {
                return NotFound(ApiResponse<object>.NotFound("User not found."));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in CancelSubscription.");
                return BadRequest(ApiResponse<object>.Fail("An unexpected error occurred. Please try again."));
            }
        }

        private int GetUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                throw new UnauthorizedAccessException("Invalid user token.");
            }
            return userId;
        }
    }
}