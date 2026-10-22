# SPDX-License-Identifier: GPL-3.0-or-later
# Gera o DeXter portatil para o release:
#   dist\DeXter-<versao>-win-x64.zip   DeXter.exe (self-contained, nao precisa de .NET instalado)
#                                      + scrcpy\ (scrcpy + adb) + server\dexter-server.jar
#   dist\SHA256SUMS.txt
#
# Uso: powershell -ExecutionPolicy Bypass -File scripts\pack.ps1 -Versao 0.5.0
# Precisa do .NET 8 SDK, JDK e Android SDK (para o servidor do DeX nativo).

param([Parameter(Mandatory)] [string]$Versao)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Comum.ps1')

if ($Versao -notmatch '^\d+\.\d+\.\d+$') { throw "Versao deve ser x.y.z (ex.: 0.5.0). Recebido: $Versao" }

$dist = Join-Path $Raiz 'dist'
$obj = Join-Path $Raiz 'obj-pack'
$app = Join-Path $obj 'DeXter'
Remove-Item $obj, $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dist, $app | Out-Null

# 1. servidor do DeX nativo (obrigatorio no release)
& (Join-Path $Raiz 'server\build-server.ps1')
if (-not (Test-Path (Join-Path $Raiz 'server\dexter-server.jar'))) { throw "dexter-server.jar nao foi gerado." }

# 2. app self-contained em um exe so (o servidor ja vem do passo 1, nao recompila)
Write-Host "Publicando..." -ForegroundColor Cyan
dotnet publish $Projeto -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Versao -p:DebugType=none -p:DebugSymbols=false -p:PularServidor=true -o $app
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou." }
foreach ($f in 'DeXter.exe', 'scrcpy\scrcpy.exe', 'scrcpy\adb.exe', 'server\dexter-server.jar') {
    if (-not (Test-Path (Join-Path $app $f))) { throw "Faltou $f no pacote." }
}

# 3. leia-me, licencas
Copy-Item (Join-Path $Raiz 'packaging\zip\*') $app
Copy-Item (Join-Path $Raiz 'LICENSE') (Join-Path $app 'LICENSE.txt')
Copy-Item (Join-Path $Raiz 'THIRD_PARTY_NOTICES.md') $app
Copy-Item (Join-Path $Raiz 'server\LICENSE-ScrcpyDeX.txt') (Join-Path $app 'server')

# 4. zip (pasta DeXter\ dentro do zip)
$zip = Join-Path $dist "DeXter-$Versao-win-x64.zip"
Compress-Archive -Path $app -DestinationPath $zip -Force
"$((Get-FileHash $zip -Algorithm SHA256).Hash.ToLower())  $(Split-Path $zip -Leaf)" |
    Set-Content (Join-Path $dist 'SHA256SUMS.txt')

Write-Host ""
Write-Host "Pronto:" -ForegroundColor Green
Get-ChildItem $dist | ForEach-Object { Write-Host ("  {0,-34} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB)) }
