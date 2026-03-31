# Guía de tests — Zentinela

---

## Cómo correr los tests

### Tests backend

```bash
# Desde la raíz del repositorio
dotnet test tests/Zentinela.API.Tests/

# Con output detallado
dotnet test tests/Zentinela.API.Tests/ --verbosity normal

# Con reporte de cobertura (requiere coverlet)
dotnet test tests/Zentinela.API.Tests/ --collect:"XPlat Code Coverage"
```

### Tests frontend

No hay tests frontend implementados actualmente. Ver la sección "Cobertura actual y qué falta cubrir".

---

## Stack de testing

### Backend

| Librería | Versión | Uso |
|---|---|---|
| `xUnit` | — | Framework de tests unitarios |
| `Moq` | — | Mocking de dependencias (UserManager, ICarteraRepository) |
| `Microsoft.EntityFrameworkCore.InMemory` | — | Base de datos en memoria para tests de repositorios |
| `System.IdentityModel.Tokens.Jwt` | — | Lectura y verificación de JWTs generados |

### Frontend

| Librería | Estado | Uso previsto |
|---|---|---|
| `Vitest` | No implementado | Runner de tests unitarios para React/TypeScript |
| `@testing-library/react` | No implementado | Renderizado y consulta de componentes |

---

## Tests backend

### AuthServiceTests

Archivo: `tests/Zentinela.API.Tests/AuthServiceTests.cs`

Prueba la lógica de `AuthService.LoginAsync()`. Usa mocks de `UserManager<ApplicationUser>` y una `IConfiguration` en memoria con valores de JWT.

| Nombre del test | Descripción |
|---|---|
| `Login_ConCredencialesValidas_RetornaToken` | Verifica que con email y password correctos se devuelve una `LoginResponse` con token no vacío, nombre completo y rol correctos. |
| `Login_ConEmailInexistente_RetornaNull` | Verifica que cuando `UserManager.FindByEmailAsync` devuelve `null`, `LoginAsync` retorna `null`. |
| `Login_ConPasswordIncorrecta_RetornaNull` | Verifica que cuando `CheckPasswordAsync` devuelve `false`, `LoginAsync` retorna `null`. |
| `Login_TokenContieneLiderID_CuandoUsuarioTieneLider` | Verifica que el `LiderID` del usuario queda reflejado tanto en la propiedad `LoginResponse.LiderID` como en el claim `liderID` del JWT. |

**Helpers internos:**
- `CrearUserManagerMock()`: construye el mock con los 9 parámetros requeridos por el constructor de `UserManager`.
- `CrearConfiguracion()`: crea una `IConfiguration` en memoria con `SecretKey` de 32 caracteres, `ExpirationHours: 8`.
- `CrearUsuario(liderID?)`: construye un `ApplicationUser` de prueba con valores fijos.

---

### CarterasControllerTests

Archivo: `tests/Zentinela.API.Tests/CarterasControllerTests.cs`

Prueba la lógica de `CarterasController.ObtenerCarteras()`. Usa mocks de `ICarteraRepository` y simula el `ClaimsPrincipal` del usuario mediante `ControllerContext` con `DefaultHttpContext`.

| Nombre del test | Descripción |
|---|---|
| `ObtenerCarteras_ComoAdmin_DevuelveTodasLasCarteras` | Verifica que con rol `Admin` se llama a `ObtenerTodasAsync()` y se devuelven las 3 carteras de distintos líderes como `CarteraDto`. |
| `ObtenerCarteras_ComoLider_DevuelveSoloSusCarteras` | Verifica que con rol `Líder` y claim `liderID: 5` se llama a `ObtenerPorLiderAsync(5)` y se devuelven solo las 2 carteras de ese líder. |
| `ObtenerCarteras_SinLiderID_DevuelveForbid` | Verifica que con rol `Líder` pero sin el claim `liderID`, el endpoint retorna `403 ForbidResult`. |
| `ObtenerCarteras_ConRolNoReconocido_DevuelveForbid` | Verifica que con rol `Asesor` y sin `liderID` (claim no parseble), el endpoint retorna `403 ForbidResult`. |

