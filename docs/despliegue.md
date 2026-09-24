# Despliegue On-Premise — Zentinela

> Última actualización: 2026-09-24

Este documento describe cómo publicar el sistema al servidor de oficina. Zentinela está dividido en dos artefactos independientes: la **API .NET** y la **SPA React**. Cada uno se despliega por separado.

---

## 1. API — `Zentinela.API` (Web Deploy a IIS)

### Perfil de publicación
Vive en `src/Zentinela.API/Properties/PublishProfiles/ZentinelaAPI Profile - Production.pubxml`.

- **Método**: MSDeploy (Web Deploy) → IIS remoto.
- **DeployIisAppPath**: `Zentinela/api` — sitio "Zentinela" + aplicación "api" en IIS.
- **SelfContained**: `false` → el servidor requiere **ASP.NET Core 9 Hosting Bundle** instalado.

> El archivo gemelo `.pubxml.user` guarda credenciales encriptadas y NO se versiona (excluido por `.gitignore` con `*.user`).

### Prerequisitos en el servidor
1. **IIS** con módulo **ASP.NET Core**.
2. **Web Deploy 3.6+** y **Web Management Service** activos.
3. **ASP.NET Core 9 Hosting Bundle** instalado.
4. SQL Server `10.0.0.89,1433` accesible desde el servidor.
5. Sitio "Zentinela" en IIS con aplicación "api" creada y un **binding HTTP en el puerto 5051**:
   - IIS Manager → Sites → Zentinela → Bindings → Add → Type `http`, Port `5051`, IP `*`, Host name vacío.
   - Reglas de firewall: abrir entrada en 5051/TCP.

### Configuración de runtime
- `appsettings.Production.json` (crearlo en el servidor o agregarlo al proyecto antes de publicar) reemplaza los placeholders `__DB_SERVER__`, `__DB_USER__`, `__DB_PASSWORD__`, `__JWT_SECRET__` de `appsettings.json` con los valores reales.
- Con IIS no se configura puerto en Kestrel: el puerto está en el binding del sitio. ASPNETCORE_HTTPS_PORT y similares no aplican.
- Variable de entorno `ASPNETCORE_ENVIRONMENT=Production` se inyecta vía `web.config` que el SDK genera automáticamente al publicar.

### CORS — política `ZentinelaPolicy`
`Program.cs` aplica globalmente la política **`ZentinelaPolicy`** que permite origen `http://zentinela`, cualquier header/método y credentials. Es la política activa en producción y debe coincidir con el host real desde donde se sirva la SPA.

```csharp
options.AddPolicy("ZentinelaPolicy", policy =>
    policy.WithOrigins("http://zentinela")
          .AllowAnyHeader()
          .AllowAnyMethod()
          .AllowCredentials());
```

