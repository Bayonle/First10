using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace First10.Api.Auth;

/// <summary>
/// Development/Testing-only authentication: every request is the "dev-console" user with
/// dispatcher + admin roles. This keeps AUTHORIZATION structural everywhere (all console
/// endpoints demand an authenticated dispatcher) while local dev needs no identity
/// provider. Program.cs refuses to boot with this scheme outside Development/Testing —
/// the same hard gate pattern as D-006.
/// </summary>
public sealed class DevAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "dev-console"),
            new Claim(ClaimTypes.Role, "dispatcher"),
            new Claim(ClaimTypes.Role, "admin"),
        ], SchemeName);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
