using Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Host.Controllers
{
    [ApiController]
    [EnableRateLimiting("auth")]
    [Route("api/auth")]
    public class AuthController(
        RegisterHandler register,
        LoginHandler login,
        VerifyEmailHandler verifyEmail,
        ResendVerificationHandler resendVerification,
        ForgotPasswordHandler forgotPassword,
        ResetPasswordHandler resetPassword) : ControllerBase
    {
        [HttpPost("register")]
        public async Task<ActionResult<RegisterResult>> Register(RegisterCommand command, CancellationToken ct)
            => Ok(await register.Handle(command, ct));

        [HttpPost("login")]
        public async Task<ActionResult<LoginResult>> Login(LoginCommand command, CancellationToken ct)
            => Ok(await login.Handle(command, ct));

        [HttpPost("verify-email")]
        public async Task<IActionResult> VerifyEmail(VerifyEmailCommand command, CancellationToken ct)
        {
            await verifyEmail.Handle(command, ct);
            return NoContent();
        }

        [HttpPost("resend-verification")]
        public async Task<IActionResult> ResendVerification(ResendVerificationCommand command, CancellationToken ct)
        {
            await resendVerification.Handle(command, ct);
            return Ok(new { message = "If the account exists and is not verified, a new email has been sent." });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordCommand command, CancellationToken ct)
        {
            await forgotPassword.Handle(command, ct);
            return Ok(new { message = "If an account exists for that email, a reset link has been sent." });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(ResetPasswordCommand command, CancellationToken ct)
        {
            await resetPassword.Handle(command, ct);
            return NoContent();
        }

        [Authorize]
        [HttpGet("me")]
        public IActionResult Me() => Ok(new
        {
            UserId = User.FindFirst("sub")?.Value,
            Email = User.FindFirst("email")?.Value,
            OrganizationId = User.FindFirst("org_id")?.Value
        });
    }
}