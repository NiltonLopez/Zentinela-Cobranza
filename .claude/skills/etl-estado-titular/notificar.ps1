# Envia un aviso por WhatsApp via EvolutionAPI.
#
#   powershell -NoProfile -File notificar.ps1 -Mensaje "texto"
#   powershell -NoProfile -File notificar.ps1 -Probar        prueba de punta a punta
#   powershell -NoProfile -File notificar.ps1 -VerConfig     configuracion SIN el apikey
#
# Esta en PowerShell y no en bash a proposito: arma el JSON con ConvertTo-Json en
# vez de escapar comillas a mano, y manda el cuerpo como bytes UTF-8, que es lo
# unico que hace que los acentos y los emoji lleguen bien al telefono.
#
# Codigos de salida:  0 enviado  |  1 fallo el envio  |  2 problema de configuracion
#
# La configuracion vive en config.local.json, ignorado por git.
# NUNCA hardcodear el apikey ni el numero de destino.

[CmdletBinding()]
param(
    [string]$Mensaje,
    [switch]$Probar,
    [switch]$VerConfig
)

$ErrorActionPreference = 'Stop'

# Mensajes de error limpios. Write-Error imprime todo el ErrorRecord con el
# fragmento de codigo que lo origino, que para una herramienta de linea de
# comandos es ruido que tapa el mensaje util.
function Fallar([string]$texto, [int]$codigo = 1) {
    [Console]::Error.WriteLine($texto)
    exit $codigo
}

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rutaCfg = Join-Path $dir 'config.local.json'

if (-not (Test-Path $rutaCfg)) {
    Fallar "Falta $rutaCfg`nCopiar config.ejemplo.json a config.local.json y completarlo." 2
}

try {
    $cfg = Get-Content $rutaCfg -Raw -Encoding UTF8 | ConvertFrom-Json
} catch {
    Fallar "config.local.json no es JSON valido: $($_.Exception.Message)" 2
}

foreach ($campo in @('base_url', 'instancia', 'apikey', 'numero_destino')) {
    if ([string]::IsNullOrWhiteSpace($cfg.$campo)) {
        Fallar "Falta '$campo' en config.local.json" 2
    }
}

$base    = $cfg.base_url.TrimEnd('/')
$formato = if ([string]::IsNullOrWhiteSpace($cfg.formato)) { 'v2' } else { $cfg.formato }

if ($VerConfig) {
    Write-Output "base_url       : $base"
    Write-Output "instancia      : $($cfg.instancia)"
    Write-Output "numero_destino : $($cfg.numero_destino)"
    Write-Output "formato        : $formato"
    Write-Output "apikey         : (configurado, $($cfg.apikey.Length) caracteres)"
    exit 0
}

if ($Probar) {
    $Mensaje = "Prueba de la skill etl-estado-titular. Si lees esto, los avisos funcionan. " +
               (Get-Date -Format 'yyyy-MM-dd HH:mm')
}

if ([string]::IsNullOrWhiteSpace($Mensaje)) {
    Fallar 'Falta el mensaje. Uso: notificar.ps1 -Mensaje "texto"' 2
}

# EvolutionAPI cambio la forma del cuerpo entre v1 y v2. Si no se sabe cual usa
# la instancia, correr -Probar con una y, si devuelve 400, cambiar 'formato'.
if ($formato -eq 'v1') {
    $cuerpo = @{
        number      = $cfg.numero_destino
        options     = @{ delay = 0; presence = 'composing' }
        textMessage = @{ text = $Mensaje }
    }
} else {
    $cuerpo = @{
        number = $cfg.numero_destino
        text   = $Mensaje
    }
}

$json  = $cuerpo | ConvertTo-Json -Depth 6 -Compress
# Sin esto, Invoke-RestMethod manda el cuerpo en la codificacion del sistema y
# los acentos y los emoji llegan como basura.
$bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
$url   = "$base/message/sendText/$($cfg.instancia)"

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $resp = Invoke-RestMethod -Method Post -Uri $url `
        -Headers @{ 'apikey' = $cfg.apikey } `
        -ContentType 'application/json; charset=utf-8' `
        -Body $bytes -TimeoutSec 30
    Write-Output 'Aviso enviado.'
    if ($resp.key.id) { Write-Output "id: $($resp.key.id)" }
    exit 0
}
catch {
    $codigo = $null
    if ($_.Exception.Response) { $codigo = [int]$_.Exception.Response.StatusCode }

    switch ($codigo) {
        401     { Fallar "EvolutionAPI rechazo la autenticacion (401). Revisar 'apikey'." 1 }
        403     { Fallar "EvolutionAPI rechazo la autenticacion (403). Revisar 'apikey'." 1 }
        404     { Fallar "Instancia o ruta no encontrada (404). Revisar 'base_url' e 'instancia'.`nURL: $url" 1 }
        400     { Fallar "EvolutionAPI rechazo el cuerpo (400). Probar cambiando 'formato' entre v1 y v2 en config.local.json." 1 }
        default {
            if ($codigo) { Fallar "Fallo el envio (HTTP $codigo): $($_.Exception.Message)" 1 }
            else         { Fallar "No se pudo contactar a EvolutionAPI en $base`n$($_.Exception.Message)" 1 }
        }
    }
}
