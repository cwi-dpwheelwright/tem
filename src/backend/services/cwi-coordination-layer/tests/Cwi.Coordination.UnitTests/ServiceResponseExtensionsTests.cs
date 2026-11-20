using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Coordination.Api.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Coordination.UnitTests;

public sealed class ServiceResponseExtensionsTests
{
    [Fact]
    public void ToActionResult_Returns_ObjectResult_With_Data_When_Success()
    {
        var response = ServiceResponse<string>.Success("ok", statusCode: 201);

        var result = response.ToActionResult();

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(201);
        objectResult.Value.Should().Be("ok");
    }

    [Fact]
    public void ToActionResult_Returns_ObjectResult_With_Error_When_Failed()
    {
        var error = new DomainError(ErrorCodes.InvalidRequest, "bad request");
        var response = ServiceResponse<string>.Fail(error, statusCode: 400, message: "bad");

        var result = response.ToActionResult();

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(400);
        objectResult.Value.Should().BeEquivalentTo(error);
    }
}
