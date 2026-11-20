using Cwi.Coordination.Api.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace Cwi.Coordination.UnitTests;

public sealed class AdminAuthorizationServiceTests
{
    [Fact]
    public void TryAuthorize_Allows_When_Token_Not_Required()
    {
        var service = new AdminAuthorizationService(new AdminAuthorizationOptions
        {
            RequireToken = false
        });

        var context = new DefaultHttpContext();
        var allowed = service.TryAuthorize(context, out var error);

        allowed.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void TryAuthorize_Rejects_When_Token_Config_Missing()
    {
        var service = new AdminAuthorizationService(new AdminAuthorizationOptions
        {
            RequireToken = true,
            HeaderName = "X-Admin-Token",
            Token = ""
        });

        var context = new DefaultHttpContext();
        var allowed = service.TryAuthorize(context, out var error);

        allowed.Should().BeFalse();
        error.Should().Be("admin token not configured");
    }

    [Fact]
    public void TryAuthorize_Rejects_When_Header_Missing()
    {
        var service = new AdminAuthorizationService(new AdminAuthorizationOptions
        {
            RequireToken = true,
            HeaderName = "X-Admin-Token",
            Token = "secret"
        });

        var context = new DefaultHttpContext();
        var allowed = service.TryAuthorize(context, out var error);

        allowed.Should().BeFalse();
        error.Should().Be("missing admin token");
    }

    [Fact]
    public void TryAuthorize_Rejects_When_Header_Invalid()
    {
        var service = new AdminAuthorizationService(new AdminAuthorizationOptions
        {
            RequireToken = true,
            HeaderName = "X-Admin-Token",
            Token = "secret"
        });

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Admin-Token"] = "wrong";
        var allowed = service.TryAuthorize(context, out var error);

        allowed.Should().BeFalse();
        error.Should().Be("invalid admin token");
    }

    [Fact]
    public void TryAuthorize_Allows_When_Header_Matches()
    {
        var service = new AdminAuthorizationService(new AdminAuthorizationOptions
        {
            RequireToken = true,
            HeaderName = "X-Admin-Token",
            Token = "secret"
        });

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Admin-Token"] = "secret";
        var allowed = service.TryAuthorize(context, out var error);

        allowed.Should().BeTrue();
        error.Should().BeNull();
    }
}
