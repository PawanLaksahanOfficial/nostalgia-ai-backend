using Application.DTOs;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;
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

        [HttpGet("plans")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<IReadOnlyList<PlanOption>>>> GetPlans()
        {
            var plans = await _subscriptionService.GetPlansAsync();
            return Ok(ApiResponse<IReadOnlyList<PlanOption>>.Ok(plans));
        }

        [HttpGet("quota")]
        public async Task<ActionResult<ApiResponse<UsageQuota>>> GetQuota()
        {
            var quota = await _subscriptionService.GetUsageQuotaAsync(GetUserId());
            return Ok(ApiResponse<UsageQuota>.Ok(quota));
        }

        [HttpGet("status")]
        public async Task<ActionResult<ApiResponse<SubscriptionStatusResponse>>> GetStatus()
        {
            var status = await _subscriptionService.GetSubscriptionStatusAsync(GetUserId());
            return Ok(ApiResponse<SubscriptionStatusResponse>.Ok(status));
        }

        [HttpPost("checkout")]
        public async Task<ActionResult<ApiResponse<CheckoutSessionResponse>>> CreateCheckoutSession([FromBody] CreateCheckoutSessionRequest request)
        {
            try
            {
                var sessionResponse = await _subscriptionService.CreateCheckoutSessionAsync(
                    GetUserId(),
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
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe rejected the checkout session request.");
                return BadRequest(ApiResponse<CheckoutSessionResponse>.Fail("Could not start checkout. Please try again."));
            }
        }

        [HttpPost("portal")]
        public async Task<ActionResult<ApiResponse<PortalSessionResponse>>> CreatePortalSession([FromBody] PortalSessionRequest request)
        {
            try
            {
                var portal = await _subscriptionService.CreatePortalSessionAsync(GetUserId(), request.ReturnUrl);
                return Ok(ApiResponse<PortalSessionResponse>.Ok(portal));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<PortalSessionResponse>.Fail(ex.Message));
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe rejected the billing portal session request.");
                return BadRequest(ApiResponse<PortalSessionResponse>.Fail("Could not open the billing portal. Please try again."));
            }
        }

        [HttpPost("cancel")]
        public async Task<ActionResult<ApiResponse<object>>> CancelSubscription()
        {
            try
            {
                await _subscriptionService.CancelSubscriptionAsync(GetUserId());
                return Ok(ApiResponse<object>.Ok(new { }, "Your subscription will remain active until the end of the current billing period, then it will not renew."));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe rejected the subscription cancellation request.");
                return BadRequest(ApiResponse<object>.Fail("Could not cancel the subscription. Please try again."));
            }
        }

        [HttpPost("resume")]
        public async Task<ActionResult<ApiResponse<object>>> ResumeSubscription()
        {
            try
            {
                await _subscriptionService.ResumeSubscriptionAsync(GetUserId());
                return Ok(ApiResponse<object>.Ok(new { }, "Your subscription will renew as normal."));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe rejected the subscription resume request.");
                return BadRequest(ApiResponse<object>.Fail("Could not resume the subscription. Please try again."));
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