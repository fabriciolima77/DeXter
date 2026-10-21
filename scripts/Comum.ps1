# SPDX-License-Identifier: GPL-3.0-or-later
# Funcoes usadas pelos scripts do DeXter (dev.ps1, pack.ps1). Compativel com Windows PowerShell 5.1 e pwsh 7.

$Raiz = Split-Path -Parent $PSScriptRoot
$Projeto = Join-Path $Raiz 'src\DeXter\DeXter.csproj'
$Empacotamento = Join-Path $Raiz 'packaging'
$ModeloManifesto = Join-Path $Empacotamento 'AppxManifest.template.xml'
$PastaAssets = Join-Path $Empacotamento 'Assets'
$CertificadoPublico = Join-Path $Empacotamento 'DeXter.cer'

function Find-FerramentaSdk([string]$Nome) {
    # makeappx.exe, makepri.exe, signtool.exe: versao mais nova do Windows SDK (x64)
    $base = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path $base) {
        $achado = Get-ChildItem $base -Directory |
            Where-Object { $_.Name -match '^10\.' } |
            Sort-Object { [version]$_.Name } -Descending |
            ForEach-Object { Join-Path $_.FullName "x64\$Nome" } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($achado) { return $achado }
    }
    $noPath = Get-Command $Nome -ErrorAction SilentlyContinue
    if ($noPath) { return $noPath.Source }
    return $null
}

function Get-AssuntoCertificado([string]$Cer) {
    $c = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($Cer)
    return $c.Subject
}

function Stop-DeXter {
    # fecha o app e o scrcpy/adb que ele embute (senao arquivos ficam presos)
    Get-Process DeXter -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "Fechando DeXter (pid $($_.Id))..."
        $_.Kill(); $_.WaitForExit(5000) | Out-Null
    }
    Get-Process scrcpy, adb -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and ($_.Path -like '*\DeXter*\*' -or $_.Path -like '*\WindowsApps\*DeXter*') } |
        ForEach-Object { Write-Host "Fechando $($_.ProcessName) embutido (pid $($_.Id))..."; $_.Kill() }
}

function New-Manifesto {
    param(
        [Parameter(Mandatory)] [string]$Destino,
        [Parameter(Mandatory)] [string]$Nome,
        [Parameter(Mandatory)] [string]$Publicador,
        [Parameter(Mandatory)] [string]$Versao,
        [Parameter(Mandatory)] [string]$Titulo,
        [Parameter(Mandatory)] [string]$Alias
    )
    $xml = [IO.File]::ReadAllText($ModeloManifesto)
    $publicadorXml = [Security.SecurityElement]::Escape($Publicador)
    $xml = $xml.Replace('__NOME__', $Nome).Replace('__PUBLICADOR__', $publicadorXml).
                Replace('__VERSAO__', $Versao).Replace('__TITULO__', $Titulo).Replace('__ALIAS__', $Alias)
    [IO.File]::WriteAllText((Join-Path $Destino 'AppxManifest.xml'), $xml, (New-Object Text.UTF8Encoding($false)))
}

function Copy-Assets([string]$Destino) {
    $alvo = Join-Path $Destino 'Assets'
    New-Item -ItemType Directory -Force $alvo | Out-Null
    Get-ChildItem $PastaAssets -Filter '*.png' |
        Where-Object { $_.Name -notlike 'DeXter-*' } |   # master/256/512 nao vao para o pacote
        Copy-Item -Destination $alvo -Force
}

function New-ResourcesPri([string]$Layout, [switch]$Opcional) {
    # resources.pri: faz o Windows escolher o icone certo para cada tamanho/escala
    $makepri = Find-FerramentaSdk 'makepri.exe'
    if (-not $makepri) {
        if ($Opcional) {
            Write-Host "makepri.exe nao encontrado (Windows SDK): seguindo sem resources.pri (icones so em tamanho base)." -ForegroundColor Yellow
            return
        }
        throw "makepri.exe nao encontrado. Instale o Windows SDK (winget install Microsoft.WindowsSDK.10.0.26100)."
    }
    # indexa so manifesto + Assets (num diretorio temporario) para nao varrer o scrcpy embutido
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("dexter-pri-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $tmp | Out-Null
    try {
        Copy-Item (Join-Path $Layout 'AppxManifest.xml') $tmp
        Copy-Item (Join-Path $Layout 'Assets') $tmp -Recurse
        & $makepri createconfig /cf "$tmp\priconfig.xml" /dq pt-BR /pv 10.0.0 /o | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "makepri createconfig falhou ($LASTEXITCODE)" }
        & $makepri new /pr "$tmp" /cf "$tmp\priconfig.xml" /mn "$tmp\AppxManifest.xml" /of "$Layout\resources.pri" /o | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "makepri new falhou ($LASTEXITCODE)" }
        Write-Host "resources.pri gerado."
    } finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
