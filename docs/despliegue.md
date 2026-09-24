# Despliegue On-Premise — Zentinela

> Última actualización: 2026-09-24

Este documento describe cómo publicar el sistema al servidor de oficina. Zentinela está dividido en dos artefactos independientes: la **API .NET** y la **SPA React**. **Hoy los dos se despliegan juntos y solos** con cada push a la rama `production` (sección 0); las secciones 1 y 2 describen cada artefacto, su configuración en el servidor y la publicación manual, que queda como alternativa.

---

## 0. Despliegue automático — GitHub Actions (cómo se despliega hoy)

`.github/workflows/deploy.yml` ("Deploy Zentinela a Producción") corre con **cada push a `production`**
en un **runner self-hosted instalado en el propio servidor** (el mismo que tiene IIS): no usa Web Deploy
ni credenciales remotas; publica a una carpeta local y copia con `robocopy`. Desplegar = hacer push
a `production` del commit que se quiere publicar; el avance se ve en la pestaña *Actions* de GitHub.

Pasos, en orden:

| # | Paso | Qué hace |
|---|---|---|
| 1 | Checkout | `actions/checkout@v4` con **`clean: false`**: el disco del runner es persistente y así `node_modules` sobrevive entre corridas (sin eso, `npm install` desde cero tardaba ~20 min). Los archivos versionados igual quedan en el commit desplegado |
| 2 | Setup .NET | SDK `9.0.x` |
| 3 | Publicar API | `dotnet publish src/Zentinela.API/Zentinela.API.csproj -c Release -r win-x64 --self-contained true` a `$GITHUB_WORKSPACE\publish_api` (se borra antes). Self-contained: no depende del runtime instalado |
| 4 | Detener App Pool | `Stop-WebAppPool ZentinelaAPI` (si no estaba detenido) + 3 s, para liberar los DLL |
| 5 | Copiar API | `robocopy publish_api C:\inetpub\wwwroot\ZentinelaAPI /E /XF appsettings.Production.json`: **no pisa el `appsettings.Production.json` del servidor** (el que tiene los secretos). Sin `/PURGE`: un archivo que ya no se publica queda en la carpeta. Falla solo con código de robocopy >= 8 |
| 6 | Iniciar App Pool | `Start-WebAppPool ZentinelaAPI` con `if: always()`: el pool vuelve a arrancar aunque la copia haya fallado |
| 7 | Smoke test | `curl` a `http://zentinelaapi/health` resuelto a `127.0.0.1:80`, hasta 12 intentos cada 5 s. `/health` (anónimo, `Program.cs`) verifica que la API arrancó y conecta a la BD. Si no da 200, **el deploy falla en rojo** y los pasos del front no corren (la API nueva ya quedó copiada). Si el sitio de la API cambia de puerto o de host header, ajustar el `--resolve` |
| 8 | Setup Node.js | Node 20 con caché de npm (`src/Zentinela.Web/package-lock.json`) |
| 9 | Instalar dependencias | `npm install --prefer-offline --no-audit --no-fund` (no `npm ci`, que borraría `node_modules` y anularía el ahorro del paso 1) |
| 10 | Build React | `npm run build` (usa `src/Zentinela.Web/.env.production`) |
| 11 | Copiar React | `robocopy src\Zentinela.Web\dist C:\inetpub\wwwroot\Zentinela /E /PURGE` y `exit 0`: **un error de esta copia no hace fallar el deploy** |

**Lo que el workflow NO hace** (queda manual):
- **La configuración del App Pool** `ZentinelaAPI`: `Idle Time-out = 0`, `Start Mode = AlwaysRunning` y el
  reciclaje periódico (ver "AppPool y trabajos en segundo plano" abajo). Se hace una vez en IIS Manager;
  el workflow solo lo detiene y lo arranca.
