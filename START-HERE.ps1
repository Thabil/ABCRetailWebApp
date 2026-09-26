# ABCRetailWebApp — Local Startup (No Docker)
# Run from the project root: .\START-HERE.ps1

$root = $PSScriptRoot

Write-Host "============================================" -ForegroundColor Cyan
Write-Host " ABCRetailWebApp — Local Startup (No Docker)" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Check dotnet
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "[ERROR] .NET 8 SDK not found." -ForegroundColor Red
    Write-Host "Download from: https://dotnet.microsoft.com/download/dotnet/8.0"
    Read-Host "Press Enter to exit"
    exit 1
}

# Check func
if (-not (Get-Command func -ErrorAction SilentlyContinue)) {
    Write-Host "[ERROR] Azure Functions Core Tools not found." -ForegroundColor Red
    Write-Host "Run: npm install -g azure-functions-core-tools@4 --unsafe-perm true"
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Prerequisites OK." -ForegroundColor Green
Write-Host ""
Write-Host "Starting Azure Functions on http://localhost:7071 ..." -ForegroundColor Yellow
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$root\ABCRetailWebApp.Functions'; func start"

Start-Sleep -Seconds 5

Write-Host "Starting Web App on http://localhost:5191 ..." -ForegroundColor Yellow
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$root\ABCRetailWebApp\ABCRetailWebApp'; dotnet run"

Write-Host ""
Write-Host "Both services starting in separate windows." -ForegroundColor Green
Write-Host "  Web app  : http://localhost:5191"
Write-Host "  Functions: http://localhost:7071"
Write-Host ""
Write-Host "Close those windows to stop the application."
Read-Host "Press Enter to exit this window"
