@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

:: ========== ⚙️ 只改这里 ==========
set "EXE=C:\Program Files\Maxon Cinema 4D 2026\Cinema 4D Team Render Client.exe"
set "PORT=5401"
set "PROC_NAME=Cinema 4D Team Render Client.exe"
:: ====================================

set "INTERVAL=300"
set "STARTUP_WAIT=60"
set "FAIL_LIMIT=3"
set "LOG=%~dp0c4d_client_watchdog.log"
set "MAX_LOG_BYTES=10485760"

:: ---------- 初始化 ----------
title C4D Team Render Client 看门狗
cls
echo ================================================
echo  C4D Team Render Client 看门狗
echo  端口: %PORT%   检测间隔: %INTERVAL%s   进程: %PROC_NAME%
echo ================================================
echo.

:: 路径检查（一跑就告诉你哪错了，不会静默没反应）
if not exist "%EXE%" (
    color 0C
    echo [✘] 找不到文件:
    echo     %EXE%
    echo.
    echo 请打开这个 bat，把 EXE= 那行改成你机器上的真实路径
    echo 2026 默认可能在:
    echo   C:\Program Files\Maxon Cinema 4D 2026\
    echo   C:\Program Files\Maxon\Cinema 4D\2026\
    echo.
    pause & exit /b 1
)

:: 日志轮转
call :rotate_log

echo [✓] 目标: %EXE%
echo [✓] 日志: %LOG%
echo.
call :log "================ 看门狗启动 ================"
call :log "EXE=%EXE%"
call :log "PORT=%PORT%  INTERVAL=%INTERVAL%s  STARTUP_WAIT=%STARTUP_WAIT%s  FAIL_LIMIT=%FAIL_LIMIT%"

:: ---------- 初始拉起 ----------
call :log ">> 初始检查进程..."
call :proc_alive
if "!alive!"=="0" (
    call :log "进程未运行 → 首次启动"
    call :launch
) else (
    call :log "进程已在运行 (PID=!cur_pid!)"
)

:: 等它绑端口
call :log "等待 %STARTUP_WAIT% 秒让 Client 初始化..."
timeout /t %STARTUP_WAIT% /nobreak >nul

:: ---------- 主循环 ----------
set fail_cnt=0
set total_rst=0

:loop
set "now=%date% %time%"
call :log --- 检测回合: !now!

:: 1) 进程在不在
call :proc_alive
if "!alive!"=="0" (
    set /a fail_cnt+=1
    call :log "[!] 进程消失  失败!fail_cnt!/%FAIL_LIMIT%"
    goto maybe_restart
)

:: 2) 端口有没有 LISTENING（netstat 不依赖任何外部工具）
set "port_ok=0"
for /f "tokens=1,*" %%a in ('netstat -ano 2^>nul ^| findstr /i /r ":%PORT%.*LISTENING"') do set port_ok=1

if "!port_ok!"=="0" (
    set /a fail_cnt+=1
    call :log "[!] 进程在(PID=!cur_pid!) 但端口 %PORT% 没监听  失败!fail_cnt!/%FAIL_LIMIT%"
    goto maybe_restart
)

:: 3) 端口通不通（用 powershell 一行，Windows 自带，不需要 .ps1 文件）
set "conn_ok=0"
powershell -NoProfile -NonInteractive -Command "$ErrorActionPreference='Stop'; $c=New-Object Net.Sockets.TcpClient; try{$r=$c.BeginConnect('127.0.0.1',%PORT%,$null,$null); if($r.AsyncWaitHandle.WaitOne(2000){$c.EndConnect($r);$c.Close();exit 0}else{exit 1}}catch{exit 1}" >nul 2>&1
if !errorlevel! equ 0 set conn_ok=1

if "!conn_ok!"=="0" (
    set /a fail_cnt+=1
    call :log "[!] 端口 %PORT% 监听中但连接超时（僵死?）  失败!fail_cnt!/%FAIL_LIMIT%"
    goto maybe_restart
)

:: 正常
if !fail_cnt! gtr 0 call :log "[✓] 已恢复（之前失败!fail_cnt!次）"
if !fail_cnt! equ 0 call :log "[✓] 正常  PID=!cur_pid!  端口=%PORT%"
set fail_cnt=0

:summary
call :log "--- 累计重启:%total_rst%  下次检测约 %INTERVAL% 秒后 ------------------"
echo.
timeout /t %INTERVAL% /nobreak >nul
goto loop

:maybe_restart
if !fail_cnt! lss %FAIL_LIMIT% goto summary
call :log "[✘] 达到 %FAIL_LIMIT% 次 → 重启"
call :log "taskkill /f /im \"%PROC_NAME%\" ..."
taskkill /f /im "%PROC_NAME%" >nul 2>&1
timeout /t 3 /nobreak >nul
call :launch
set /a total_rst+=1
set fail_cnt=0
call :log "重启后等待 %STARTUP_WAIT% 秒..."
timeout /t %STARTUP_WAIT% /nobreak >nul
goto loop

:: ========== 子过程 ==========

:proc_alive
:: 返回 alive=1/0 , cur_pid=PID
set "alive=0"
set "cur_pid="
for /f "skip=3 tokens=2" %%p in ('tasklist /fi "imagename eq %PROC_NAME%" /nh 2^>nul') do (
    set "alive=1"
    set "cur_pid=%%~p"
)
exit /b

:launch
call :log ">>> 启动 %PROC_NAME%"
start "" "%EXE%"
exit /b

:log
echo %date% %time:~0,8%  %~1 >> "%LOG%"
echo %~1
exit /b

:rotate_log
if exist "%LOG%" (
    call :fsize "%LOG%" LOGSZ
    if !LOGSZ! gtr %MAX_LOG_BYTES% (
        set "BF=%LOG%.%date:~0,4%%date:~5,2%%date:~8,2%_%time:~0,2%%time:~3,2%%time:~6,2%.bak"
        set "BF=!BF: =0!"
        move "%LOG%" "!BF!" >nul 2>&1
        echo [日志] 已轮转，旧日志: !BF!
    )
)
exit /b

:: 取文件字节大小（兼容没有 ~z 的情况）
:fsize
set "%~2=0"
for /f "usebackq" %%S in ('"%~1"') do set "%~2=%%~zS"
exit /b