- `appsettings.Production.json` en `C:\inetpub\wwwroot\ZentinelaAPI` (se crea a mano; el workflow lo preserva).
- Correr tests, migraciones EF Core ni scripts SQL de `docs/sql/migrations/` (cada uno se aplica a mano,
  con su confirmación).
- Revisar si hay un **Cargue de Fuentes en curso**: el paso 4 detiene el pool y lo mata (ADR-039). Mirar
  la lista "en curso" **antes** del push a `production`.

> El runner es un servicio de GitHub Actions en el servidor. Si se cae, los push a `production` quedan en
> cola sin desplegar ("Waiting for a runner"): revisar el servicio del runner en el servidor.

---

## 1. API — `Zentinela.API` (IIS)

### Perfil de publicación (manual, alternativa al workflow)
Vive en `src/Zentinela.API/Properties/PublishProfiles/ZentinelaAPI Profile - Production.pubxml`.

- **Método**: MSDeploy (Web Deploy) → IIS remoto.
- **DeployIisAppPath**: `ZentinelaAPI`.
- **SelfContained**: `true` (igual que el workflow).
- **MSDeployServiceURL** tiene el placeholder `http://SERVERHOST/MsDeployAgentService`: hay que poner el
  servidor real antes de usarlo. El workflow dejó de usar Web Deploy (commit 352976e) justamente porque el
  `.pubxml` apuntaba a ese placeholder.

> El archivo gemelo `.pubxml.user` guarda credenciales encriptadas y NO se versiona (excluido por `.gitignore` con `*.user`).

### Prerequisitos en el servidor
1. **IIS** con módulo **ASP.NET Core** (y el módulo `WebAdministration` de PowerShell, que usa el workflow).
2. **Web Deploy 3.6+** y **Web Management Service** activos (solo para la publicación manual por perfil).
3. **ASP.NET Core 9 Hosting Bundle** instalado (aporta el módulo de IIS; el runtime va dentro del publish self-contained).
4. **Runner self-hosted de GitHub Actions** registrado en el repositorio y corriendo como servicio, con permiso para detener/arrancar el App Pool y escribir en `C:\inetpub\wwwroot\`.
5. SQL Server `10.0.0.89,1433` accesible desde el servidor.
6. Sitio "Zentinela" en IIS con aplicación "api" creada y un **binding HTTP en el puerto 5051**:
   - IIS Manager → Sites → Zentinela → Bindings → Add → Type `http`, Port `5051`, IP `*`, Host name vacío.
   - Reglas de firewall: abrir entrada en 5051/TCP.
7. Lo que asume el workflow: App Pool **`ZentinelaAPI`** sirviendo `C:\inetpub\wwwroot\ZentinelaAPI`, la
   API alcanzable en `127.0.0.1:80` con host header `zentinelaapi` (smoke test) y la SPA servida desde
   `C:\inetpub\wwwroot\Zentinela`. Si el sitio real difiere del punto 6 (puerto 5051), manda el workflow:
   es el que despliega; ajustar este documento o el `--resolve` del smoke test, no los dos por separado.

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

Configuración obligatoria del AppPool de la API (`ZentinelaAPI`; IIS Manager → Application Pools →
*pool* → Advanced Settings). **El workflow de despliegue no la aplica ni la verifica**: se hace una vez
a mano y sobrevive a los despliegues (el workflow solo detiene y arranca el pool):

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
- **No publicar con un cargue en curso**: el despliegue detiene el pool (paso 4 del workflow, con cada push
  a `production`). Mirar antes la lista "en curso". Las corridas del harness de pruebas (usuarios
  `harness-cargue*`) no salen en esa lista (commit cb20c62): no cuentan, pero un despliegue también las mata.

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
**Normal: push a `production`** (sección 0). Manual, solo si el runner no está disponible:
1. VS 2022 → Solution Explorer → click derecho `Zentinela.API` → **Publish**.
2. Seleccionar perfil `ZentinelaAPI Profile - Production` (antes, reemplazar `SERVERHOST`).
3. Botón **Publish** → MSDeploy sube los binarios al servidor y reinicia el AppPool.

Alternativa manual equivalente al workflow, en el servidor: `dotnet publish` self-contained a una carpeta,
detener `ZentinelaAPI`, `robocopy … C:\inetpub\wwwroot\ZentinelaAPI /E /XF appsettings.Production.json`,
arrancar el pool (los pasos 3–6 de la sección 0).

### Verificación post-deploy
El workflow ya consulta `/health` (paso 7). A mano:
```
curl http://<servidor>:5051/health          # endpoint anónimo: 200 = API arriba y conectada a la BD, 503 = no
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
**Hoy lo hace el workflow** (sección 0, pasos 8–11): `npm install` + `npm run build` en el runner y
`robocopy dist C:\inetpub\wwwroot\Zentinela /E /PURGE` (la carpeta queda idéntica a `dist/`; lo que se
agregue a mano ahí se borra en el siguiente despliegue). Solo corre si el smoke test de la API pasó.
Si hay que hacerlo a mano: `npm run build` y la misma copia.

