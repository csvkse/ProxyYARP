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

echo [2/4] Starting ProxyYARP Control Plane (Port: 8080)...
start "ProxyYARP - ControlPlane" cmd /k "set NODE_ID=node-control-1&& set Management__Enabled=true&& set Management__GroupId=default&& set Management__NodeName=ControlPlane&& set DB_TYPE=sqlite&& set DB_CONNECTION=Data Source=%~dp0DB\test.db&& dotnet run --no-build --no-launch-profile --project src\ProxyYARP\ProxyYARP.csproj -- -p 8080"

echo Waiting 5 seconds for Control Plane (and DB Init) to initialize...
timeout /t 5 /nobreak > nul

echo [3/4] Starting ProxyYARP Worker Node 1 (Port: 8081)...
start "ProxyYARP - Worker 1" cmd /k "set NODE_ID=node-worker-1&& set Management__Enabled=false&& set Management__GroupId=default&& set Management__NodeName=Worker-1&& set DB_TYPE=sqlite&& set DB_CONNECTION=Data Source=%~dp0DB\test.db&& dotnet run --no-build --no-launch-profile --project src\ProxyYARP\ProxyYARP.csproj -- -p 8081"

echo [4/4] Starting ProxyYARP Worker Node 2 (Port: 8082)...
start "ProxyYARP - Worker 2" cmd /k "set NODE_ID=node-worker-2&& set Management__Enabled=false&& set Management__GroupId=default&& set Management__NodeName=Worker-2&& set DB_TYPE=sqlite&& set DB_CONNECTION=Data Source=%~dp0DB\test.db&& dotnet run --no-build --no-launch-profile --project src\ProxyYARP\ProxyYARP.csproj -- -p 8082"

echo.
echo ===================================================
echo   Done! 1 Control Plane and 2 Worker Nodes are running.
echo   You can open your browser to manage them at:
echo   http://localhost:8080/
echo.
echo   Workers are listening on:
echo   http://localhost:8081/ and http://localhost:8082/
echo ===================================================
echo.
echo   [!] Press ANY KEY in this window to RESTART all.
echo   [!] Close this window to exit completely.
echo ===================================================
pause > nul
goto MENU
