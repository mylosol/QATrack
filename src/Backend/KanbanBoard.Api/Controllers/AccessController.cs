using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using KanbanBoard.Api.Middleware;
using KanbanBoard.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace KanbanBoard.Api.Controllers;

/// <summary>Browser sign-in with the shared access password.</summary>
[ApiController]
[Route("api/auth")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class AccessController : ControllerBase
{
    private readonly IOptionsMonitor<AccessControlOptions> _options;
    private readonly ILogger<AccessController> _logger;

    public AccessController(IOptionsMonitor<AccessControlOptions> options, ILogger<AccessController> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>Password sent by the sign-in dialog.</summary>
    public sealed class LoginRequest
    {
        [Required(AllowEmptyStrings = false)]
        [MaxLength(1024)]
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>Whether a password is required and whether this browser is signed in.</summary>
    [HttpGet("status")]
    public IActionResult Status()
    {
        Response.Headers.CacheControl = "no-store";
        var required = _options.CurrentValue.IsRequired;
        return Ok(new { required, authenticated = !required || User.Identity?.IsAuthenticated == true });
    }

    /// <summary>Signs this browser in for <see cref="AccessControlOptions.SessionDays"/> days.</summary>
    [HttpPost("login")]
    [EnableRateLimiting(AccessControlDefaults.LoginRateLimitPolicy)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var options = _options.CurrentValue;
        if (!options.IsRequired)
        {
            return NoContent();
        }

        if (!SharedPasswordHasher.Verify(request.Password, options.SharedPasswordHash))
        {
            _logger.LogWarning("Failed board sign-in from {Remote}.", HttpContext.Connection.RemoteIpAddress);
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Incorrect password",
                detail: "That password is not correct.");
        }

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, "QATrack board user"),
                new Claim(AccessControlDefaults.PasswordVersionClaim, SharedPasswordHasher.Fingerprint(options.SharedPasswordHash)),
            },
            AccessControlDefaults.Scheme);

        await HttpContext.SignInAsync(AccessControlDefaults.Scheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });
        return NoContent();
    }

    /// <summary>Signs this browser out.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AccessControlDefaults.Scheme);
        return NoContent();
    }
}
