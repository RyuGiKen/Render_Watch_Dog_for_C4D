@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

:: ========== 配置 ==========
set "EXE=C:\Program Files\Maxon Cinema 4D 2026\Cinema 4D Team Render Client.exe"
set "PORT=5401"
set "LOG=c4d_monitor.log"
set "CHECK_INTERVAL=300"
set "STARTUP_WAIT=60"
set "KILL_WAIT=5"
:: =========================

title C4D Client 监控
cls
echo ========================================
echo   C4D Team Render Client 监控
echo   端口: %PORT%  检查间隔: 5分钟
echo ========================================
echo.

:: 检查文件是否存在
if not exist "%EXE%" (
    echo [错误] 找不到文件:
    echo   %EXE%
    echo.
    echo 请确认路径是否正确
    pause
    exit /b
)

:: 初始化日志
echo [%date% %time:~0,8%] 监控启动 > "%LOG%"

:: 函数：记录日志
:log_func
set "log_msg=%~1"
if "%log_msg%"=="" goto :eof
set "time_now=%time: =0%"
>> "%LOG%" echo [%date% %time_now:~0,8%] %log_msg%
echo [%time_now:~0,8%] %log_msg%
goto :eof

:: 函数：获取端口对应的PID
:get_pid_from_port
set "port_pid="
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":%PORT%" ^| findstr "LISTENING"') do (
    set "port_pid=%%p"
)
goto :eof

:: 函数：启动程序
:start_app
call :log_func "启动程序..."
if not exist "%EXE%" (
    call :log_func "错误: 程序文件不存在"
    goto :eof
)
start "" "%EXE%"
timeout /t 3 /nobreak >nul
goto :eof

:: 函数：检查进程是否存在
:check_process_alive
set "process_alive=0"
if "%1"=="" goto :eof
tasklist /fi "PID eq %1" 2>nul | findstr "%1" >nul && set "process_alive=1"
goto :eof

:: 函数：检查端口是否可连接
:check_port_connect
set "port_connectable=0"
powershell -Command "$c=New-Object Net.Sockets.TcpClient; $r=$c.BeginConnect('127.0.0.1',%PORT%,$null,$null); if($r.AsyncWaitHandle.WaitOne(2000)){$c.EndConnect($r);$c.Close();exit 0}else{exit 1}" 2>nul
if !errorlevel! equ 0 set "port_connectable=1"
goto :eof

:: 主程序开始
call :log_func "监控程序启动"
call :log_func "目标程序: %EXE%"
call :log_func "监控端口: %PORT%"
call :log_func "检查间隔: %CHECK_INTERVAL%秒"
call :log_func "启动等待: %STARTUP_WAIT%秒"
call :log_func "终止等待: %KILL_WAIT%秒"

:: 获取当前端口对应的PID
call :get_pid_from_port
if not defined port_pid (
    call :log_func "端口未监听，启动程序..."
    call :start_app
    timeout /t %STARTUP_WAIT% /nobreak >nul
    goto get_pid_from_port
) else (
    call :log_func "初始PID: !port_pid!"
    set "current_pid=!port_pid!"
)

:: 主监控循环
:main_loop
echo.
call :log_func "开始检查..."
echo 当前PID: !current_pid!

:: 情况1: 检查端口是否还在监听
call :get_pid_from_port
if not defined port_pid (
    call :log_func "端口%PORT%未监听"
    
    :: 检查进程是否还在
    call :check_process_alive !current_pid!
    if "!process_alive!"=="1" (
        call :log_func "进程还在运行但端口已关闭 (卡死状态)"
        call :log_func "终止卡死进程..."
        taskkill /f /pid !current_pid! >nul 2>&1
        call :log_func "等待%KILL_WAIT%秒..."
        timeout /t %KILL_WAIT% /nobreak >nul
        call :log_func "启动新进程..."
        call :start_app
        timeout /t %STARTUP_WAIT% /nobreak >nul
        
        :: 获取新PID
        call :get_pid_from_port
        if defined port_pid (
            set "current_pid=!port_pid!"
            call :log_func "新进程PID: !current_pid!"
        ) else (
            call :log_func "警告: 启动后未检测到端口监听"
        )
    ) else (
        call :log_func "进程已退出，启动新进程..."
        call :start_app
        timeout /t %STARTUP_WAIT% /nobreak >nul
        
        :: 获取新PID
        call :get_pid_from_port
        if defined port_pid (
            set "current_pid=!port_pid!"
            call :log_func "新进程PID: !current_pid!"
        )
    )
    goto next_check
)

:: 情况2: 端口在监听，检查是否是当前PID
if "!port_pid!" neq "!current_pid!" (
    call :log_func "端口被其他进程占用 (PID: !port_pid!)"
    
    :: 检查当前PID是否还在
    call :check_process_alive !current_pid!
    if "!process_alive!"=="1" (
        call :log_func "当前进程还在运行，终止它..."
        taskkill /f /pid !current_pid! >nul 2>&1
    )
    
    :: 检查占用端口的进程
    call :check_process_alive !port_pid!
    if "!process_alive!"=="1" (
        call :log_func "终止占用端口的进程..."
        taskkill /f /pid !port_pid! >nul 2>&1
        timeout /t %KILL_WAIT% /nobreak >nul
    )
    
    call :log_func "启动新进程..."
    call :start_app
    timeout /t %STARTUP_WAIT% /nobreak >nul
    
    :: 获取新PID
    call :get_pid_from_port
    if defined port_pid (
        set "current_pid=!port_pid!"
        call :log_func "新进程PID: !current_pid!"
    )
    goto next_check
)

:: 情况3: 端口在监听，是当前PID，检查端口是否可连接
call :check_port_connect
if "!port_connectable!"=="0" (
    call :log_func "端口监听但无法连接 (僵死状态)"
    call :log_func "终止僵死进程..."
    taskkill /f /pid !current_pid! >nul 2>&1
    timeout /t %KILL_WAIT% /nobreak >nul
    call :log_func "启动新进程..."
    call :start_app
    timeout /t %STARTUP_WAIT% /nobreak >nul
    
    :: 获取新PID
    call :get_pid_from_port
    if defined port_pid (
        set "current_pid=!port_pid!"
        call :log_func "新进程PID: !current_pid!"
    )
    goto next_check
)

:: 一切正常
call :log_func "状态正常: PID=!current_pid!, 端口=%PORT% 监听正常"

:next_check
call :log_func "等待%CHECK_INTERVAL%秒后再次检查..."
echo ========================================
timeout /t %CHECK_INTERVAL% /nobreak >nul
goto main_loop