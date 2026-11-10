using System.Reflection;
using NetArchTest.Rules;

namespace Mawrid.Architecture.Tests;

/// <summary>
/// Module rules (ADR-0001): a module never touches another module's entities or handlers.
/// It may use the other module's published interfaces in <c>Mawrid.Application.Contracts.{Module}</c>.
/// </summary>
public class ModuleTests
{
    private static readonly Assembly DomainAssembly = typeof(Domain.AssemblyReference).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Application.AssemblyReference).Assembly;

    private static readonly string[] ModuleNames = ["Identity", "Suppliers", "Files"];

    public static TheoryData<string> Modules => new(ModuleNames);

    [Theory]
    [MemberData(nameof(Modules))]
    public void Domain_module_does_not_depend_on_other_domain_modules(string module)
    {
        var result = Types.InAssembly(DomainAssembly)
            .That().ResideInNamespace($"Mawrid.Domain.{module}")
            .ShouldNot().HaveDependencyOnAny(Others(module, m => $"Mawrid.Domain.{m}"))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Application_module_uses_other_modules_only_through_contracts(string module)
    {
        var forbidden = Others(module, m => $"Mawrid.Domain.{m}")
            .Concat(Others(module, m => $"Mawrid.Application.{m}"))
            .ToArray();

        var result = Types.InAssembly(ApplicationAssembly)
            .That().ResideInNamespace($"Mawrid.Application.{module}")
            .ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Contracts_depend_on_no_module_internals()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That().ResideInNamespace("Mawrid.Application.Contracts")
            .ShouldNot().HaveDependencyOnAny([.. ModuleNames.Select(m => $"Mawrid.Application.{m}"), .. ModuleNames.Select(m => $"Mawrid.Domain.{m}")])
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    private static string[] Others(string module, Func<string, string> toNamespace) =>
        ModuleNames.Where(m => m != module).Select(toNamespace).ToArray();

    private static string Describe(TestResult result) =>
        "these types break the rule: " + string.Join(", ", result.FailingTypeNames ?? []);
}
