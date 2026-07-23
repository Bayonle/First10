using System.Reflection;
using First10.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace First10.Tests;

/// <summary>
/// M4 structural guarantee: no controller ships unprotected. Every controller must carry
/// exactly one of: [Authorize] (console surface), [DevelopmentOnly] (D-006 dev cockpit),
/// or [AllowAnonymous] with an entry in the reviewed whitelist below (endpoints whose
/// authorization is cryptographic rather than identity-based).
/// </summary>
public class AuthCoverageTests
{
    /// <summary>Anonymous-by-design endpoints. Adding to this list is a security review event.</summary>
    private static readonly string[] AnonymousWhitelist =
    [
        "MediaController", // serve-side of signed URLs: the HMAC signature is the authorization
    ];

    [Fact]
    public void Every_controller_is_explicitly_protected_or_whitelisted()
    {
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(t => t.IsAssignableTo(typeof(ControllerBase)) && !t.IsAbstract)
            .ToList();
        Assert.NotEmpty(controllers);

        var unprotected = new List<string>();
        foreach (var c in controllers)
        {
            var authorized = c.GetCustomAttribute<AuthorizeAttribute>() is not null;
            var devOnly = c.GetCustomAttribute<DevelopmentOnlyAttribute>() is not null;
            var anonymous = c.GetCustomAttribute<AllowAnonymousAttribute>() is not null;

            if (anonymous && !AnonymousWhitelist.Contains(c.Name))
            {
                unprotected.Add($"{c.Name} is [AllowAnonymous] but not in the reviewed whitelist");
            }
            else if (!authorized && !devOnly && !anonymous)
            {
                unprotected.Add($"{c.Name} has no [Authorize], [DevelopmentOnly], or whitelisted [AllowAnonymous]");
            }
        }

        Assert.True(unprotected.Count == 0, string.Join("; ", unprotected));
    }
}
