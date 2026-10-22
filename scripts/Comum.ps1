# SPDX-License-Identifier: GPL-3.0-or-later
# Funcoes usadas pelos scripts do DeXter. Compativel com Windows PowerShell 5.1 e pwsh 7.

$Raiz = Split-Path -Parent $PSScriptRoot
$Projeto = Join-Path $Raiz 'src\DeXter\DeXter.csproj'

function Stop-DeXter {
    # fecha o app e o scrcpy/adb que ele embute (senao arquivos ficam presos)
    Get-Process DeXter -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "Fechando DeXter (pid $($_.Id))..."
        $_.Kill(); $_.WaitForExit(5000) | Out-Null
    }
    Get-Process scrcpy, adb -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path -like '*\DeXter*\*' } |
        ForEach-Object { Write-Host "Fechando $($_.ProcessName) embutido (pid $($_.Id))..."; $_.Kill() }
}

function Remove-PacotesAntigos {
    # versoes antigas instaladas como pacote MSIX (0.3.x / dev / DexBridge) seguram o comando e o icone
    foreach ($nome in 'DeXter', 'DeXter.Dev', 'DexBridge.Dev') {
        $p = Get-AppxPackage $nome -ErrorAction SilentlyContinue
        if ($p) {
            Write-Host "Removendo o pacote antigo $nome $($p.Version)..." -ForegroundColor Yellow
            Get-Process DeXter, DexBridge -ErrorAction SilentlyContinue | Stop-Process -Force
            $p | Remove-AppxPackage
        }
    }
}
