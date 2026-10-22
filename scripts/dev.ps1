# SPDX-License-Identifier: GPL-3.0-or-later
# Desenvolvimento: compila (app + servidor Java do DeX nativo) e abre o DeXter.
# Uso: powershell -ExecutionPolicy Bypass -File scripts\dev.ps1

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Comum.ps1')

Stop-DeXter
Remove-PacotesAntigos

Write-Host "Compilando..." -ForegroundColor Cyan
dotnet build $Projeto -c Release
if ($LASTEXITCODE -ne 0) { throw "Build falhou." }

$exe = Join-Path $Raiz 'src\DeXter\bin\Release\net8.0-windows\win-x64\DeXter.exe'
$jar = Join-Path (Split-Path $exe) 'server\dexter-server.jar'
if (-not (Test-Path $jar)) {
    Write-Host "Aviso: server\dexter-server.jar nao foi gerado (falta JDK ou Android SDK?)." -ForegroundColor Yellow
    Write-Host "       O DeXter vai abrir so no modo compatibilidade. Rode server\build-server.ps1 para ver o erro." -ForegroundColor Yellow
}

Write-Host "Abrindo $exe" -ForegroundColor Green
Start-Process $exe
