@echo off
title ABCRetailWebApp — Startup
echo ============================================
echo  ABCRetailWebApp — Local Startup (No Docker)
echo ============================================
echo.
echo Checking prerequisites...

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET 8 SDK not found.
    echo Download from: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

where func >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Azure Functions Core Tools not found.
    echo Run: npm install -g azure-functions-core-tools@4 --unsafe-perm true
    pause
    exit /b 1
)

echo Prerequisites OK.
echo.
echo Starting Azure Functions on http://localhost:7071 ...
start "ABCRetail Functions" cmd /k "cd /d "%~dp0ABCRetailWebApp.Functions" && func start"

timeout /t 5 /nobreak >nul

echo Starting Web App on http://localhost:5191 ...
start "ABCRetail WebApp" cmd /k "cd /d "%~dp0ABCRetailWebApp\ABCRetailWebApp" && dotnet run"

echo.
echo Both services starting in separate windows.
echo   Web app  : http://localhost:5191
echo   Functions: http://localhost:7071
echo.
echo Close those windows to stop the application.
pause
