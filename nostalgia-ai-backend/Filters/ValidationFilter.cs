using Application.DTOs;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace nostalgia_ai_backend.Filters
{
    public class ValidationFilter : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            foreach (var argument in context.ActionArguments.Values)
            {
                if (argument == null)
                {
                    continue;
                }

                var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
                if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
                {
                    continue;
                }

                var validationContext = new ValidationContext<object>(argument);
                var result = await validator.ValidateAsync(validationContext);
                if (!result.IsValid)
                {
                    var errors = result.Errors.Select(e => e.ErrorMessage).ToList();
                    context.Result = new BadRequestObjectResult(ApiResponse.Fail("Validation failed.", errors));
                    return;
                }
            }

            await next();
        }
    }
}
