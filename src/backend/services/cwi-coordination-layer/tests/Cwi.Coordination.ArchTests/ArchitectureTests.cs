using NetArchTest.Rules;
using Xunit;

namespace Cwi.Coordination.ArchTests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Api_ShouldNotReference_DomainOrDataAccess()
    {
        var result = Types.InAssembly(typeof(Cwi.Coordination.Api.Controllers.HealthController).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Coordination.Domain")
            .GetResult();

        var dataAccessResult = Types.InAssembly(typeof(Cwi.Coordination.Api.Controllers.HealthController).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Coordination.DataAccess")
            .GetResult();

        Assert.True(result.IsSuccessful, result.ToString());
        Assert.True(dataAccessResult.IsSuccessful, dataAccessResult.ToString());
    }

    [Fact]
    public void Controllers_ShouldNot_Depend_On_Repositories()
    {
        var controllerAssembly = typeof(Cwi.Coordination.Api.Controllers.MembershipController).Assembly;
        var controllers = controllerAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Controller", StringComparison.Ordinal));

        foreach (var controller in controllers)
        {
            var constructors = controller.GetConstructors();
            foreach (var ctor in constructors)
            {
                foreach (var parameter in ctor.GetParameters())
                {
                    Assert.False(
                        parameter.ParameterType.Name.EndsWith("Repository", StringComparison.Ordinal),
                        $"{controller.Name} depends on repository {parameter.ParameterType.Name}");
                }
            }
        }
    }

    [Fact]
    public void Application_ShouldNotReference_ApiOrDataAccess()
    {
        var result = Types.InAssembly(typeof(Cwi.Coordination.Application.ApplicationOptions).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Coordination.Api")
            .GetResult();

        var dataAccessResult = Types.InAssembly(typeof(Cwi.Coordination.Application.ApplicationOptions).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Coordination.DataAccess")
            .GetResult();

        Assert.True(result.IsSuccessful, result.ToString());
        Assert.True(dataAccessResult.IsSuccessful, dataAccessResult.ToString());
    }

    [Fact]
    public void DataAccess_ShouldNotReference_ApiOrApplication()
    {
        var result = Types.InAssembly(typeof(Cwi.Coordination.DataAccess.InMemoryMembershipRepository).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Coordination.Api")
            .GetResult();

        var applicationResult = Types.InAssembly(typeof(Cwi.Coordination.DataAccess.InMemoryMembershipRepository).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Coordination.Application")
            .GetResult();

        Assert.True(result.IsSuccessful, result.ToString());
        Assert.True(applicationResult.IsSuccessful, applicationResult.ToString());
    }

    [Fact]
    public void Domain_ShouldNotReference_ApiApplicationOrDataAccess()
    {
        var result = Types.InAssembly(typeof(Cwi.Coordination.Domain.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Coordination.Api")
            .GetResult();

        var applicationResult = Types.InAssembly(typeof(Cwi.Coordination.Domain.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Coordination.Application")
            .GetResult();

        var dataAccessResult = Types.InAssembly(typeof(Cwi.Coordination.Domain.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Coordination.DataAccess")
            .GetResult();

        Assert.True(result.IsSuccessful, result.ToString());
        Assert.True(applicationResult.IsSuccessful, applicationResult.ToString());
        Assert.True(dataAccessResult.IsSuccessful, dataAccessResult.ToString());
    }

    [Fact]
    public void Composition_ShouldNotReference_Api()
    {
        var result = Types.InAssembly(typeof(Cwi.Coordination.Composition.CoordinationCompositionOptions).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Coordination.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, result.ToString());
    }
}
