using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using RenewableEnergy.API.Tests.Infrastructure;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Application.Features.Auth.Commands.Login;
using RenewableEnergy.Application.Features.Proyectos.Commands.ActivarProyecto;
using RenewableEnergy.Application.Features.Proyectos.Commands.CrearProyecto;
using RenewableEnergy.Application.Features.Proyectos.Queries.ObtenerProyectos;

namespace RenewableEnergy.API.Tests.Controllers
{
    /// <summary>
    /// Tests E2E del ProyectosController: peticiones HTTP reales contra la
    /// aplicación completa (middleware + JWT + validación + MediatR + EF).
    /// Verifican el CONTRATO de la API: rutas, códigos de estado, JSON.
    /// </summary>
    public sealed class ProyectosControllerTests(ApiTestFactory factory)
        : IClassFixture<ApiTestFactory>
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>Hace login con el usuario demo del seed y devuelve un cliente autenticado.</summary>
        private async Task<HttpClient> CrearClienteAutenticadoAsync()
        {
            var cliente = factory.CreateClient();

            var respuesta = await cliente.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { Email = "demo@renovables.demo", Password = "Demo1234!" });

            respuesta.EnsureSuccessStatusCode();

            var login = await respuesta.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);

            cliente.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", login!.Token);

            return cliente;
        }

        private static object BodyProyectoValido(string nombre)
        {
            return new
            {
                Nombre = nombre,
                Tipo = "Solar",
                PotenciaKW = 250.5m,
                Coordenadas = new { Latitud = 39.47, Longitud = -0.38 },
                Descripcion = "Creado desde un test E2E"
            };
        }

        [Fact]
        public async Task GetProyectos_DeberiaDevolver200ConLosDatosDelSeed()
        {
            var cliente = factory.CreateClient();

            var respuesta = await cliente.GetAsync("/api/v1/proyectos?pagina=1&tamanoPagina=10");

            respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

            var pagina = await respuesta.Content
                .ReadFromJsonAsync<PaginaDeProyectos>(JsonOptions);

            pagina!.TotalCount.Should().BeGreaterThanOrEqualTo(3, "el seed crea 3 proyectos");
            pagina.Items.Should().NotBeEmpty();
        }

        [Fact]
        public async Task GetProyectoPorId_Inexistente_DeberiaDevolver404ConProblemDetails()
        {
            var cliente = factory.CreateClient();

            var respuesta = await cliente.GetAsync($"/api/v1/proyectos/{Guid.NewGuid()}");

            respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound);
            respuesta.Content.Headers.ContentType!.MediaType.Should().Contain("json");
        }

        [Fact]
        public async Task CrearProyecto_SinToken_DeberiaDevolver401()
        {
            var cliente = factory.CreateClient();

            var respuesta = await cliente.PostAsJsonAsync(
                "/api/v1/proyectos",
                BodyProyectoValido("Proyecto sin token"));

            respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task CrearProyecto_ConTokenYDatosValidos_DeberiaDevolver201ConLocation()
        {
            var cliente = await CrearClienteAutenticadoAsync();

            var respuesta = await cliente.PostAsJsonAsync(
                "/api/v1/proyectos",
                BodyProyectoValido($"Proyecto E2E {Guid.NewGuid():N}"));

            respuesta.StatusCode.Should().Be(HttpStatusCode.Created);
            respuesta.Headers.Location.Should().NotBeNull("un 201 debe indicar dónde vive el recurso");

            var creado = await respuesta.Content.ReadFromJsonAsync<CrearProyectoResponse>(JsonOptions);
            creado!.Estado.Should().Be("Borrador");
        }

        [Fact]
        public async Task CrearProyecto_ConPotenciaNegativa_DeberiaDevolver400ConErroresDeValidacion()
        {
            var cliente = await CrearClienteAutenticadoAsync();

            var body = new
            {
                Nombre = "Proyecto inválido",
                Tipo = "Solar",
                PotenciaKW = -5,
                Coordenadas = new { Latitud = 0.0, Longitud = 0.0 }
            };

            var respuesta = await cliente.PostAsJsonAsync("/api/v1/proyectos", body);

            // El ValidationBehavior corta ANTES del handler y el middleware
            // lo traduce a 400 con la lista de errores en ProblemDetails.
            respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var cuerpo = await respuesta.Content.ReadAsStringAsync();
            cuerpo.Should().Contain("errors");
            cuerpo.Should().Contain("PotenciaKW");
        }

        [Fact]
        public async Task CrearProyecto_ConNombreDuplicado_DeberiaDevolver409()
        {
            var cliente = await CrearClienteAutenticadoAsync();
            var nombre = $"Proyecto duplicado {Guid.NewGuid():N}";

            (await cliente.PostAsJsonAsync("/api/v1/proyectos", BodyProyectoValido(nombre)))
                .StatusCode.Should().Be(HttpStatusCode.Created);

            var segunda = await cliente.PostAsJsonAsync("/api/v1/proyectos", BodyProyectoValido(nombre));

            segunda.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        [Fact]
        public async Task Login_ConPasswordIncorrecta_DeberiaDevolver401()
        {
            var cliente = factory.CreateClient();

            var respuesta = await cliente.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { Email = "demo@renovables.demo", Password = "incorrecta" });

            respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task FlujoCompleto_CrearActivarYMedir_DeberiaFuncionarDePrincipioAFin()
        {
            // El "camino feliz" completo de la API, como lo haría un cliente real:
            // crear (Borrador) → activar (Activo) → registrar medición → consultarla.
            var cliente = await CrearClienteAutenticadoAsync();

            // 1. Crear.
            var creacion = await cliente.PostAsJsonAsync(
                "/api/v1/proyectos",
                BodyProyectoValido($"Flujo completo {Guid.NewGuid():N}"));

            creacion.StatusCode.Should().Be(HttpStatusCode.Created);
            var proyecto = await creacion.Content.ReadFromJsonAsync<CrearProyectoResponse>(JsonOptions);

            // 2. Activar (transición de negocio explícita).
            var activacion = await cliente.PostAsync($"/api/v1/proyectos/{proyecto!.Id}/activar", null);

            activacion.StatusCode.Should().Be(HttpStatusCode.OK);
            var activado = await activacion.Content.ReadFromJsonAsync<ActivarProyectoResponse>(JsonOptions);
            activado!.Estado.Should().Be("Activo");

            // 3. Registrar una medición (recurso anidado).
            var medicion = await cliente.PostAsJsonAsync(
                $"/api/v1/proyectos/{proyecto.Id}/mediciones",
                new { FechaMedicion = DateTime.UtcNow.AddHours(-1), EnergiaGeneradaKWh = 321.5m });

            medicion.StatusCode.Should().Be(HttpStatusCode.Created);

            // 4. La medición aparece en el listado del proyecto.
            var listado = await cliente.GetAsync($"/api/v1/proyectos/{proyecto.Id}/mediciones");

            listado.StatusCode.Should().Be(HttpStatusCode.OK);
            (await listado.Content.ReadAsStringAsync()).Should().Contain("321.5");
        }

        [Fact]
        public async Task OpsInfo_SinApiKey_DeberiaDevolver403()
        {
            var cliente = factory.CreateClient();

            var respuesta = await cliente.GetAsync("/api/v1/ops/info");

            respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task OpsInfo_ConApiKey_DeberiaDevolver200()
        {
            var cliente = factory.CreateClient();
            cliente.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

            var respuesta = await cliente.GetAsync("/api/v1/ops/info");

            respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // DTO local para deserializar la página del listado (el genérico
        // PaginatedList<T> no expone constructor sin parámetros).
        private sealed record PaginaDeProyectos
        {
            public List<ProyectoListadoViewModel> Items { get; init; } = [];
            public int TotalCount { get; init; }
            public int PageNumber { get; init; }
            public int TotalPages { get; init; }
        }
    }
}
