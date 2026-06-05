@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

:: ========== CONFIG ==========
set "EXE_PATH=C:\Program Files\Maxon Cinema 4D 2026\Cinema 4D Team Render Client.exe"
set "PORT=5401"
set "PROC_NAME=Cinema 4D Team Render Client.exe"
:: ============================

set "CHECK_INTERVAL=300"
set "STARTUP_DELAY=60"
set "MAX_FAILURES=3"
set "LOG_FILE=c4d_monitor.log"
set "MAX_LOG_SIZE=10485760"

:: ---------- INIT ----------
title C4D Client Monitor
cls
echo ========================================
echo   C4D Team Render Client Monitor
echo   Port: %PORT%  Check: %CHECK_INTERVAL%s
echo ========================================
echo.

:: Check if executable exists
if not exist "%EXE_PATH%" (
    echo [ERROR] File not found: %EXE_PATH%
    echo.
    echo Please edit line 7 in this bat file and set the correct path.
    echo.
    pause
    exit /b 1
)

:: Clean up any stray files
if exist "check" del "check" 2>nul
if exist "start" del "start" 2>nul

:: Initialize log
echo. > "%LOG_FILE%"

:: Log function
:LOG
set "MESSAGE=%~1"
if "%MESSAGE%"=="" goto :EOF
set "TIMESTAMP=%date% %time:~0,8%"
echo %TIMESTAMP%  %MESSAGE% >> "%LOG_FILE%"
echo %MESSAGE%
goto :EOF

:: Check if process is running
:CHECK_PROCESS
set "PROCESS_ALIVE=0"
set "PROCESS_PID="
for /f "tokens=2" %%p in ('tasklist /fi "imagename eq %PROC_NAME%" /nh 2^>nul') do (
    set "PROCESS_ALIVE=1"
    set "PROCESS_PID=%%p"
)
goto :EOF

:: Start the process
:START_PROCESS
start "" "%EXE_PATH%"
timeout /t 3 /nobreak >nul
call :CHECK_PROCESS
if "%PROCESS_ALIVE%"=="1" (
    echo Started (PID=!PROCESS_PID!)
    set "START_RESULT=1"
) else (
    echo Failed to start
    set "START_RESULT=0"
)
goto :EOF

:: Check if port is listening
:CHECK_PORT
set "PORT_OK=0"
for /f "tokens=1,2,3,4" %%a in ('netstat -ano 2^>nul') do (
    if "%%d"=="LISTENING" (
        echo %%a | findstr ":%PORT%" >nul
        if !errorlevel! equ 0 set PORT_OK=1
    )
)
goto :EOF

:: Check if port is connectable
:CHECK_CONNECTION
set "CONN_OK=0"
powershell -Command "$c=New-Object Net.Sockets.TcpClient; $r=$c.BeginConnect('127.0.0.1',%PORT%,$null,$null); if($r.AsyncWaitHandle.WaitOne(2000)){$c.EndConnect($r);$c.Close();exit 0}else{exit 1}" 2>nul
if !errorlevel! equ 0 set CONN_OK=1
goto :EOF

:: ---------- MAIN PROGRAM ----------
call :LOG "=== C4D Client Monitor Started ==="
call :LOG "Path: %EXE_PATH%"
call :LOG "Port: %PORT%  Interval: %CHECK_INTERVAL%s"

:: Initial check
call :LOG "Checking initial state..."
call :CHECK_PROCESS
if "%PROCESS_ALIVE%"=="0" (
    call :LOG "Process not running, attempting to start..."
    
    :: Try to start up to 3 times
    set "ATTEMPTS=0"
    set "SUCCESS=0"
    
    :START_ATTEMPT
    set /a ATTEMPTS+=1
    call :LOG "Start attempt !ATTEMPTS!/3..."
    call :START_PROCESS
    
    if "%START_RESULT%"=="1" (
        set SUCCESS=1
        call :LOG "Start successful!"
    ) else (
        if !ATTEMPTS! lss 3 (
            call :LOG "Failed, retrying in 5 seconds..."
            timeout /t 5 /nobreak >nul
            goto START_ATTEMPT
        ) else (
            call :LOG "Failed 3 times, exiting..."
        )
    )
) else (
    call :LOG "Process already running (PID=!PROCESS_PID!)"
    set SUCCESS=1
)

if "%SUCCESS%"=="0" (
    call :LOG "Cannot start process, monitor will exit"
    pause
    exit /b 1
)

:: Wait for startup
call :LOG "Waiting %STARTUP_DELAY% seconds for initialization..."
timeout /t %STARTUP_DELAY% /nobreak >nul

:: Main monitoring loop
set FAILURE_COUNT=0
set RESTART_COUNT=0
set "LAST_CHECK="

:MONITOR_LOOP
set "CHECK_TIME=%time%"
if "%CHECK_TIME%" neq "%LAST_CHECK%" (
    call :LOG "--- Check at %CHECK_TIME% ---"
    set "LAST_CHECK=%CHECK_TIME%"
)

:: Check 1: Process
call :CHECK_PROCESS
if "%PROCESS_ALIVE%"=="0" (
    set /a FAILURE_COUNT+=1
    call :LOG "[WARN] Process gone (!FAILURE_COUNT!/%MAX_FAILURES%)"
    goto CHECK_RESTART
)

:: Check 2: Port
call :CHECK_PORT
if "%PORT_OK%"=="0" (
    set /a FAILURE_COUNT+=1
    call :LOG "[WARN] Port not listening (!FAILURE_COUNT!/%MAX_FAILURES%)"
    goto CHECK_RESTART
)

:: Check 3: Connection
call :CHECK_CONNECTION
if "%CONN_OK%"=="0" (
    set /a FAILURE_COUNT+=1
    call :LOG "[WARN] Port not responding (!FAILURE_COUNT!/%MAX_FAILURES%)"
    goto CHECK_RESTART
)

:: All good
if %FAILURE_COUNT% gtr 0 call :LOG "[OK] Status recovered"
if %FAILURE_COUNT% equ 0 call :LOG "[OK] Running (PID=!PROCESS_PID!)"
set FAILURE_COUNT=0

:END_CHECK
call :LOG "Status: OK  Restarts: !RESTART_COUNT!"
call :LOG "Waiting %CHECK_INTERVAL% seconds..."
echo.
timeout /t %CHECK_INTERVAL% /nobreak >nul
goto MONITOR_LOOP

:CHECK_RESTART
if %FAILURE_COUNT% lss %MAX_FAILURES% goto END_CHECK

call :LOG "[ERROR] Max failures reached, restarting..."

:: Kill process
call :LOG "Killing process..."
taskkill /f /im "%PROC_NAME%" >nul 2>&1
timeout /t 3 /nobreak >nul

:: Start process
set "START_OK=0"
for /l %%i in (1,1,3) do (
    if "%START_OK%"=="0" (
        call :LOG "Start attempt %%i/3..."
        call :START_PROCESS
        if "%START_RESULT%"=="1" set START_OK=1
        if "%START_OK%"=="0" timeout /t 5 /nobreak >nul
    )
)

if "%START_OK%"=="1" (
    set /a RESTART_COUNT+=1
    set FAILURE_COUNT=0
    call :LOG "[SUCCESS] Restart complete (Total: !RESTART_COUNT!)"
    call :LOG "Waiting %STARTUP_DELAY% seconds..."
    timeout /t %STARTUP_DELAY% /nobreak >nul
) else (
    call :LOG "[CRITICAL] Cannot start process, stopping monitor"
    pause
    exit /b 1
)

goto MONITOR_LOOP