using Application.Interfaces;
using Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Stripe;

namespace nostalgia_ai_backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IConfiguration _configuration;

        public WebhookController(ISubscriptionService subscriptionService, IConfiguration configuration)
        {
            _subscriptionService = subscriptionService;
            _configuration = configuration;
        }

        [HttpPost("stripe")]
        public async Task<IActionResult> StripeWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            var stripeSignature = Request.Headers["Stripe-Signature"];
            var webhookSecret = _configuration["Stripe:WebhookSecret"];
            if (string.IsNullOrEmpty(webhookSecret))
            {
                return BadRequest("Stripe webhook secret not configured.");
            }
            try
            {
                var stripeEvent = EventUtility.ConstructEvent(json, stripeSignature, webhookSecret);

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
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest($"Webhook error: {ex.Message}");
            }
        }

        private async Task HandleCheckoutSessionCompleted(Event stripeEvent)
        {
            var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
            if (session == null) return;
            var userIdStr = session.Metadata?.GetValueOrDefault("userId");
            await _subscriptionService.HandleCheckoutSessionCompletedAsync(userIdStr);
        }

        private async Task HandleSubscriptionCreated(Event stripeEvent)
        {
            var subscription = stripeEvent.Data.Object as Stripe.Subscription;
            if (subscription == null) return;
            var customerId = subscription.CustomerId;
            if (string.IsNullOrEmpty(customerId)) return;
            await _subscriptionService.HandleSubscriptionCreatedAsync(customerId, subscription.Id);
        }

        private async Task HandleSubscriptionDeleted(Event stripeEvent)
        {
            var subscription = stripeEvent.Data.Object as Stripe.Subscription;
            if (subscription == null) return;
            await _subscriptionService.HandleSubscriptionDeletedAsync(subscription.Id);
        }

        private async Task HandleSubscriptionUpdated(Event stripeEvent)
        {
            var subscription = stripeEvent.Data.Object as Stripe.Subscription;
            if (subscription == null) return;
            await _subscriptionService.HandleSubscriptionUpdatedAsync(subscription.Id, subscription.Status);
        }

        private async Task HandlePaymentSucceeded(Event stripeEvent)
        {
            var invoice = stripeEvent.Data.Object as Stripe.Invoice;
            if (invoice == null) return;
            await _subscriptionService.HandlePaymentSucceededAsync(invoice.CustomerId);
        }

        private async Task HandlePaymentFailed(Event stripeEvent)
        {
            var invoice = stripeEvent.Data.Object as Stripe.Invoice;
            if (invoice == null) return;
            await _subscriptionService.HandlePaymentFailedAsync(invoice.CustomerId);
        }
    }
}