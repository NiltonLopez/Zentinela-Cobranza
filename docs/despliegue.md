# Despliegue On-Premise — Zentinela

> Última actualización: 2026-04-29

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
