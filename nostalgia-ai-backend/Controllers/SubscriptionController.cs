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
            var userId = GetUserId();
            var quota = await _subscriptionService.GetUsageQuotaAsync(userId);
            return Ok(ApiResponse<UsageQuota>.Ok(quota));
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
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<CheckoutSessionResponse>.Fail(ex.Message));
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
            catch (InvalidOperationException ex)
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