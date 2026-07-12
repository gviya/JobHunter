# 🧪 Guía de testing desde cero

Si nunca has escrito un test, empieza aquí. Esta guía te lleva de "no sé qué
es un test" a escribir los tuyos con las herramientas de este repo (xUnit,
AwesomeAssertions, Moq). Los ejercicios del **Nivel 0** de `EJERCICIOS.md` la
acompañan paso a paso.

---

## 1. Qué es un test (y por qué te va a encantar)

Un test es un método que **ejecuta tu código con datos conocidos y comprueba
que el resultado es el esperado**. Se escriben una vez y se ejecutan miles:
cada vez que cambies algo, en un segundo sabes si rompiste otra cosa.

Sin tests, verificar un cambio = arrancar la API, hacer login en Swagger,
crear un proyecto, activarlo, mandar una medición... 3 minutos por prueba,
y solo pruebas UN camino. Con tests: `dotnet test` → 117 comprobaciones en
segundos, incluidos los caminos de error que jamás probarías a mano.

## 2. Antes de escribir: EJECUTA los que ya existen

```bash
dotnet test                                              # toda la suite
dotnet test tests/RenewableEnergy.Domain.Tests           # solo un proyecto
dotnet test --filter "FullyQualifiedName~Activar"        # tests cuyo nombre contiene "Activar"
dotnet test --logger "console;verbosity=detailed"        # ver cada test uno a uno
```

