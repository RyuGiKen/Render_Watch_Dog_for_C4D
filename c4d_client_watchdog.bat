@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

:: ========== ⚙️ 配置区 ==========
set "EXE=C:\Program Files\Maxon Cinema 4D 2026\Cinema 4D Team Render Client.exe"
set "PORT=5401"
set "PROC_NAME=Cinema 4D Team Render Client.exe"
:: =================================

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

:: 检查路径
if not exist "%EXE%" (
    echo [错误] 找不到文件: %EXE%
    echo.
    echo 请检查路径是否正确。2026 版可能在这些位置:
    echo   1. C:\Program Files\Maxon Cinema 4D 2026\
    echo   2. C:\Program Files\Maxon\Cinema 4D\2026\
    echo   3. C:\Program Files\Maxon Cinema 4D 2026\bin\
    echo.
    pause
    exit /b 1
)

:: 清理可能的残留文件
if exist "初始检查进程" del "初始检查进程" 2>nul
if exist ">>" del ">>" 2>nul

:: 初始化日志
if not exist "%LOG%" >nul type nul > "%LOG%"

echo [✓] 目标: %EXE%
echo [✓] 日志: %LOG%
echo.

:: 日志函数
:log
set "msg=%~1"
if "!msg!"=="" exit /b
set "ts=%date% %time:~0,8%"
>> "%LOG%" echo !ts!  !msg!
echo !msg!
exit /b

:: 检查进程是否存在
:proc_alive
set "alive=0"
set "cur_pid="
for /f "tokens=2" %%p in ('tasklist /fi "imagename eq %PROC_NAME%" /nh 2^>nul') do (
    set "alive=1"
    set "cur_pid=%%p"
)
exit /b

:: 启动进程
:launch
echo 正在启动进程...
start "" "%EXE%"
timeout /t 3 /nobreak >nul 2>nul
call :proc_alive
if "!alive!"=="1" (
    echo 启动成功 (PID=!cur_pid!)
    set launch_result=1
) else (
    echo 启动失败，进程未运行
    set launch_result=0
)
exit /b

:: 检查端口是否监听
:check_port
set "port_ok=0"
for /f "delims=" %%L in ('netstat -ano 2^>nul ^| findstr ":%PORT%"') do (
    echo %%L | findstr /c:"LISTENING" /c:"侦听" >nul
    if !errorlevel! equ 0 set port_ok=1
)
exit /b

:: 检查端口连接
:check_conn
set "conn_ok=0"
powershell -NoProfile -NonInteractive -Command "$c=New-Object Net.Sockets.TcpClient; $r=$c.BeginConnect('127.0.0.1',%PORT%,$null,$null); if($r.AsyncWaitHandle.WaitOne(2000)){$c.EndConnect($r);$c.Close();exit 0}else{exit 1}" >nul 2>&1
if !errorlevel! equ 0 set conn_ok=1
exit /b

:: 日志轮转
:rotate_log
if not exist "%LOG%" exit /b
for %%F in ("%LOG%") do set "logsize=%%~zF"
if not defined logsize exit /b
if !logsize! gtr %MAX_LOG_BYTES% (
    set "bak=%LOG%.%date:~0,4%%date:~5,2%%date:~8,2%_%time:~0,2%%time:~3,2%%time:~6,2%.bak"
    set "bak=!bak: =0!"
    move "%LOG%" "!bak!" >nul
    echo 日志已轮转: !bak!
)
exit /b

:: ---------- 主程序 ----------
call :log "========== 看门狗启动 =========="
call :log "路径: %EXE%"
call :log "端口: %PORT%  间隔: %INTERVAL%s"

:: 初始检查
call :log ">> 检查进程状态..."
call :proc_alive
if "!alive!"=="0" (
    call :log "进程未运行，尝试启动..."
    
    :: 尝试启动最多3次
    set "launch_attempts=0"
    set "launch_success=0"
    
    :launch_retry
    set /a launch_attempts+=1
    call :log "启动尝试 !launch_attempts!/3..."
    call :launch
    
    if "!launch_result!"=="1" (
        set launch_success=1
        call :log "启动成功!"
    ) else (
        if !launch_attempts! lss 3 (
            call :log "启动失败，5秒后重试..."
            timeout /t 5 /nobreak >nul
            goto launch_retry
        ) else (
            call :log "启动失败3次，请检查程序或路径"
        )
    )
) else (
    call :log "进程已运行 (PID=!cur_pid!)"
    set launch_success=1
)

if "!launch_success!"=="0" (
    call :log "程序无法启动，监控将停止"
    pause
    exit /b 1
)

:: 等待启动完成
call :log "等待 %STARTUP_WAIT% 秒启动完成..."
timeout /t %STARTUP_WAIT% /nobreak >nul

:: 主循环
set fail_cnt=0
set total_restarts=0
set "last_check="

:main_loop
set "check_time=%date% %time%"
if "!check_time!" neq "!last_check!" (
    call :log "--- 检测: !check_time! ---"
    set "last_check=!check_time!"
)

:: 检测进程
call :proc_alive
if "!alive!"=="0" (
    set /a fail_cnt+=1
    call :log "[警告] 进程消失 (!fail_cnt!/%FAIL_LIMIT%)"
    goto check_restart
)

:: 检测端口
call :check_port
if "!port_ok!"=="0" (
    set /a fail_cnt+=1
    call :log "[警告] 端口未监听 (!fail_cnt!/%FAIL_LIMIT%)"
    goto check_restart
)

:: 检测连接
call :check_conn
if "!conn_ok!"=="0" (
    set /a fail_cnt+=1
    call :log "[警告] 端口无响应 (!fail_cnt!/%FAIL_LIMIT%)"
    goto check_restart
)

:: 一切正常
if !fail_cnt! gtr 0 call :log "[正常] 状态已恢复"
if !fail_cnt! equ 0 call :log "[正常] 运行中 (PID=!cur_pid!)"
set fail_cnt=0

:end_check
call :log "状态: 正常  重启次数: !total_restarts!"
call :log "等待 %INTERVAL% 秒后再次检测..."
echo.
timeout /t %INTERVAL% /nobreak >nul
goto main_loop

:check_restart
if !fail_cnt! lss %FAIL_LIMIT% goto end_check

call :log "[错误] 达到失败阈值，执行重启..."

:: 停止进程
call :log "停止进程..."
taskkill /f /im "%PROC_NAME%" >nul 2>&1
timeout /t 3 /nobreak >nul

:: 启动进程
set "launch_ok=0"
for /l %%i in (1,1,3) do (
    if "!launch_ok!"=="0" (
        call :log "启动尝试 %%i/3..."
        call :launch
        if "!launch_result!"=="1" set launch_ok=1
        if "!launch_ok!"=="0" timeout /t 5 /nobreak >nul
    )
)

if "!launch_ok!"=="1" (
    set /a total_restarts+=1
    set fail_cnt=0
    call :log "[成功] 重启完成 (总计: !total_restarts!)"
    call :log "等待 %STARTUP_WAIT% 秒初始化..."
    timeout /t %STARTUP_WAIT% /nobreak >nul
) else (
    call :log "[严重] 无法启动进程，停止监控"
    pause
    exit /b 1
)

goto main_loop