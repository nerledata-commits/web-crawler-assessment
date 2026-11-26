# Runs the system on Windows without Docker:
#   API     http://localhost:5080  (Swagger: /swagger, health: /health)
#   Worker  http://localhost:5081  (health: /health)
#   UI      http://localhost:5173
# Prerequisites: .NET SDK 8+, Node.js 20+, RabbitMQ running on localhost:5672 (guest/guest).
# Database: SQLite file at data/crawler.db (docker-compose uses PostgreSQL instead).
# Each process opens in its own window; close the windows to stop.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# Reload PATH from the registry: terminals opened before Node was installed don't see npm yet.
# Child windows started below inherit this PATH.
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    throw 'npm not found. Install Node.js 20+ (https://nodejs.org) and re-run.'
}

$rabbit = Test-NetConnection -ComputerName localhost -Port 5672 -WarningAction SilentlyContinue
if (-not $rabbit.TcpTestSucceeded) {
    Write-Warning 'RabbitMQ is not reachable on localhost:5672. Start the RabbitMQ service first.'
}

# Some networks inspect TLS with their own root certificate; make Node trust the Windows store for npm.
$env:NODE_OPTIONS = '--use-system-ca'

if (-not (Test-Path "$root\web\node_modules")) {
    Push-Location "$root\web"
    npm ci
    Pop-Location
}

dotnet build "$root\Crawler.sln" -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$env:ASPNETCORE_ENVIRONMENT = 'Development'
Start-Process powershell -ArgumentList '-NoExit', '-Command', "dotnet run --no-build --project '$root\src\Crawler.Api'"
Start-Process powershell -ArgumentList '-NoExit', '-Command', "dotnet run --no-build --project '$root\src\Crawler.Worker'"
Start-Process powershell -ArgumentList '-NoExit', '-Command', "`$env:NODE_OPTIONS='--use-system-ca'; Set-Location '$root\web'; npm run dev"

Write-Host 'Started API (5080), Worker (5081) and UI (5173). Open http://localhost:5173'
