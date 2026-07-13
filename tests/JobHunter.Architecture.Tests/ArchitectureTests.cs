using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
// Alias: ArchUnitNET también define un tipo "Assembly" y colisionaría.
using Assembly = System.Reflection.Assembly;

namespace JobHunter.Architecture.Tests;

/// <summary>
/// TESTS DE ARQUITECTURA con ArchUnitNET: convierten el diagrama de
/// capas en reglas EJECUTABLES.
///
/// Regla de oro: las dependencias apuntan HACIA DENTRO
/// (Api → Application → Domain; Infrastructure → Application/Domain).
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly ApplicationAssembly =
        typeof(JobHunter.Application.DependencyInjection).Assembly;

    private static readonly Assembly InfrastructureAssembly =
        typeof(JobHunter.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly ApiAssembly =
        typeof(Program).Assembly;

    // El Domain no expone ningún tipo que la API use directamente, así que
    // se localiza por nombre (su DLL viaja al output como referencia
    // transitiva de Api → Infrastructure → Domain).
    private static readonly Assembly DomainAssembly =
        Assembly.Load("JobHunter.Domain");

    private static readonly ArchUnitNET.Domain.Architecture Architecture = new ArchLoader()
        .LoadAssemblies(DomainAssembly, ApplicationAssembly, InfrastructureAssembly, ApiAssembly)
        .Build();

    private static readonly IObjectProvider<IType> DomainLayer =
        Types().That().ResideInAssembly(DomainAssembly).As("Capa Domain");

    private static readonly IObjectProvider<IType> ApplicationLayer =
        Types().That().ResideInAssembly(ApplicationAssembly).As("Capa Application");

    private static readonly IObjectProvider<IType> InfrastructureLayer =
        Types().That().ResideInAssembly(InfrastructureAssembly).As("Capa Infrastructure");

    private static readonly IObjectProvider<IType> ApiLayer =
        Types().That().ResideInAssembly(ApiAssembly).As("Capa API");

    [Fact]
    public void Domain_NoDebeDependerDeApplication()
    {
        Types().That().Are(DomainLayer)
            .Should().NotDependOnAny(ApplicationLayer)
            .Because("Domain es el núcleo: no conoce a NADIE.")
            .Check(Architecture);
    }

    [Fact]
    public void Domain_NoDebeDependerDeInfrastructure()
    {
        Types().That().Are(DomainLayer)
            .Should().NotDependOnAny(InfrastructureLayer)
            .Because("Domain no puede saber cómo se persiste ni se publica nada.")
            .Check(Architecture);
    }

    [Fact]
    public void Domain_NoDebeDependerDeLaApi()
    {
        Types().That().Are(DomainLayer)
            .Should().NotDependOnAny(ApiLayer)
            .Because("Domain no conoce HTTP.")
            .Check(Architecture);
    }

    [Fact]
    public void Application_NoDebeDependerDeInfrastructure()
    {
        Types().That().Are(ApplicationLayer)
            .Should().NotDependOnAny(InfrastructureLayer)
            .Because("Application define contratos; Infrastructure los implementa (inversión de dependencias).")
            .Check(Architecture);
    }

    [Fact]
    public void Application_NoDebeDependerDeLaApi()
    {
        Types().That().Are(ApplicationLayer)
            .Should().NotDependOnAny(ApiLayer)
            .Because("los casos de uso no saben desde dónde se los invoca.")
            .Check(Architecture);
    }

    [Fact]
    public void Application_NoDebeUsarEntityFrameworkDirectamente()
    {
        Types().That().Are(ApplicationLayer)
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"Microsoft\.EntityFrameworkCore.*")
            .Because("el acceso a datos pasa por las interfaces de repositorio, nunca por EF directo.")
            .Check(Architecture);
    }
}
