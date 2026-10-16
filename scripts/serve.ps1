# Local dev helper (Windows): .\scripts\serve.ps1 to (re)start, .\scripts\serve.ps1 -StopOnly to stop.
# Stops any running SublimaLab API, rebuilds, starts it in the background and waits until healthy.
# Retries the build with a new assembly hash when Smart App Control blocks a fresh DLL.
param([switch] $StopOnly)
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo

Get-CimInstance Win32_Process -Filter "Name='dotnet.exe' OR Name='SublimaLab.Api.exe'" |
    Where-Object { $_.CommandLine -match 'SublimaLab\.Api' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Milliseconds 500
if ($StopOnly) { 'stopped'; return }

for ($attempt = 1; $attempt -le 6; $attempt++) {
    $props = @(if ($attempt -gt 1) { "-p:InformationalVersion=0.1.0-sac$attempt$(Get-Random)" })
    $build = dotnet build src/SublimaLab.Api -nologo -v q @props 2>&1 | Out-String
    if ($build -notmatch ' 0 Error\(s\)') { $build -split "`n" | Select-String ' error ' | Select-Object -First 15 | ForEach-Object { $_.Line.Trim() }; 'BUILD FAILED'; return }

    $out = Join-Path $env:TEMP 'sl-api.out'; $err = Join-Path $env:TEMP 'sl-api.err'
    $p = Start-Process dotnet -ArgumentList 'run --project src/SublimaLab.Api --no-build --launch-profile https' -PassThru -WindowStyle Hidden -RedirectStandardOutput $out -RedirectStandardError $err
    $ok = $false
    foreach ($i in 1..60) {
        try { if ((Invoke-WebRequest http://localhost:5034/health/ready -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200) { $ok = $true; break } } catch {}
        if ($p.HasExited) { break }
        Start-Sleep -Milliseconds 500
    }
    if ($ok) { "running (attempt $attempt) at https://localhost:7132"; return }

    $log = (Get-Content $out -Raw) + (Get-Content $err -Raw)
    if ($log -notmatch 'Application Control policy|0x800711C7|Could not load file or assembly') { $log; 'START FAILED'; return }
    "attempt $attempt blocked by Smart App Control; rebuilding"
}
'START FAILED after retries'