**Desde el IDE es aún mejor**: en VS Code (extensión C# Dev Kit) o Visual
Studio aparece un icono ▶ junto a cada test — clic derecho para ejecutarlo o
**depurarlo** (breakpoint dentro del test y F5: verás el código de producción
ejecutarse línea a línea. Es la mejor herramienta de estudio del repo).

## 3. Anatomía: el patrón AAA

Todos los tests del repo siguen la misma estructura de tres actos. Uno real
de `ProyectoRenovableTests.cs`:

```csharp
[Fact]                                                       // ← "esto es un test"
public void Activar_DesdeBorrador_DeberiaCambiarAActivoYLevantarEvento()
{
    // ── ARRANGE (preparar): construye el mundo que el test necesita ──
    var proyecto = CrearProyectoValido();          // un proyecto en Borrador
    proyecto.ClearDomainEvents();                  // limpio eventos del nacimiento

    // ── ACT (actuar): ejecuta LA operación que estás probando (una sola) ──
    proyecto.Activar();

    // ── ASSERT (afirmar): comprueba el resultado y los efectos ──
    proyecto.Estado.Should().Be(EstadoProyecto.Activo);
    proyecto.FechaActivacion.Should().NotBeNull();
    proyecto.GetDomainEvents().Should().ContainSingle()
        .Which.Should().BeOfType<ProyectoActivado>();
}
```

Reglas de oro:
- **Un test = un comportamiento.** Si el nombre lleva "y... y...", son dos tests.
- **El nombre es una frase**: `Metodo_Escenario_ResultadoEsperado`. Cuando
  falle en rojo, el nombre te dice QUÉ se rompió sin abrir el código.
- Los tres bloques separados por línea en blanco (en los tests cortos del
  repo a veces se omiten los comentarios, pero la estructura siempre está).

## 4. xUnit: las dos etiquetas que importan

```csharp
[Fact]                        // un test con UN escenario fijo
public void Create_ConDatosValidos_DeberiaCrearEnEstadoBorrador() { ... }

[Theory]                      // el MISMO test con VARIOS datos de entrada
[InlineData("")]              // se ejecuta una vez por cada InlineData
[InlineData("   ")]
public void Create_ConNombreVacio_DeberiaLanzarDomainException(string nombreInvalido) { ... }
```

`[Theory]` evita copiar/pegar el mismo test cambiando un valor. Míralo en
acción en `CoordenadasTests.cs` (rangos inválidos de latitud).

## 5. AwesomeAssertions: afirmar leyendo en voz alta

En lugar del clásico `Assert.Equal(esperado, real)` (¿cuál era cuál?), las
aserciones se encadenan como una frase:

```csharp
resultado.IsSuccess.Should().BeTrue();
proyecto.Nombre.Should().Be("Parque Solar Test");
lista.Should().HaveCount(2);
lista.Should().ContainSingle().Which.Id.Should().Be(id);   // "tiene exactamente uno, y ese..."
valor.Should().BeNull();

// Excepciones: la acción se envuelve en una lambda
var accion = () => proyecto.Activar();
accion.Should().Throw<DomainException>().WithMessage("*Borrador*");   // * = comodín
```

Su gran ventaja son los MENSAJES DE FALLO: `Expected proyecto.Estado to be
Activo, but found Borrador` — te dice qué esperabas y qué llegó.

> El paquete se llama **AwesomeAssertions** (fork open source de
> FluentAssertions, misma API). En internet busca ejemplos de
> "FluentAssertions": valen igual.

## 6. Moq: dobles de prueba para lo que no quieres ejecutar

Para probar `CrearProyectoHandler` NO quieres una base de datos real: quieres
probar SU lógica. Como el handler depende de INTERFACES (`IProyectoRepository`),
puedes darle un impostor — un **mock** — que se comporta como tú digas:

```csharp
// 1. Crear el mock de la interfaz
private readonly Mock<IProyectoRepository> _repositorioMock = new();

// 2. PROGRAMARLO: "cuando te pregunten si existe el nombre, di que no"
_repositorioMock
    .Setup(r => r.ExisteNombreAsync("Parque X", null, It.IsAny<CancellationToken>()))
    .ReturnsAsync(false);

// 3. Entregarlo al handler (.Object es el impostor en sí)
var handler = new CrearProyectoHandler(_repositorioMock.Object, ...);

// 4. Tras ejecutar, VERIFICAR las interacciones: "¿le pasaste el proyecto al repo?"
_repositorioMock.Verify(r => r.Add(It.IsAny<ProyectoRenovable>()), Times.Once);
_unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never); // ¡o que NO!
```

Vocabulario mínimo:
- `Setup(...).Returns/ReturnsAsync(...)` — programar una respuesta.
- `It.IsAny<T>()` — "me da igual qué argumento llegue".
- `Verify(..., Times.Once/Never)` — comprobar que se llamó (o no).
- `Callback(...)` — capturar lo que le pasaron (mira
  `CrearProyectoHandlerTests.Handle_ConUsuarioAutenticado_...`).

**¿Y esto no es hacer trampa?** No: el test del handler prueba la ORQUESTACIÓN
(qué decide, a quién llama, qué devuelve). Que el repositorio de verdad
funcione lo prueban los tests de integración con SQL Server real. Cada nivel
prueba lo suyo.

## 7. Builders: fabricar objetos de prueba sin dolor

Los tests necesitan proyectos "de mentira" constantemente. En lugar de repetir
`ProyectoRenovable.Create(...)` con 5 argumentos en cada test:

```csharp
var borrador = new ProyectoRenovableBuilder().Build();
var activo   = new ProyectoRenovableBuilder().ConTipo(TipoEnergia.Eolica).Activado().Build();
```

Defaults razonables + personalizas SOLO lo que le importa a tu test. Está en
`tests/RenewableEnergy.Application.Tests/Builders/`.

## 8. Los 4 niveles del repo (dónde va cada test)

| Nivel | Proyecto | Qué prueba | Herramienta | Velocidad |
|---|---|---|---|---|
| **Dominio** | Domain.Tests | Reglas de negocio puras | Nada (C# a pelo) | ⚡ ms |
| **Caso de uso** | Application.Tests | Orquestación de handlers | Moq + Builder | ⚡ ms |
| **Arquitectura** | Architecture.Tests | Que las capas no se rompan | ArchUnitNET | ⚡ s |
| **E2E** | API.Tests | HTTP real, pipeline entero | WebApplicationFactory + SQLite | 🚶 s |
| **Integración** | Infrastructure.Tests | SQL de verdad | Testcontainers (Docker) | 🐢 min |

Regla práctica al añadir código: ¿es una regla de negocio? → test de dominio.
¿Es un handler? → test con mocks. ¿Es un endpoint? → un caso E2E del camino
feliz + los errores importantes. ¿Es SQL/mapeo? → integración.

## 9. Tu flujo para escribir un test nuevo

1. Abre el archivo de tests de la clase (o créalo copiando la estructura de uno vecino).
2. Escribe el NOMBRE primero: `Metodo_Escenario_ResultadoEsperado` — te obliga
   a saber qué pruebas antes de teclear.
3. Escribe el **Assert** antes que el Arrange (¿qué quiero que sea verdad?),
   y rellena hacia atrás.
4. **Míralo fallar** al menos una vez (cambia el valor esperado o comenta el
   código de producción). Un test que nunca has visto en rojo no sabes si
   comprueba algo.
5. Verde → siguiente escenario (el de error, el del límite, el del null).

El paso 4 es el 80 % del valor de esta guía. Interiorízalo.

## 10. TDD en tres líneas (por si te suena de las ofertas)

Test-Driven Development = invertir el orden: PRIMERO escribes el test (rojo),
LUEGO el mínimo código que lo pone verde, DESPUÉS refactorizas. El ejercicio
1.1 (Pausar/Reanudar) es perfecto para probarlo: escribe
`Pausar_DesdeActivo_DeberiaCambiarAPausado` ANTES de que exista `Pausar()` —
ni compilará (ese es el "rojo" más honesto) — y deja que el test te dicte el código.

## 11. Errores de principiante (y cómo los evita este repo)

| Error | Antídoto en el repo |
|---|---|
| Probar 5 cosas en un test | Un comportamiento por test; el nombre te delata |
| Tests que dependen unos de otros | Cada test construye SU mundo en el Arrange (builders) |
| Mockear TODO, hasta lo trivial | El dominio se prueba sin un solo mock |
| Solo probar el camino feliz | Mira cualquier handler: feliz + NotFound + Conflict |
| Aserciones vagas (`NotBeNull` y ya) | Afirma VALORES concretos: `.Should().Be(...)` |
| No ver nunca el test en rojo | Paso 4 del flujo; ejercicio 0.1 |
| Datos aleatorios que a veces fallan | Datos fijos; `Guid.NewGuid()` solo para unicidad |

## 12. Chuleta de comandos

```bash
dotnet test                                          # todo
dotnet test --filter "FullyQualifiedName~MiTest"     # uno concreto
dotnet test --filter "Category!=Integration"         # sin Docker
dotnet test --collect:"XPlat Code Coverage"          # con cobertura
dotnet watch test --project tests/RenewableEnergy.Domain.Tests   # re-ejecuta al guardar 🔁
```

`dotnet watch test` + los tests de dominio = el bucle de aprendizaje más
rápido que existe: editas la entidad, guardas, y en 2 segundos ves el efecto.

➡️ **Siguiente paso**: los ejercicios 0.1 a 0.4 de `EJERCICIOS.md`, que
aplican todo esto con instrucciones paso a paso.
