using System.Reflection;
using NetArchTest.Rules;

namespace Mawrid.Architecture.Tests;

/// <summary>
/// Clean Architecture rules (ADR-0001): dependencies point inward only.
/// Api -> Infrastructure -> Application -> Domain.
/// </summary>
public class LayerTests
{
    private const string Domain = "Mawrid.Domain";
    private const string Application = "Mawrid.Application";
    private const string Infrastructure = "Mawrid.Infrastructure";
    private const string Api = "Mawrid.Api";

    private static readonly Assembly DomainAssembly = typeof(Domain.AssemblyReference).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Application.AssemblyReference).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Infrastructure.AssemblyReference).Assembly;

    [Fact]
    public void Domain_does_not_depend_on_other_layers_or_frameworks()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                Application, Infrastructure, Api,
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "MediatR", "FluentValidation")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_api_or_web_frameworks()
    {
        // The EF Core core package is allowed (ADR-0008); the SQL Server provider is not.
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(Infrastructure, Api, "Microsoft.EntityFrameworkCore.SqlServer", "Microsoft.Data.SqlClient", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_api()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(Api)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    private static string Describe(TestResult result) =>
        "these types break the rule: " + string.Join(", ", result.FailingTypeNames ?? []);
}
