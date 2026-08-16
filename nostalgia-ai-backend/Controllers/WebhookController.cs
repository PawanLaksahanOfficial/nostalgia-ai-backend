using Application.DTOs;
using Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace nostalgia_ai_backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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
            var stripeSignature = Request.Headers["Stripe-Signature"];
            var webhookSecret = _configuration["Stripe:WebhookSecret"];
            if (string.IsNullOrEmpty(webhookSecret))
            {
                return BadRequest(ApiResponse.Fail("Stripe webhook secret not configured."));
            }
            try
            {
                var stripeEvent = EventUtility.ConstructEvent(json, stripeSignature, webhookSecret);

                var isNewEvent = await _subscriptionService.TryMarkEventProcessedAsync(stripeEvent.Id);
                if (!isNewEvent)
                {
                    return Ok(ApiResponse.Ok("Event already processed."));
                }

                switch (stripeEvent.Type)
                {
                    case "checkout.session.completed":
                        await HandleCheckoutSessionCompleted(stripeEvent);
                        break;
                    case "customer.subscription.created":
                        await HandleSubscriptionCreated(stripeEvent);
                        break;
                    case "customer.subscription.deleted":
                        await HandleSubscriptionDeleted(stripeEvent);
                        break;
                    case "customer.subscription.updated":
                        await HandleSubscriptionUpdated(stripeEvent);
                        break;
                    case "invoice.payment_succeeded":
                        await HandlePaymentSucceeded(stripeEvent);
                        break;
                    case "invoice.payment_failed":
                        await HandlePaymentFailed(stripeEvent);
                        break;
                }

                return Ok(ApiResponse.Ok("Webhook processed successfully."));
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "Stripe webhook signature verification failed.");
                return BadRequest(ApiResponse.Fail("Invalid webhook signature."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing Stripe webhook.");
                return BadRequest(ApiResponse.Fail("An unexpected error occurred processing the webhook."));
            }
        }

        private async Task HandleCheckoutSessionCompleted(Event stripeEvent)
        {
            var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
            if (session == null)
            {
                _logger.LogWarning("Received checkout.session.completed event with an unexpected payload shape.");
                return;
            }
            await _subscriptionService.HandleCheckoutSessionCompletedAsync(session.Id);
        }

        private async Task HandleSubscriptionCreated(Event stripeEvent)
        {
            var subscription = stripeEvent.Data.Object as Stripe.Subscription;
            if (subscription == null)
            {
                _logger.LogWarning("Received customer.subscription.created event with an unexpected payload shape.");
                return;
            }
            var customerId = subscription.CustomerId;
            if (string.IsNullOrEmpty(customerId)) return;

            var currentPeriodEnd = subscription.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd;
            await _subscriptionService.HandleSubscriptionCreatedAsync(customerId, subscription.Id, currentPeriodEnd);
        }

        private async Task HandleSubscriptionDeleted(Event stripeEvent)
        {
            var subscription = stripeEvent.Data.Object as Stripe.Subscription;
            if (subscription == null)
            {
                _logger.LogWarning("Received customer.subscription.deleted event with an unexpected payload shape.");
                return;
            }
            await _subscriptionService.HandleSubscriptionDeletedAsync(subscription.Id);
        }

        private async Task HandleSubscriptionUpdated(Event stripeEvent)
        {
            var subscription = stripeEvent.Data.Object as Stripe.Subscription;
            if (subscription == null)
            {
                _logger.LogWarning("Received customer.subscription.updated event with an unexpected payload shape.");
                return;
            }
            var currentPeriodEnd = subscription.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd;
            await _subscriptionService.HandleSubscriptionUpdatedAsync(subscription.Id, subscription.Status, currentPeriodEnd);
        }

        private async Task HandlePaymentSucceeded(Event stripeEvent)
        {
            var invoice = stripeEvent.Data.Object as Stripe.Invoice;
            if (invoice == null)
            {
                _logger.LogWarning("Received invoice.payment_succeeded event with an unexpected payload shape.");
                return;
            }
            await _subscriptionService.HandlePaymentSucceededAsync(invoice.CustomerId);
        }

        private async Task HandlePaymentFailed(Event stripeEvent)
        {
            var invoice = stripeEvent.Data.Object as Stripe.Invoice;
            if (invoice == null)
            {
                _logger.LogWarning("Received invoice.payment_failed event with an unexpected payload shape.");
                return;
            }
            await _subscriptionService.HandlePaymentFailedAsync(invoice.CustomerId);
        }
    }
}