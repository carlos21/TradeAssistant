# Runs the TradeAssistant test suite with coverage gated at 98% line coverage
# of the TradeAssistant.Core library.
$ErrorActionPreference = 'Stop'

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
}
$testsDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Push-Location $testsDir
try {
    & $dotnet test TradeAssistant.Tests/TradeAssistant.Tests.csproj `
        -p:CollectCoverage=true `
        -p:Threshold=98 `
        -p:ThresholdType=line `
        -p:ThresholdStat=total
    $exit = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($exit -eq 0) {
    Write-Host "`nCoverage gate PASSED (>= 98% line coverage of TradeAssistant.Core)." -ForegroundColor Green
} else {
    Write-Host "`nTests or coverage gate FAILED (exit $exit)." -ForegroundColor Red
}
exit $exit
