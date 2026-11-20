using NetArchTest.Rules;
using Xunit;

namespace Cwi.Orchestrator.ArchTests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Api_ShouldNotReference_DomainOrDataAccess()
    {
        var result = Types.InAssembly(typeof(Cwi.Orchestrator.Api.Controllers.HealthController).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Orchestrator.Domain")
            .GetResult();

        var dataAccessResult = Types.InAssembly(typeof(Cwi.Orchestrator.Api.Controllers.HealthController).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Orchestrator.DataAccess")
            .GetResult();

        Assert.True(result.IsSuccessful, result.ToString());
        Assert.True(dataAccessResult.IsSuccessful, dataAccessResult.ToString());
    }

    [Fact]
    public void Application_ShouldNotReference_ApiOrDataAccess()
    {
        var result = Types.InAssembly(typeof(Cwi.Orchestrator.Application.ApplicationOptions).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Orchestrator.Api")
            .GetResult();

        var dataAccessResult = Types.InAssembly(typeof(Cwi.Orchestrator.Application.ApplicationOptions).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Orchestrator.DataAccess")
            .GetResult();

        Assert.True(result.IsSuccessful, result.ToString());
        Assert.True(dataAccessResult.IsSuccessful, dataAccessResult.ToString());
    }

    [Fact]
    public void DataAccess_ShouldNotReference_ApiOrApplication()
    {
        var result = Types.InAssembly(typeof(Cwi.Orchestrator.DataAccess.OrchestratorDbContext).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Orchestrator.Api")
            .GetResult();

        var applicationResult = Types.InAssembly(typeof(Cwi.Orchestrator.DataAccess.OrchestratorDbContext).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Orchestrator.Application")
            .GetResult();

        Assert.True(result.IsSuccessful, result.ToString());
        Assert.True(applicationResult.IsSuccessful, applicationResult.ToString());
    }

    [Fact]
    public void Domain_ShouldNotReference_ApiApplicationOrDataAccess()
    {
        var result = Types.InAssembly(typeof(Cwi.Orchestrator.Domain.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Orchestrator.Api")
            .GetResult();

        var applicationResult = Types.InAssembly(typeof(Cwi.Orchestrator.Domain.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Orchestrator.Application")
            .GetResult();

        var dataAccessResult = Types.InAssembly(typeof(Cwi.Orchestrator.Domain.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Orchestrator.DataAccess")
            .GetResult();

        Assert.True(result.IsSuccessful, result.ToString());
        Assert.True(applicationResult.IsSuccessful, applicationResult.ToString());
        Assert.True(dataAccessResult.IsSuccessful, dataAccessResult.ToString());
    }

    [Fact]
    public void Composition_ShouldNotReference_ApiOrDomain()
    {
        var result = Types.InAssembly(typeof(Cwi.Orchestrator.Composition.OrchestratorCompositionOptions).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Orchestrator.Api")
            .GetResult();

        var domainResult = Types.InAssembly(typeof(Cwi.Orchestrator.Composition.OrchestratorCompositionOptions).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Cwi.Orchestrator.Domain")
            .GetResult();

        Assert.True(result.IsSuccessful, result.ToString());
        Assert.True(domainResult.IsSuccessful, domainResult.ToString());
    }
}
