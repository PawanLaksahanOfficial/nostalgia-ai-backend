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
        public async Task<ActionResult> GetQuota()
        {
            try
            {
                var userId = GetUserId();
                var quota = await _subscriptionService.GetUsageQuotaAsync(userId);
                return Ok(quota);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("checkout")]
        public async Task<ActionResult> CreateCheckoutSession([FromBody] CreateCheckoutSessionRequest request)
        {
            try
            {
                var userId = GetUserId();
                var sessionId = await _subscriptionService.CreateCheckoutSessionAsync(
                    userId, 
                    request.PriceId, 
                    request.SuccessUrl, 
                    request.CancelUrl
                );
                return Ok(new CheckoutSessionResponse { SessionId = sessionId });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("cancel")]
        public async Task<ActionResult> CancelSubscription()
        {
            try
            {
                var userId = GetUserId();
                // Later call Stripe to cancel the subscription
                // For now, return success
                return Ok(new { message = "Subscription cancellation initiated." });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
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