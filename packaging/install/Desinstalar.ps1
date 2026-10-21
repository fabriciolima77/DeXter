# DeXter - desinstalador
# Uso: clique com o botao direito > Executar com o PowerShell
#      Para tirar tambem o certificado:  powershell -ExecutionPolicy Bypass -File Desinstalar.ps1 -RemoverCertificado

param([switch]$RemoverCertificado)

Get-Process DeXter -ErrorAction SilentlyContinue | Stop-Process -Force
$pkg = Get-AppxPackage DeXter
if ($pkg) {
    $pkg | Remove-AppxPackage
    Write-Host "DeXter desinstalado." -ForegroundColor Green
} else {
    Write-Host "DeXter nao esta instalado."
}

if ($RemoverCertificado) {
    $cer = Join-Path $PSScriptRoot 'DeXter.cer'
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cer)
    $cmd = "Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq '$($cert.Thumbprint)' | Remove-Item"
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $cmd
    Write-Host "Certificado removido."
}
