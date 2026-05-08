# Backup comprimido de las 3 BDs antes de operaciones destructivas.
# Ejecuta BACKUP DATABASE con WITH COMPRESSION en el servidor SQL.
# La carpeta C:\Backups\ debe existir en el servidor; si no, se crea con xp_create_subdir.

param(
  [string]$Server   = "10.0.0.89,1433",
  [string]$User     = "sa",
  [string]$Password = $env:SA_PASSWORD,
  [string]$Folder   = "C:\Backups"
)

if (-not $Password) {
  Write-Error "Define la variable de entorno SA_PASSWORD antes de correr (no la pegues en el script)."
  exit 1
}

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$dbs = @("DBZentinela", "DBRecursosHumanos", "DBCallCenter")

# 1. Asegurar la carpeta en el servidor
$ensure = "EXEC master.dbo.xp_create_subdir N'$Folder';"
& sqlcmd -S $Server -U $User -P $Password -C -b -Q $ensure
if ($LASTEXITCODE -ne 0) { Write-Error "No se pudo asegurar la carpeta $Folder"; exit 1 }

# 2. Backup comprimido por BD
foreach ($db in $dbs) {
  $file = Join-Path $Folder "${db}_$stamp.bak"
  $sql  = "BACKUP DATABASE [$db] TO DISK = N'$file' WITH COMPRESSION, INIT, FORMAT, NAME = N'$db Full $stamp', STATS = 10;"
  Write-Host "→ Backup $db → $file"
  & sqlcmd -S $Server -U $User -P $Password -C -b -Q $sql
  if ($LASTEXITCODE -ne 0) { Write-Error "Backup de $db falló"; exit 1 }
}

Write-Host "`nOK - Backups completados en $Folder con sello $stamp"
