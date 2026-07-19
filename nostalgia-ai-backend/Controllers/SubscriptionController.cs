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

        public SubscriptionController(ISubscriptionService subscriptionService)
        {
            _subscriptionService = subscriptionService;
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
                return BadRequest(ApiResponse<UsageQuota>.Fail(ex.Message));
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
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<CheckoutSessionResponse>.Fail(ex.Message));
            }
        }

        [HttpPost("cancel")]
        public async Task<ActionResult<ApiResponse<object>>> CancelSubscription()
        {
            try
            {
                // Later call Stripe to cancel the subscription
                return Ok(ApiResponse<object>.Ok(new { }, "Subscription cancellation initiated."));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
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