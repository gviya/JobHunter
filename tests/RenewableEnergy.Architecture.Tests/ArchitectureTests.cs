using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
// Alias: ArchUnitNET también define un tipo "Assembly" y colisionaría.
using Assembly = System.Reflection.Assembly;

namespace RenewableEnergy.Architecture.Tests
{
    /// <summary>
    /// TESTS DE ARQUITECTURA con ArchUnitNET: convierten el diagrama de
    /// capas en reglas EJECUTABLES.
    ///
    /// ¿POR QUÉ SON TAN VALIOSOS?
    /// La Clean Architecture se degrada por accidentes pequeños: alguien
    /// añade "using Infrastructure" en un handler y compila sin quejas.
    /// Estos tests convierten esa degradación en un FALLO de CI inmediato.
    /// Recuerda la regla de oro: las dependencias apuntan HACIA DENTRO
    /// (API → Application → Domain; Infrastructure → Application/Domain).
    /// </summary>
    public sealed class ArchitectureTests
    {
        // Un tipo cualquiera de cada assembly sirve de "ancla" para localizarlo.
        private static readonly Assembly DomainAssembly =
            typeof(Domain.Common.BaseEntity).Assembly;

        private static readonly Assembly ApplicationAssembly =
            typeof(Application.DependencyInjection).Assembly;

        private static readonly Assembly InfrastructureAssembly =
            typeof(Infrastructure.DependencyInjection).Assembly;

        private static readonly Assembly ApiAssembly =
            typeof(Program).Assembly;

        // La "arquitectura" se carga UNA vez (es costoso) y la comparten
        // todos los tests de la clase.
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

        [Fact]
        public void Controllers_NoDebenUsarRepositoriosDirectamente()
        {
            Classes().That().ResideInAssembly(ApiAssembly).And().HaveNameEndingWith("Controller")
                .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"RenewableEnergy\.Domain\.Interfaces.*")
                .Because("los controllers hablan con MediatR; el acceso a datos es asunto de los handlers.")
                .Check(Architecture);
        }

        [Fact]
        public void Handlers_NoDebenInvocarOtrosHandlers()
        {
            // La forma práctica de "un handler no llama a otro handler" es
            // prohibirle el mediador: sin ISender/IMediator no puede
            // encadenar casos de uso (eso se hace con eventos de dominio).
            Classes().That().ResideInAssembly(ApplicationAssembly).And().HaveNameEndingWith("Handler")
                .Should().NotDependOnAnyTypesThat().HaveFullNameMatching("MediatR\\.(ISender|IMediator)")
                .Because("encadenar handlers crea acoplamiento oculto; usa eventos de dominio.")
                .Check(Architecture);
        }

        [Fact]
        public void Handlers_DebenSerSealed()
        {
            Classes().That().ResideInAssembly(ApplicationAssembly).And().HaveNameEndingWith("Handler")
                .Should().BeSealed()
                .Because("los handlers no se heredan: la reutilización va por composición.")
                .Check(Architecture);
        }
    }
}
