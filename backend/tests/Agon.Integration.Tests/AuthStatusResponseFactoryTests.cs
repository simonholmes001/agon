using Agon.Api.Auth;
using FluentAssertions;

namespace Agon.Integration.Tests;

public class AuthStatusResponseFactoryTests
{
    [Fact]
    public void Create_Returns_Discovery_Metadata_When_Auth_Enabled()
    {
        var response = AuthStatusResponseFactory.Create(
            authEnabled: true,
            authority: "https://login.microsoftonline.com/17ca2540-dd3e-4204-b2f7-a3e3ad209719/v2.0",
            audience: "api://651d8078-f03e-4278-9111-dd9cd111211a",
            tenantIdHint: "",
            interactiveClientId: "04b07795-8ddb-461a-bbee-02f9e1bf7b46");

        response.Required.Should().BeTrue();
        response.Scheme.Should().Be("bearer");
        response.Authority.Should().Be("https://login.microsoftonline.com/17ca2540-dd3e-4204-b2f7-a3e3ad209719/v2.0");
        response.Audience.Should().Be("api://651d8078-f03e-4278-9111-dd9cd111211a");
        response.TenantId.Should().Be("17ca2540-dd3e-4204-b2f7-a3e3ad209719");
        response.Scope.Should().Be("api://651d8078-f03e-4278-9111-dd9cd111211a/.default");
        response.InteractiveClientId.Should().Be("04b07795-8ddb-461a-bbee-02f9e1bf7b46");
    }

    [Fact]
    public void Create_Returns_Nulls_For_Discovery_Metadata_When_Auth_Disabled()
    {
        var response = AuthStatusResponseFactory.Create(
            authEnabled: false,
            authority: "https://login.microsoftonline.com/17ca2540-dd3e-4204-b2f7-a3e3ad209719/v2.0",
            audience: "api://651d8078-f03e-4278-9111-dd9cd111211a",
            tenantIdHint: "17ca2540-dd3e-4204-b2f7-a3e3ad209719",
            interactiveClientId: "04b07795-8ddb-461a-bbee-02f9e1bf7b46");

        response.Required.Should().BeFalse();
        response.Scheme.Should().Be("none");
        response.Authority.Should().BeNull();
        response.Audience.Should().BeNull();
        response.TenantId.Should().BeNull();
        response.Scope.Should().BeNull();
        response.InteractiveClientId.Should().BeNull();
    }
}
