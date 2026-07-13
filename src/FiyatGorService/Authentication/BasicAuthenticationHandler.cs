using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using FiyatGorService.Configuration;
using FiyatGorService.Security;

namespace FiyatGorService.Authentication;

public sealed class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IOptionsMonitor<AppSettings> _settingsMonitor;

    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptionsMonitor<AppSettings> settingsMonitor)
        : base(options, logger, encoder)
    {
        _settingsMonitor = settingsMonitor;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authorizationHeader))
        {
            return Task.FromResult(AuthenticateResult.Fail("Authorization header missing."));
        }

        if (!authorizationHeader.ToString().StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.Fail("Unsupported authorization scheme."));
        }

        string decodedCredentials;

        try
        {
            var encodedCredentials = authorizationHeader.ToString()["Basic ".Length..].Trim();
            var credentialBytes = Convert.FromBase64String(encodedCredentials);
            decodedCredentials = Encoding.UTF8.GetString(credentialBytes);
        }
        catch
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid authorization header."));
        }

        var separatorIndex = decodedCredentials.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid basic auth payload."));
        }

        var username = decodedCredentials[..separatorIndex];
        var password = decodedCredentials[(separatorIndex + 1)..];
        var settings = _settingsMonitor.CurrentValue;

        if (!string.Equals(username, settings.Admin.Username, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(settings.Admin.PasswordHash) ||
            !PasswordHasher.Verify(password, settings.Admin.PasswordHash))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid username or password."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, "Admin")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Basic realm=\"FiyatGorAdmin\"";
        return base.HandleChallengeAsync(properties);
    }
}
