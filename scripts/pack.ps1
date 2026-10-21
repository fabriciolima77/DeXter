# SPDX-License-Identifier: GPL-3.0-or-later
# Gera o instalador do DeXter: MSIX assinado + zip pronto para o release do GitHub.
#
#   dist\DeXter-<versao>-x64.msix   pacote assinado
#   dist\DeXter-<versao>-x64.zip    msix + DeXter.cer + Instalar.cmd/ps1 + Desinstalar.ps1 + LEIA-ME.txt
#
# Assinatura (nesta ordem):
#   1. -Pfx <arquivo> -SenhaPfx <senha>
#   2. variaveis SIGNING_PFX_BASE64 / SIGNING_PFX_PASSWORD (GitHub Actions)
#   3. cert\DeXter.pfx (pede a senha)
#
# Uso local: powershell -ExecutionPolicy Bypass -File scripts\pack.ps1 -Versao 0.3.0
# Precisa do .NET 8 SDK e do Windows SDK (makeappx, makepri, signtool).

param(
    [Parameter(Mandatory)] [string]$Versao,
    [string]$Pfx,
    [string]$SenhaPfx
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Comum.ps1')

if ($Versao -notmatch '^\d+\.\d+\.\d+$') { throw "Versao deve ser x.y.z (ex.: 0.3.0). Recebido: $Versao" }
$versaoPacote = "$Versao.0"

$dist = Join-Path $Raiz 'dist'
$obj = Join-Path $Raiz 'obj-pack'
$publicado = Join-Path $obj 'publish'
$layout = Join-Path $obj 'layout'
Remove-Item $obj, $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dist, $layout | Out-Null

$makeappx = Find-FerramentaSdk 'makeappx.exe'
$signtool = Find-FerramentaSdk 'signtool.exe'
if (-not $makeappx -or -not $signtool) {
    throw "makeappx.exe/signtool.exe nao encontrados. Instale o Windows SDK (winget install Microsoft.WindowsSDK.10.0.26100)."
}
if (-not (Test-Path $CertificadoPublico)) {
    throw "Falta $CertificadoPublico. Rode scripts\New-Certificado.ps1 uma vez e commite o .cer."
}
$publicador = Get-AssuntoCertificado $CertificadoPublico
Write-Host "Versao $Versao, publicador '$publicador'" -ForegroundColor Cyan

# 1. publica self-contained (quem instala nao precisa do .NET) + scrcpy/adb embutidos
Write-Host "Publicando..." -ForegroundColor Cyan
dotnet publish $Projeto -c Release -r win-x64 --self-contained true `
    -p:Version=$Versao -p:DebugType=none -p:DebugSymbols=false -o $publicado
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou." }
if (-not (Test-Path (Join-Path $publicado 'scrcpy\scrcpy.exe'))) { throw "scrcpy embutido nao foi copiado para o publish." }

# 2. layout do pacote: app + manifesto + icones + resources.pri
Copy-Item "$publicado\*" $layout -Recurse
Copy-Assets $layout
New-Manifesto -Destino $layout -Nome 'DeXter' -Publicador $publicador -Versao $versaoPacote `
              -Titulo 'DeXter' -Alias 'dexter.exe'
New-ResourcesPri $layout

# 3. empacota
$msix = Join-Path $dist "DeXter-$Versao-x64.msix"
Write-Host "Empacotando $msix..." -ForegroundColor Cyan
& $makeappx pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx falhou." }

# 4. assina
$pfxTemp = $null
if (-not $Pfx) {
    if ($env:SIGNING_PFX_BASE64) {
        $pfxTemp = Join-Path $obj 'assinatura.pfx'
        [IO.File]::WriteAllBytes($pfxTemp, [Convert]::FromBase64String($env:SIGNING_PFX_BASE64))
        $Pfx = $pfxTemp
        $SenhaPfx = $env:SIGNING_PFX_PASSWORD
    } elseif (Test-Path (Join-Path $Raiz 'cert\DeXter.pfx')) {
        $Pfx = Join-Path $Raiz 'cert\DeXter.pfx'
    } else {
        throw "Sem certificado para assinar (veja o cabecalho deste script)."
    }
}
if (-not $SenhaPfx) {
    $seg = Read-Host -AsSecureString "Senha de $Pfx"
    $SenhaPfx = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($seg))
}

# confere se o .pfx e o mesmo do .cer publico (senao o instalador nao confia)
$certPfx = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($Pfx, $SenhaPfx)
$certCer = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertificadoPublico)
if ($certPfx.Thumbprint -ne $certCer.Thumbprint) {
    throw "O .pfx ($($certPfx.Thumbprint)) nao corresponde a packaging\DeXter.cer ($($certCer.Thumbprint))."
}

Write-Host "Assinando..." -ForegroundColor Cyan
try {
    & $signtool sign /fd SHA256 /f $Pfx /p $SenhaPfx /tr http://timestamp.digicert.com /td SHA256 $msix
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Assinatura com carimbo de tempo falhou; assinando sem carimbo." -ForegroundColor Yellow
        & $signtool sign /fd SHA256 /f $Pfx /p $SenhaPfx $msix
        if ($LASTEXITCODE -ne 0) { throw "signtool falhou." }
    }
} finally {
    if ($pfxTemp) { Remove-Item $pfxTemp -Force -ErrorAction SilentlyContinue }
}

# 5. zip do release (o que o usuario baixa)
$pastaZip = Join-Path $obj 'zip'
New-Item -ItemType Directory -Force $pastaZip | Out-Null
Copy-Item $msix, $CertificadoPublico $pastaZip
Copy-Item (Join-Path $Empacotamento 'install\*') $pastaZip
Copy-Item (Join-Path $Raiz 'LICENSE') (Join-Path $pastaZip 'LICENSE.txt')
Copy-Item (Join-Path $Raiz 'THIRD_PARTY_NOTICES.md') $pastaZip
$zip = Join-Path $dist "DeXter-$Versao-x64.zip"
Compress-Archive -Path "$pastaZip\*" -DestinationPath $zip -Force
Copy-Item $CertificadoPublico $dist

# hashes para quem quiser conferir o download
Get-ChildItem $dist -File | Where-Object Extension -ne '.txt' | ForEach-Object {
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)"
} | Set-Content (Join-Path $dist 'SHA256SUMS.txt')

Write-Host ""
Write-Host "Pronto:" -ForegroundColor Green
Get-ChildItem $dist | ForEach-Object { Write-Host ("  {0,-32} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB)) }
