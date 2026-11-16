<#
  Medora - one-time setup on a new Windows machine.
  Run from this folder in PowerShell:   powershell -ExecutionPolicy Bypass -File .\setup.ps1
  Installs project dependencies (not system tools) and runs the tests.
#>
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Need($cmd, $hint) {
  if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) { Write-Host "MISSING: $cmd -> $hint" -ForegroundColor Red; $script:missing = $true }
}
$missing = $false
Need node   'Install Node.js 20 or newer: https://nodejs.org (or: winget install OpenJS.NodeJS.LTS)'
Need dotnet '.NET SDK 10: https://dotnet.microsoft.com/download (or: winget install Microsoft.DotNet.SDK.10)'
if ($missing) { Write-Host "Install the missing tools, open a NEW terminal, and run setup again." -ForegroundColor Yellow; exit 1 }

Write-Host "`n== API (.NET) ==" -ForegroundColor Cyan
Push-Location backend
dotnet tool restore
dotnet test Medora.slnx
Pop-Location

Write-Host "`n== Mobile app (Expo) ==" -ForegroundColor Cyan
Push-Location mobile
npm ci --no-audit --no-fund
npx tsc --noEmit
Pop-Location

Write-Host "`n== Database ==" -ForegroundColor Cyan
if (Get-Command sqllocaldb -ErrorAction SilentlyContinue) {
  sqllocaldb start MSSQLLocalDB | Out-Null
  Write-Host "SQL Server LocalDB is ready (the API creates and seeds the database on first start)."
} else {
  Write-Host "SQL Server LocalDB not found. Either install it (see SETUP.md) or run SQL Server in Docker:" -ForegroundColor Yellow
  Write-Host "  docker run -d --name medora-sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Dev_Passw0rd!' -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest"
  Write-Host "  dotnet user-secrets set ConnectionStrings:Medora 'Server=localhost,1433;Database=Medora;User Id=sa;Password=Dev_Passw0rd!;TrustServerCertificate=True' --project backend/src/Medora.Api"
}

Write-Host "`n== AI key (only thing to re-enter on a new device) ==" -ForegroundColor Cyan
$hasKey = (dotnet user-secrets list --project backend/src/Medora.Api 2>$null) -match '^Ai:ApiKey'
if ($hasKey) { Write-Host "AI key already set on this machine." }
else { Write-Host "Set your free Groq key (https://console.groq.com -> API Keys) - run this yourself, don't share the key:" -ForegroundColor Yellow
       Write-Host "  dotnet user-secrets set Ai:ApiKey <gsk_...> --project backend/src/Medora.Api" }

Write-Host "`nDone. Start it with two terminals:" -ForegroundColor Green
Write-Host "  1) dotnet run --project backend/src/Medora.Api --launch-profile http   (API on http://localhost:5280)"
Write-Host "  2) cd mobile; npm run web       (or: npx expo start  -> scan the QR code with Expo Go / press a for Android)"
