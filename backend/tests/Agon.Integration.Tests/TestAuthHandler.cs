using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agon.Integration.Tests;

internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    internal const string SchemeName = "TestAuth";
    internal const string UserIdHeader = "X-Test-User-Id";
    internal const string UserGroupsHeader = "X-Test-User-Groups";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserIdHeader, out var headerValue))
        {
            return Task.FromResult(AuthenticateResult.Fail($"Missing required header: {UserIdHeader}"));
        }

        var raw = headerValue.ToString();
        if (!Guid.TryParse(raw, out var userId))
        {
            return Task.FromResult(AuthenticateResult.Fail($"Invalid {UserIdHeader} value."));
        }

        var claims = new[]
        {
            new Claim("oid", userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("sub", userId.ToString())
        };

        var claimList = new List<Claim>(claims);
        if (Request.Headers.TryGetValue(UserGroupsHeader, out var groupsHeader))
        {
            var groups = groupsHeader.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(group => !string.IsNullOrWhiteSpace(group));

            foreach (var group in groups)
            {
                claimList.Add(new Claim("groups", group));
            }
        }

        var identity = new ClaimsIdentity(claimList, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
