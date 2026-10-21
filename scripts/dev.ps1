# SPDX-License-Identifier: GPL-3.0-or-later
# Desenvolvimento: compila e registra o app a partir da pasta de build (precisa do Modo de desenvolvedor).
# Instala como "DeXter (dev)" com o comando dexter-dev, separado da versao instalada pelo MSIX.
#
# Uso: powershell -ExecutionPolicy Bypass -File scripts\dev.ps1

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Comum.ps1')

Stop-DeXter

# o prototipo antigo (DexBridge) segura o receptor Miracast se estiver registrado/rodando
$antigo = Get-AppxPackage DexBridge.Dev -ErrorAction SilentlyContinue
if ($antigo) {
    Write-Host "Removendo o pacote antigo DexBridge.Dev..." -ForegroundColor Yellow
    Get-Process DexBridge -ErrorAction SilentlyContinue | Stop-Process -Force
    $antigo | Remove-AppxPackage
}

Write-Host "Compilando..." -ForegroundColor Cyan
dotnet build $Projeto -c Release
if ($LASTEXITCODE -ne 0) { throw "Build falhou." }

$saida = Join-Path $Raiz 'src\DeXter\bin\Release\net8.0-windows10.0.19041.0\win-x64'
$versao = (Get-Item (Join-Path $saida 'DeXter.dll')).VersionInfo.FileVersion   # x.y.z.0

Copy-Assets $saida
New-Manifesto -Destino $saida -Nome 'DeXter.Dev' -Publicador 'CN=DeXterDev' -Versao $versao `
              -Titulo 'DeXter (dev)' -Alias 'dexter-dev.exe'
New-ResourcesPri $saida -Opcional

Write-Host "Registrando o pacote de desenvolvimento..." -ForegroundColor Cyan
try {
    Add-AppxPackage -Register (Join-Path $saida 'AppxManifest.xml') -ForceApplicationShutdown -ForceUpdateFromAnyVersion
} catch {
    Write-Host "Registro direto falhou; removendo e registrando de novo..." -ForegroundColor Yellow
    Get-AppxPackage DeXter.Dev | Remove-AppxPackage
    Add-AppxPackage -Register (Join-Path $saida 'AppxManifest.xml')
}

$pkg = Get-AppxPackage DeXter.Dev
Write-Host "OK: $($pkg.Name) $($pkg.Version)" -ForegroundColor Green
Write-Host "Inicie com: dexter-dev   (ou menu Iniciar -> DeXter (dev))"
