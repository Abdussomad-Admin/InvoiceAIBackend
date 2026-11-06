using Application.Common.Exceptions;
using Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Host
{
    public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            var (status, title, detail) = exception switch
            {
                DomainException => (StatusCodes.Status400BadRequest, "Invalid request", exception.Message),
                ConflictException => (StatusCodes.Status409Conflict, "Conflict", exception.Message),
                AuthenticationFailedException => (StatusCodes.Status401Unauthorized, "Authentication failed", exception.Message),
                NotFoundException => (StatusCodes.Status404NotFound, "Not found", exception.Message),
                EmailDeliveryException => (StatusCodes.Status502BadGateway, "Email not sent", exception.Message),
                _ => (StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred.")
            };

            if (status == StatusCodes.Status500InternalServerError)
                logger.LogError(exception, "Unhandled exception");

            httpContext.Response.StatusCode = status;
            await httpContext.Response.WriteAsJsonAsync(
                new ProblemDetails { Status = status, Title = title, Detail = detail }, cancellationToken);
            return true;
        }
    }
}