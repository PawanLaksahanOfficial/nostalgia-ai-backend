using Application.DTOs;
using Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Stripe;

namespace nostalgia_ai_backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [DisableRateLimiting]
    public class WebhookController : ControllerBase
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<WebhookController> _logger;

        public WebhookController(ISubscriptionService subscriptionService, IConfiguration configuration, ILogger<WebhookController> logger)
        {
            _subscriptionService = subscriptionService;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("stripe")]
        public async Task<IActionResult> StripeWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            var stripeSignature = Request.Headers["Stripe-Signature"].ToString();
            var webhookSecret = _configuration["Stripe:WebhookSecret"];
            if (string.IsNullOrEmpty(webhookSecret))
            {
                _logger.LogError("Stripe webhook received but Stripe:WebhookSecret is not configured.");
                return BadRequest(ApiResponse.Fail("Stripe webhook secret not configured."));
            }
            if (string.IsNullOrEmpty(stripeSignature))
            {
                _logger.LogWarning("Stripe webhook received with no Stripe-Signature header.");
                return BadRequest(ApiResponse.Fail("Missing webhook signature."));
            }
            Event stripeEvent;
            try
            {
                // Stripe.net pins one API version. Rejecting events from an endpoint on another version would
                // leave customers who paid on the free plan, so they are accepted and the mismatch is logged.
                stripeEvent = EventUtility.ConstructEvent(json, stripeSignature, webhookSecret, throwOnApiVersionMismatch: false);
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "Stripe webhook signature verification failed.");
                return BadRequest(ApiResponse.Fail("Invalid webhook signature."));
            }
            if (!string.Equals(stripeEvent.ApiVersion, StripeConfiguration.ApiVersion, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Stripe webhook event {EventId} uses API version {EventApiVersion}, but this build expects {SdkApiVersion}. Set the webhook endpoint's API version to {SdkApiVersion} in the Stripe dashboard.",
                    stripeEvent.Id, stripeEvent.ApiVersion, StripeConfiguration.ApiVersion, StripeConfiguration.ApiVersion);
            }
            var processed = await _subscriptionService.ProcessEventOnceAsync(
                stripeEvent.Id,
                () => DispatchAsync(stripeEvent));

            return processed
                ? Ok(ApiResponse.Ok("Webhook processed successfully."))
                : Ok(ApiResponse.Ok("Event already processed."));
        }

        private Task DispatchAsync(Event stripeEvent) => stripeEvent.Type switch
        {
            "checkout.session.completed" => HandleCheckoutSessionCompleted(stripeEvent),
            "customer.subscription.created" => HandleSubscriptionCreated(stripeEvent),
            "customer.subscription.deleted" => HandleSubscriptionDeleted(stripeEvent),
            "customer.subscription.updated" => HandleSubscriptionUpdated(stripeEvent),
            "invoice.payment_succeeded" => HandlePaymentSucceeded(stripeEvent),
            "invoice.payment_failed" => HandlePaymentFailed(stripeEvent),
            _ => Task.CompletedTask
        };

        private static string? GetPriceId(Stripe.Subscription subscription) =>
            subscription.Items?.Data?.FirstOrDefault()?.Price?.Id;
        private static DateTime? GetCurrentPeriodEnd(Stripe.Subscription subscription) =>
            subscription.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd;

        private async Task HandleCheckoutSessionCompleted(Event stripeEvent)
        {
            if (stripeEvent.Data.Object is not Stripe.Checkout.Session session)
            {
                _logger.LogWarning("Received checkout.session.completed event with an unexpected payload shape.");
                return;
            }
            await _subscriptionService.HandleCheckoutSessionCompletedAsync(session.Id);
        }

        private async Task HandleSubscriptionCreated(Event stripeEvent)
        {
            if (stripeEvent.Data.Object is not Stripe.Subscription subscription)
            {
                _logger.LogWarning("Received customer.subscription.created event with an unexpected payload shape.");
                return;
            }
            var customerId = subscription.CustomerId;
            if (string.IsNullOrEmpty(customerId)) return;

            await _subscriptionService.HandleSubscriptionCreatedAsync(
                customerId, subscription.Id, GetPriceId(subscription), GetCurrentPeriodEnd(subscription));
        }

        private async Task HandleSubscriptionDeleted(Event stripeEvent)
        {
            if (stripeEvent.Data.Object is not Stripe.Subscription subscription)
            {
                _logger.LogWarning("Received customer.subscription.deleted event with an unexpected payload shape.");
                return;
            }
            await _subscriptionService.HandleSubscriptionDeletedAsync(subscription.Id);
        }

        private async Task HandleSubscriptionUpdated(Event stripeEvent)
        {
            if (stripeEvent.Data.Object is not Stripe.Subscription subscription)
            {
                _logger.LogWarning("Received customer.subscription.updated event with an unexpected payload shape.");
                return;
            }
            await _subscriptionService.HandleSubscriptionUpdatedAsync(
                subscription.Id, subscription.Status, GetPriceId(subscription), GetCurrentPeriodEnd(subscription));
        }

        private async Task HandlePaymentSucceeded(Event stripeEvent)
        {
            if (stripeEvent.Data.Object is not Stripe.Invoice invoice)
            {
                _logger.LogWarning("Received invoice.payment_succeeded event with an unexpected payload shape.");
                return;
            }
            await _subscriptionService.HandlePaymentSucceededAsync(invoice.CustomerId);
        }

        private async Task HandlePaymentFailed(Event stripeEvent)
        {
            if (stripeEvent.Data.Object is not Stripe.Invoice invoice)
            {
                _logger.LogWarning("Received invoice.payment_failed event with an unexpected payload shape.");
                return;
            }
            await _subscriptionService.HandlePaymentFailedAsync(invoice.CustomerId);
        }
    }
}