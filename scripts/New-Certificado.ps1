# SPDX-License-Identifier: GPL-3.0-or-later
# Mantenedor, UMA vez: cria o certificado autoassinado que assina os pacotes MSIX do DeXter.
#
#  - packaging\DeXter.cer  -> parte publica. VAI para o git e para cada release (o instalador confia nele).
#  - cert\DeXter.pfx       -> chave privada. NUNCA vai para o git (.gitignore). Guarde um backup seguro.
#  - cert\DeXter.pfx.base64.txt -> para colar no secret SIGNING_PFX_BASE64 do GitHub.
#
# Trocar de certificado depois obriga os usuarios a confiar no novo, entao use validade longa.
#
# Uso: powershell -ExecutionPolicy Bypass -File scripts\New-Certificado.ps1

param(
    [string]$Assunto = 'CN=fabriciolima77',
    [int]$Anos = 10
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Comum.ps1')

$pastaPrivada = Join-Path $Raiz 'cert'
$pfx = Join-Path $pastaPrivada 'DeXter.pfx'
if (Test-Path $pfx) { throw "Ja existe $pfx. Apague manualmente se quiser mesmo trocar o certificado." }
New-Item -ItemType Directory -Force $pastaPrivada | Out-Null

$cert = New-SelfSignedCertificate -Type Custom -Subject $Assunto `
    -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 3072 `
    -FriendlyName 'DeXter - assinatura de pacotes' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') `
    -NotAfter (Get-Date).AddYears($Anos)

$senha = Read-Host -AsSecureString 'Escolha uma senha para o arquivo .pfx'
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $senha | Out-Null
Export-Certificate -Cert $cert -FilePath $CertificadoPublico | Out-Null
[Convert]::ToBase64String([IO.File]::ReadAllBytes($pfx)) | Set-Content (Join-Path $pastaPrivada 'DeXter.pfx.base64.txt') -NoNewline

# a chave fica so no .pfx; tira do repositorio de certificados do Windows
Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)"

Write-Host ""
Write-Host "Certificado criado: $Assunto (valido ate $($cert.NotAfter.ToString('dd/MM/yyyy')))" -ForegroundColor Green
Write-Host "  publico : $CertificadoPublico   (commitar)"
Write-Host "  privado : $pfx   (NAO commitar; faca backup)"
Write-Host ""
Write-Host "No GitHub: Settings > Secrets and variables > Actions > New repository secret"
Write-Host "  SIGNING_PFX_BASE64   = conteudo de cert\DeXter.pfx.base64.txt"
Write-Host "  SIGNING_PFX_PASSWORD = a senha que voce acabou de digitar"
if (Get-Command gh -ErrorAction SilentlyContinue) {
    Write-Host ""
    Write-Host "Ou com o GitHub CLI, na pasta do repositorio:"
    Write-Host "  Get-Content cert\DeXter.pfx.base64.txt | gh secret set SIGNING_PFX_BASE64"
    Write-Host "  gh secret set SIGNING_PFX_PASSWORD"
}
