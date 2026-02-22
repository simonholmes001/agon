using System.Reflection;
using Agon.Infrastructure.Agents;
using FluentAssertions;

namespace Agon.Infrastructure.Tests.Agents;

public class ProviderErrorSummaryTests
{
    [Fact]
    public void FromResponseBody_ReturnsDefaultMessage_WhenBodyIsEmpty()
    {
        var summary = InvokeFromResponseBody(string.Empty);

        summary.Should().Be("No error details returned by provider.");
    }

    [Fact]
    public void FromResponseBody_ReturnsNonJsonMessage_WhenBodyIsNotJson()
    {
        var summary = InvokeFromResponseBody("Service unavailable");

        summary.Should().Be("Provider returned a non-JSON error payload.");
    }

    [Fact]
    public void FromResponseBody_UsesErrorString_WhenProvided()
    {
        var summary = InvokeFromResponseBody(
            """
            { "error": "  line1\nline2\tvalue  " }
            """);

        summary.Should().Be("line1 line2 value");
    }

    [Fact]
    public void FromResponseBody_UsesNestedErrorArray_WhenProvided()
    {
        var summary = InvokeFromResponseBody(
            """
            {
              "error": [
                {},
                { "type": "invalid_request_error", "message": "missing model id" }
              ]
            }
            """);

        summary.Should().Be("invalid_request_error: missing model id");
    }

    [Fact]
    public void FromResponseBody_FallsBackToRootMessage_WhenErrorIsMissing()
    {
        var summary = InvokeFromResponseBody(
            """
            { "message": " top level message " }
            """);

        summary.Should().Be("top level message");
    }

    [Fact]
    public void FromResponseBody_FallsBackToRootDetail_WhenMessageMissing()
    {
        var summary = InvokeFromResponseBody(
            """
            { "detail": " detailed explanation " }
            """);

        summary.Should().Be("detailed explanation");
    }

    [Fact]
    public void FromResponseBody_ReturnsStructuredFallback_WhenNoMessageFieldsExist()
    {
        var summary = InvokeFromResponseBody(
            """
            { "status": 500, "error": { "context": "none" } }
            """);

        summary.Should().Be("Provider returned no structured error message.");
    }

    [Fact]
    public void FromResponseBody_TruncatesVeryLongMessages()
    {
        var longMessage = new string('a', 250);
        var summary = InvokeFromResponseBody($$"""
            { "error": { "message": "{{longMessage}}" } }
            """);

        summary.Length.Should().Be(180);
        summary.Should().Be(new string('a', 180));
    }

    private static string InvokeFromResponseBody(string responseBody)
    {
        var assembly = typeof(OpenAiCouncilAgent).Assembly;
        var type = assembly.GetType("Agon.Infrastructure.Agents.ProviderErrorSummary", throwOnError: true)!;
        var method = type.GetMethod(
            "FromResponseBody",
            BindingFlags.Public | BindingFlags.Static)!;

        var result = method.Invoke(null, [responseBody]);
        return result.Should().BeOfType<string>().Subject;
    }
}