Pipeline:
```
app.UseRouting();
app.UseCors("ZentinelaPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

> Si la SPA cambia de host (otro dominio interno, IP directa, etc.), actualizar `WithOrigins(...)` o agregar más orígenes a la lista — `AllowCredentials()` es incompatible con `AllowAnyOrigin()`, los orígenes deben enumerarse explícitamente.
>
> La política `FrontendPolicy` (localhost 5173/5174) sigue registrada pero ya no se aplica globalmente; queda disponible para activarla condicionalmente en Development si se requiere.

### AppPool y trabajos en segundo plano — Cargue de Fuentes de Datos (ADR-039)

El módulo *Administración → Cargue de Fuentes de Datos* valida y carga archivos grandes en un
`Task.Run` **dentro del proceso de la API** (hosting in-process). Todo lo que reinicie el proceso mata
el trabajo en curso:

- **Idle Time-out** del AppPool (20 min sin peticiones por defecto). Mientras el usuario tiene la página
  abierta el polling lo mantiene vivo; si cierra el navegador con un cargue de 30 min, el pool se apaga.
- **Reciclaje** del AppPool (periódico, por memoria, por cambio de configuración o por un despliegue).

Configuración obligatoria del AppPool de la aplicación `api` (IIS Manager → Application Pools →
*pool* → Advanced Settings):

| Parámetro | Valor | Por qué |
|---|---|---|
| Process Model → **Idle Time-out (minutes)** | `0` | Sin apagado por inactividad |
| General → **Start Mode** | `AlwaysRunning` | El pool arranca solo y no espera a la primera petición |
| Recycling → Regular Time Interval | `0` o una hora fija fuera de horario | Evitar el reciclaje cada 29 h a media tarde |

Qué pasa si igual se muere:
- En **Validando**: se pierde el job (nada se había escrito); hay que volver a subir el archivo.
- En **Cargando**: la conexión muere y **la transacción se revierte sola**: no hay escritura parcial. La
  fila de `CargueFuenteEjecucion` queda `EN CURSO`; la lista de cargues de la UI la muestra como
  **Interrumpido** y la cierra como `ERROR` (`Interrumpida (reinicio del servidor)`). Volver a subir.
- **No publicar con un cargue en curso**: el despliegue recicla el pool. Mirar antes la lista "en curso".

**Carpeta temporal** (`appsettings` → `CargueFuentes:CarpetaTemporal`). El archivo subido se guarda en
`<carpeta>/<jobId>/archivo.<ext>` entre validar y confirmar (confirmar lo vuelve a leer). Vacío = subcarpeta
`zentinela-cargues` del TEMP del proceso, que con IIS suele ser `C:\Windows\Temp` o el temp del perfil
del AppPool: si ese disco es chico, configurar una ruta en un disco con espacio **y dar permiso de
escritura a la identidad del AppPool** (`IIS AppPool\<pool>`). Espacio: hasta 2 archivos en curso de
≤120 MB más los que esperan confirmación; se borran al terminar y, si quedan (reinicio), la purga los
elimina a las **48 h** (`CargueFuentes:RetencionHoras`). Otros parámetros de la sección:
`LimiteFilasXlsx` (250.000; por encima se pide CSV) y `MaxJobsSimultaneos` (2).

**Límite de subida: 120 MB.** Lo fija `src/Zentinela.API/web.config`
(`<requestLimits maxAllowedContentLength="125829120" />`, IIS corta antes que la aplicación) junto con
`[RequestSizeLimit]` del controller. Si se regenera el `web.config`, conservar ese valor.

**No cargar mientras corre el ETL de Estado Titular** (`Ejecutar_ETL_UltimoEstadoTitular` →
`sp_Insertar_UltimoEstadoTitular`, ADR-038). No se bloquean entre sí, pero los dos escriben mucho log de
transacciones en el mismo servidor: medido el 2026-09-24, con el ETL corriendo el servidor quedó
saturado de escritura de log (esperas `WRITELOG`) y el harness del cargue se detuvo hasta que el ETL
terminó. Consultar el horario del trabajo del Agente (o
`vw_EtlEstadoTitularEstado`) antes de un cargue grande.

### Flujo de publicación
1. VS 2022 → Solution Explorer → click derecho `Zentinela.API` → **Publish**.
2. Seleccionar perfil `ZentinelaAPI Profile - Production`.
3. Botón **Publish** → MSDeploy sube los binarios al servidor y reinicia el AppPool.

### Verificación post-deploy
```
curl http://<servidor>:5051/api/health      # o cualquier endpoint conocido
```
Si responde 200 OK, el despliegue está vivo. Logs de IIS en `C:\inetpub\logs\LogFiles\`.

---

## 2. SPA — `Zentinela.Web` (estática)

### ¿Por qué no aparece en `Zentinela.sln`?
El frontend es **React + Vite + TypeScript**, no es un `.csproj`. El archivo de solución (`.sln`) solo lista proyectos .NET, por eso no se abre desde Visual Studio como un proyecto.

### Cómo abrirlo en desarrollo

**Opción recomendada — VS Code:**
```bash
cd "C:\Agentic Environment\Zentinela\src\Zentinela.Web"
code .
npm install        # primera vez
npm run dev        # vite en http://localhost:5173
```

**Alternativa — Visual Studio "Open Folder":**
File → Open → Folder… → seleccionar `src/Zentinela.Web/`. VS detecta `package.json` y permite ejecutar scripts desde el Task Runner Explorer, pero no integra el build al `.sln`.

**Solo para visualización** se puede agregar como Solution Folder al `.sln` con `package.json`/`vite.config.ts` como ítems, sin build integrado.

### Build de producción
```bash
cd src/Zentinela.Web
npm run build
```
Genera `src/Zentinela.Web/dist/` con HTML, JS y CSS estáticos.

### Despliegue de la SPA
Tres opciones (elegir una):

1. **Sitio IIS independiente** (recomendado para mantener API y SPA desacoplados):
   - Crear sitio "Zentinela.Web" en IIS apuntando a la ruta donde se copie `dist/`.
   - Binding en el puerto que prefieras (ej. 80, 8080).
   - Configurar `web.config` con rewrite a `index.html` para SPA routing.

2. **Servir desde la propia API** (mismo origen, evita CORS):
   - Copiar `dist/*` a `wwwroot/` del bundle de la API antes de publicar.
   - En `Program.cs` agregar `app.UseDefaultFiles(); app.UseStaticFiles();` y al final `app.MapFallbackToFile("index.html");`.
   - Una sola publicación cubre todo.

3. **Otro host estático** (Nginx, S3-like, etc.) — no aplicable al setup On-Premise actual.

### Variable de entorno del frontend
`src/Zentinela.Web/src/api/*Api.ts` resuelve la URL base de la API desde `import.meta.env.VITE_API_URL`. Crear `.env.production` en `src/Zentinela.Web/` antes del build:
```
VITE_API_URL=http://<servidor>:5051/api
```
> No commitear este archivo si contiene URLs internas — añadirlo al `.gitignore` si aún no lo está.

### Fallback de acceso por IP

`src/Zentinela.Web/src/api/authApi.ts` aplica un fallback automático: si la SPA fue cargada desde una IP literal (regex `^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$`) en vez del nombre `zentinela`, la `baseURL` de axios se resuelve como `<protocolo>://<misma-ip>:5051` ignorando `VITE_API_URL`. Esto permite que un usuario en la red interna acceda como `http://10.0.0.89:5050` cuando el DNS interno no resuelve `zentinela`. Para que funcione end-to-end, la política CORS `ZentinelaPolicy` en `Program.cs` también whitelista `http://10.0.0.89:5050`. Si cambia la IP del servidor o el puerto de la SPA, actualizar ambos lugares.

---

## 3. Checklist de release

Antes de publicar a producción:
- [ ] `dotnet build src/Zentinela.API` limpio (0 errores).
- [ ] `npm run build` en `Zentinela.Web` limpio.
- [ ] `appsettings.Production.json` con cadenas de conexión y `JwtSecret` reales (NO commitear).
- [ ] Migraciones EF Core aplicadas en BD de producción (`dotnet ef database update`).
- [ ] Backup de la BD antes de cualquier release con cambios de schema.
- [ ] Verificar que el binding de IIS escucha en 5051 y el firewall permite el tráfico.
- [ ] Smoke test post-deploy: login + un endpoint de cada módulo crítico.
- [ ] AppPool de `api` con `Idle Time-out = 0` y `Start Mode = AlwaysRunning`; ningún Cargue de Fuentes en curso al publicar (ADR-039).
- [ ] `CargueFuentes:CarpetaTemporal` con espacio en disco y permiso de escritura para la identidad del AppPool.
