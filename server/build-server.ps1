# SPDX-License-Identifier: GPL-3.0-or-later
# Compila o servidor do DeX nativo (Java) em server\dexter-server.jar (DEX dentro de um jar).
#
# Precisa de:
#   - JDK 11+            (JAVA_HOME ou javac no PATH)
#   - Android SDK com uma plataforma (android.jar) e build-tools (d8)
#       ANDROID_HOME / ANDROID_SDK_ROOT, ou %LOCALAPPDATA%\Android\Sdk
#       sdkmanager "platforms;android-35" "build-tools;35.0.0"
#
# Uso: powershell -ExecutionPolicy Bypass -File server\build-server.ps1

param([string]$Saida = (Join-Path $PSScriptRoot 'dexter-server.jar'))

$ErrorActionPreference = 'Stop'

# --- JDK
$javac = $null
if ($env:JAVA_HOME -and (Test-Path (Join-Path $env:JAVA_HOME 'bin\javac.exe'))) {
    $javac = Join-Path $env:JAVA_HOME 'bin\javac.exe'
} elseif (Get-Command javac -ErrorAction SilentlyContinue) {
    $javac = (Get-Command javac).Source
} else {
    $studio = 'C:\Program Files\Android\Android Studio\jbr\bin\javac.exe'
    if (Test-Path $studio) { $javac = $studio }
}
if (-not $javac) { throw "javac nao encontrado. Instale um JDK 11+ e defina JAVA_HOME." }

# --- Android SDK
$sdk = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $env:LOCALAPPDATA 'Android\Sdk')) |
    Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $sdk) { throw "Android SDK nao encontrado (ANDROID_HOME, ANDROID_SDK_ROOT ou %LOCALAPPDATA%\Android\Sdk)." }

$androidJar = Get-ChildItem (Join-Path $sdk 'platforms') -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^android-(\d+)$' -and (Test-Path (Join-Path $_.FullName 'android.jar')) } |
    Sort-Object { [int]($_.Name -replace '\D', '') } -Descending |
    ForEach-Object { Join-Path $_.FullName 'android.jar' } | Select-Object -First 1
if (-not $androidJar) { throw "Nenhuma plataforma no SDK. Rode: sdkmanager ""platforms;android-35""" }

$d8 = Get-ChildItem (Join-Path $sdk 'build-tools') -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' -and (Test-Path (Join-Path $_.FullName 'd8.bat')) } |
    Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName 'd8.bat' } | Select-Object -First 1
if (-not $d8) { throw "d8 nao encontrado no SDK. Rode: sdkmanager ""build-tools;35.0.0""" }

Write-Host "javac      : $javac"
Write-Host "android.jar: $androidJar"
Write-Host "d8         : $d8"

# --- compila
$tmp = Join-Path $PSScriptRoot 'build'
$classes = Join-Path $tmp 'classes'
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $classes | Out-Null

$fontes = Get-ChildItem (Join-Path $PSScriptRoot 'src') -Recurse -Filter *.java | ForEach-Object FullName
& $javac --release 11 -encoding UTF-8 -cp $androidJar -d $classes $fontes
if ($LASTEXITCODE -ne 0) { throw "javac falhou ($LASTEXITCODE)" }

# --- DEX
$arquivosClass = Get-ChildItem $classes -Recurse -Filter *.class | ForEach-Object FullName
$saidaTmp = Join-Path $tmp 'dexter-server.jar'
& $d8 --release --min-api 30 --lib $androidJar --output $saidaTmp $arquivosClass
if ($LASTEXITCODE -ne 0) { throw "d8 falhou ($LASTEXITCODE)" }

Copy-Item $saidaTmp $Saida -Force
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "OK: $Saida ($([math]::Round((Get-Item $Saida).Length / 1KB, 1)) KB)" -ForegroundColor Green
