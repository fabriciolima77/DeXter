# DeXter - instalador
# Confia no certificado do projeto (pede permissao de administrador uma vez) e instala o pacote.

$ErrorActionPreference = 'Stop'
$aqui = $PSScriptRoot

try {
    $msix = Get-ChildItem $aqui -Filter 'DeXter-*.msix' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $msix) { throw "Nao achei o arquivo DeXter-*.msix nesta pasta. Extraia o zip inteiro antes de instalar." }
    $cer = Join-Path $aqui 'DeXter.cer'
    if (-not (Test-Path $cer)) { throw "Nao achei o DeXter.cer nesta pasta." }

    if ([Environment]::OSVersion.Version.Build -lt 19041) {
        throw "O DeXter precisa do Windows 10 versao 2004 (build 19041) ou mais novo."
    }

    # 1. certificado: em "Pessoas Confiaveis" e em "Raiz Confiavel".
    #    Por ser autoassinado ele e a propria raiz da cadeia; sem estar na Raiz alguns Windows
    #    recusam o pacote com 0x800B010A. O certificado so assina codigo (nao pode emitir outros).
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cer)
    $lojas = 'TrustedPeople', 'Root'
    $faltando = $lojas | Where-Object {
        -not (Get-ChildItem "Cert:\LocalMachine\$_" | Where-Object Thumbprint -eq $cert.Thumbprint)
    }
    if ($faltando) {
        Write-Host "1/2 Confiando no certificado do DeXter ($($cert.Subject))..." -ForegroundColor Cyan
        Write-Host "    O Windows vai pedir permissao de administrador."
        $caminho = $cer.Replace("'", "''")
        $cmd = ($faltando | ForEach-Object {
            "Import-Certificate -FilePath '$caminho' -CertStoreLocation Cert:\LocalMachine\$_ | Out-Null"
        }) -join '; '
        $p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru `
            -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $cmd
        if ($p.ExitCode -ne 0) { throw "Nao foi possivel confiar no certificado (permissao negada?)." }
    } else {
        Write-Host "1/2 Certificado do DeXter ja e confiavel." -ForegroundColor Cyan
    }

    # 2. pacote (instala ou atualiza)
    Write-Host "2/2 Instalando $($msix.Name)..." -ForegroundColor Cyan
    Get-Process DeXter -ErrorAction SilentlyContinue | Stop-Process -Force
    Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown -ForceUpdateFromAnyVersion

    $pkg = Get-AppxPackage DeXter
    Write-Host ""
    Write-Host "DeXter $($pkg.Version) instalado!" -ForegroundColor Green
    Write-Host "Abra pelo menu Iniciar (DeXter) ou digite  dexter  no terminal."
    Write-Host "No celular: DeX > conectar a TV/monitor > DeXter."
    Start-Process "shell:AppsFolder\$($pkg.PackageFamilyName)!App"
} catch {
    Write-Host ""
    Write-Host "Erro: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
