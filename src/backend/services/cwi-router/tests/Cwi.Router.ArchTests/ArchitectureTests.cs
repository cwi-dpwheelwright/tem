using NetArchTest.Rules;
using Xunit;

namespace Cwi.Router.ArchTests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Api_ShouldNotReference_DataAccess()
    {
        var result = Types.InAssembly(typeof(Cwi.Router.Api.Controllers.HealthController).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Router.DataAccess")
            .GetResult();

        Assert.True(result.IsSuccessful, result.ToString());
    }

    [Fact]
    public void Domain_ShouldNotReference_ApiOrDataAccess()
    {
        var apiResult = Types.InAssembly(typeof(Cwi.Router.Domain.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Router.Api")
            .GetResult();

        var dataAccessResult = Types.InAssembly(typeof(Cwi.Router.Domain.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Router.DataAccess")
            .GetResult();

        Assert.True(apiResult.IsSuccessful, apiResult.ToString());
        Assert.True(dataAccessResult.IsSuccessful, dataAccessResult.ToString());
    }
}
