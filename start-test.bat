@echo off
title ProxyYARP One-Click Multi-Node Test

:MENU
cls
echo ===================================================
echo   ProxyYARP - Local Multi-Node Test Startup
echo ===================================================
echo.

echo [0/4] Cleaning up previous instances (Restarting)...
taskkill /F /FI "WINDOWTITLE eq ProxyYARP - ControlPlane*" /T >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq ProxyYARP - Worker*" /T >nul 2>&1
taskkill /F /IM ProxyYARP.exe /T >nul 2>&1
timeout /t 1 /nobreak > nul

if not exist "DB" mkdir DB

echo [1/4] Building project to prevent lock conflicts...
dotnet build src\ProxyYARP\ProxyYARP.csproj
if %ERRORLEVEL% neq 0 (
    echo Build failed!
    pause
    goto MENU
)

echo [2/4] Starting ProxyYARP Web Control Plane (Port: 8080, Group: group-web)...
start "ProxyYARP - Web ControlPlane" cmd /k "set NODE_ID=node-web-cp&& set Management__Enabled=true&& set MANAGEMENT_PATH=/_proxyadmin&& set GROUP_ID=group-web&& set NODE_NAME=Web-ControlPlane&& set DB_TYPE=sqlite&& set DB_CONNECTION=Data Source=%~dp0DB\test.db&& dotnet run --no-build --no-launch-profile --project src\ProxyYARP\ProxyYARP.csproj -- -p 8080"

echo Waiting 5 seconds for Control Plane (and DB Init) to initialize...
timeout /t 5 /nobreak > nul

echo [3/4] Starting ProxyYARP Web Worker (Port: 8081, Group: group-web)...
start "ProxyYARP - Web Worker" cmd /k "set NODE_ID=node-web-worker&& set Management__Enabled=false&& set GROUP_ID=group-web&& set NODE_NAME=Web-Worker&& set DB_TYPE=sqlite&& set DB_CONNECTION=Data Source=%~dp0DB\test.db&& dotnet run --no-build --no-launch-profile --project src\ProxyYARP\ProxyYARP.csproj -- -p 8081"

echo [4/4] Starting ProxyYARP Api Worker (Port: 8082, Group: group-api)...
start "ProxyYARP - Api Worker" cmd /k "set NODE_ID=node-api-worker&& set Management__Enabled=false&& set GROUP_ID=group-api&& set NODE_NAME=Api-Worker&& set DB_TYPE=sqlite&& set DB_CONNECTION=Data Source=%~dp0DB\test.db&& dotnet run --no-build --no-launch-profile --project src\ProxyYARP\ProxyYARP.csproj -- -p 8082"

echo.
echo ===================================================
echo   Done! 1 Control Plane and 2 Worker Nodes are running.
echo   You can open your browser to manage them at:
echo   http://localhost:8080/_proxyadmin/
echo.
echo   Workers are listening on:
echo   - [group-web] Worker : http://localhost:8081/
echo   - [group-api] Worker : http://localhost:8082/
echo ===================================================
echo.
echo   [!] Press ANY KEY in this window to RESTART all.
echo   [!] Close this window to exit completely.
echo ===================================================
pause > nul
goto MENU