**Helpers internos:**
- `AsignarContexto(controller, claims)`: configura el `ControllerContext` con los claims indicados.
- `CarterasDelLider(cantidad, liderID)`: genera una lista de `Cartera` de prueba.

---

### CarteraRepositoryTests

Archivo: `tests/Zentinela.API.Tests/CarteraRepositoryTests.cs`

Prueba `CarteraRepository` contra una base de datos EF Core InMemory. Cada test crea su propia base con `Guid.NewGuid()` como nombre, evitando contaminación entre pruebas paralelas.

| Nombre del test | Descripción |
|---|---|
| `ObtenerPorLiderAsync_DevuelveSoloCarterasDelLider` | Inserta 2 carteras del líder 1 y 1 del líder 2. Verifica que `ObtenerPorLiderAsync(1)` devuelve exactamente 2 resultados, todos con `LiderID == 1`. |
| `ObtenerPorLiderAsync_CuandoNoHayCarteras_DevuelveListaVacia` | Con base de datos vacía, verifica que `ObtenerPorLiderAsync` devuelve colección vacía (no lanza excepción). |
| `ObtenerTodasAsync_DevuelveTodasLasCarteras` | Inserta 3 carteras de distintos líderes (incluyendo una con `LiderID: null`). Verifica que `ObtenerTodasAsync()` devuelve los 3 registros. |
| `ObtenerPorLiderAsync_ResultadoOrdenadoPorNombre` | Inserta carteras en orden Zeta, Alfa, Medio. Verifica que el resultado viene ordenado: Alfa → Medio → Zeta. |

**Helper interno:**
- `CrearOpcionesInMemory()`: devuelve `DbContextOptions<ZentinelaDbContext>` con base InMemory de nombre único por UUID.

---

## Convenciones de nombres

Los tests siguen el patrón:

```
Método_Condición_ResultadoEsperado
```

### Ejemplos del código fuente

| Nombre completo | Método | Condición | Resultado esperado |
|---|---|---|---|
| `Login_ConCredencialesValidas_RetornaToken` | `Login` | `ConCredencialesValidas` | `RetornaToken` |
| `Login_ConEmailInexistente_RetornaNull` | `Login` | `ConEmailInexistente` | `RetornaNull` |
| `ObtenerCarteras_ComoAdmin_DevuelveTodasLasCarteras` | `ObtenerCarteras` | `ComoAdmin` | `DevuelveTodasLasCarteras` |
| `ObtenerPorLiderAsync_ResultadoOrdenadoPorNombre` | `ObtenerPorLiderAsync` | `ResultadoOrdenadoPorNombre` | (el resultado es la condición misma) |

---

## Cómo agregar nuevos tests

### Nuevo test backend (ejemplo paso a paso)

Supongamos que queremos testear un futuro `ExcelService.GenerarBaseAsync()`.

**1. Crear el archivo de tests** en `tests/Zentinela.API.Tests/`:

```csharp
// ExcelServiceTests.cs
namespace Zentinela.API.Tests;

public class ExcelServiceTests
{
    // Helpers de setup aquí
}
```

**2. Identificar dependencias** que necesita el servicio y crear mocks con Moq:

```csharp
private static Mock<ICarteraRepository> CrearRepoMock()
{
    var mock = new Mock<ICarteraRepository>();
    // configurar Setup() según lo que necesite cada test
    return mock;
}
```

**3. Escribir el test siguiendo el patrón Arrange / Act / Assert:**