Opciones de hospedaje consideradas (la vigente es un sitio IIS independiente que sirve esa carpeta):

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
`src/Zentinela.Web/src/api/*Api.ts` resuelve la URL base de la API desde `import.meta.env.VITE_API_URL`, que en el build de producción sale de `src/Zentinela.Web/.env.production`. **Hoy ese archivo está versionado** (el workflow lo necesita en el checkout) con:
```
VITE_API_URL=http://zentinelaapi
```
> Solo lleva el nombre interno del host de la API, nunca credenciales. Si cambia el host de la API, cambiarlo aquí y en el `--resolve` del smoke test del workflow.

### Fallback de acceso por IP

`src/Zentinela.Web/src/api/authApi.ts` aplica un fallback automático: si la SPA fue cargada desde una IP literal (regex `^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$`) en vez del nombre `zentinela`, la `baseURL` de axios se resuelve como `<protocolo>://<misma-ip>:5051` ignorando `VITE_API_URL`. Esto permite que un usuario en la red interna acceda como `http://10.0.0.89:5050` cuando el DNS interno no resuelve `zentinela`. Para que funcione end-to-end, la política CORS `ZentinelaPolicy` en `Program.cs` también whitelista `http://10.0.0.89:5050`. Si cambia la IP del servidor o el puerto de la SPA, actualizar ambos lugares.

---

## 3. Checklist de release

Antes de hacer push a `production` (el push despliega):
- [ ] `dotnet build src/Zentinela.API` limpio (0 errores) y tests en verde (el workflow no los corre).
- [ ] `npm run build` en `Zentinela.Web` limpio.
- [ ] `appsettings.Production.json` con cadenas de conexión y `JwtSecret` reales (NO commitear).
- [ ] Migraciones EF Core aplicadas en BD de producción (`dotnet ef database update`).
- [ ] Backup de la BD antes de cualquier release con cambios de schema.
- [ ] Verificar que el binding de IIS escucha en 5051 y el firewall permite el tráfico.
- [ ] Scripts SQL pendientes de `docs/sql/migrations/` que el release necesite, aplicados a mano con confirmación (el workflow no toca la BD).
- [ ] Después del push: el workflow en verde en *Actions* (incluye `/health`); si falla el smoke test, la API nueva ya quedó copiada y el front NO se actualizó.
- [ ] Smoke test post-deploy: login + un endpoint de cada módulo crítico.
- [ ] AppPool `ZentinelaAPI` con `Idle Time-out = 0` y `Start Mode = AlwaysRunning` (manual: el workflow no lo configura); ningún Cargue de Fuentes en curso al publicar (ADR-039).
- [ ] `CargueFuentes:CarpetaTemporal` con espacio en disco y permiso de escritura para la identidad del AppPool.
