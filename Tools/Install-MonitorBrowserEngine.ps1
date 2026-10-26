$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$packageName = 'dev.voltstro.unitywebbrowser.engine.cef.win.x64'
$packageVersion = '2.2.8'
$packageDirectory = Join-Path $projectRoot "Packages/$packageName"
$engineDirectory = Join-Path $packageDirectory 'Engine~'

if (Test-Path -LiteralPath (Join-Path $engineDirectory 'UnityWebBrowser.Engine.Cef.exe')) {
    Write-Host 'Monitor browser engine is already installed.'
    exit 0
}

$downloadDirectory = Join-Path $projectRoot ('Temp/MonitorBrowserInstall-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null

# 공개 패키지 저장소에서 고정된 버전과 무결성 값을 조회한다.
$metadata = Invoke-RestMethod -Uri "https://upr.voltstro.dev/$packageName"
$release = $metadata.versions.PSObject.Properties[$packageVersion].Value
if (-not $release -or -not $release.dist.shasum) {
    throw "Package metadata is missing for $packageName $packageVersion."
}
$archivePath = Join-Path $downloadDirectory 'engine.tgz'
Invoke-WebRequest -Uri $release.dist.tarball -OutFile $archivePath
$actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA1).Hash
if ($actualHash -ine $release.dist.shasum) {
    throw 'Browser engine archive checksum mismatch.'
}

# 검증된 압축 파일에서 엔진과 라이선스 리소스를 함께 복원한다.
& tar -xzf $archivePath -C $downloadDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'Browser engine archive extraction failed.'
}
$extractedEngine = Join-Path $downloadDirectory 'package/Engine~'
if (-not (Test-Path -LiteralPath (Join-Path $extractedEngine 'UnityWebBrowser.Engine.Cef.exe'))) {
    throw 'Browser engine executable is missing from the archive.'
}
Copy-Item -LiteralPath $extractedEngine -Destination $packageDirectory -Recurse -Force
Write-Host 'Monitor browser engine installed. Reopen Unity before using the monitor.'