```csharp
[Fact]
public async Task GenerarBaseAsync_ConCarteraValida_RetornaArchivoNoVacio()
{
    // Arrange
    var repoMock = CrearRepoMock();
    repoMock
        .Setup(r => r.ObtenerPorLiderAsync(1))
        .ReturnsAsync(new List<Cartera> { new() { ID = 1, NombreCartera = "Test" } });

    var servicio = new ExcelService(repoMock.Object);

    // Act
    var resultado = await servicio.GenerarBaseAsync(liderID: 1);

    // Assert
    Assert.NotNull(resultado);
    Assert.True(resultado.Length > 0, "El archivo Excel no debe estar vacío");
}
```

**4. Ejecutar solo este test para verificar:**

```bash
dotnet test tests/Zentinela.API.Tests/ --filter "GenerarBaseAsync_ConCarteraValida_RetornaArchivoNoVacio"
```

---

### Nuevo test frontend (ejemplo paso a paso)

Los tests de frontend aún no están implementados. Cuando se configuren con Vitest y Testing Library, el flujo será:

**1. Crear el archivo** junto al componente o en una carpeta `__tests__/`:

```typescript
// ZButton.test.tsx
import { render, screen, fireEvent } from '@testing-library/react';
import { ZButton } from '../ZButton';
```

**2. Escribir el test:**

```typescript
describe('ZButton', () => {
  it('renderiza el texto del children', () => {
    render(<ZButton>Descargar</ZButton>);
    expect(screen.getByText('Descargar')).toBeInTheDocument();
  });

  it('llama onClick al hacer clic', () => {
    const handleClick = vi.fn();
    render(<ZButton onClick={handleClick}>Clic</ZButton>);
    fireEvent.click(screen.getByText('Clic'));
    expect(handleClick).toHaveBeenCalledTimes(1);
  });

  it('no llama onClick cuando está disabled', () => {
    const handleClick = vi.fn();
    render(<ZButton disabled onClick={handleClick}>Clic</ZButton>);
    fireEvent.click(screen.getByText('Clic'));
    expect(handleClick).not.toHaveBeenCalled();
  });
});
```

**3. Ejecutar:**

```bash
npm run test
# o en modo watch:
npm run test -- --watch
```

---

## Cobertura actual y qué falta cubrir

### Backend — cubierto

| Clase | Métodos testeados | Tests |
|---|---|---|
| `AuthService` | `LoginAsync` | 4 |
| `CarterasController` | `ObtenerCarteras` | 4 |
| `CarteraRepository` | `ObtenerPorLiderAsync`, `ObtenerTodasAsync` | 4 |

**Total: 12 tests backend.**

### Backend — pendiente de cobertura

| Área | Descripción |
|---|---|
| `DbSeeder` | Verificar que se crean los 5 roles y el usuario admin; que no duplica al ejecutarse dos veces |
| `AuthController` — endpoint `Me` | Verificar que los claims del JWT se mapean correctamente al response body |
| `AuthController` — `Login` con body inválido | Verificar que `400 Bad Request` se devuelve ante email malformado o campos vacíos |
| `CarterasController` — mapeo a DTO | Verificar que los campos `ID` y `NombreCartera` se proyectan correctamente en `CarteraDto` |
| Módulos futuros | `ExcelService`, generador de bases, filtros dinámicos |

### Frontend — pendiente de cobertura

No hay ningún test de frontend implementado. Las áreas prioritarias para implementar son:

| Componente / módulo | Tipo de test sugerido |
|---|---|
| `ZButton` | Renderizado, interacción (click, disabled) |
| `MetricCard` | Renderizado con valores numéricos (formato es-CO) y string |
| `LoginPage` | Formulario: submit con datos válidos, manejo de error 401 |
| `RutaProtegida` | Redirección a `/login` sin token; renderizado de children con token |
| `AuthContext` | Login exitoso almacena token; cerrarSesión limpia el estado |
| `Navbar` | Tabs visibles según rol; click navega a la ruta correcta |
| `Sidebar` | Ítems correctos por tab activa; ítem activo destacado por ruta |
