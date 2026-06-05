@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: 配置
set "EXE=C:\Program Files\Maxon Cinema 4D 2026\Cinema 4D Team Render Client.exe"
set "PORT=5401"
set "NORMAL_WAIT=300"   :: 正常等待5分钟
set "RESTART_WAIT=120"  :: 重启后等待2分钟
set "KILL_WAIT=5"       :: 杀进程后等待5秒

title C4D Client Monitor
cls
echo ========================================
echo   C4D Team Render Client 监控
echo   端口: %PORT%
echo   正常检测间隔: 5分钟
echo   重启后等待: 2分钟
echo ========================================
echo.

if not exist "%EXE%" (
    echo 错误: 文件不存在
    echo %EXE%
    pause
    exit /b
)

echo 监控已启动，按 Ctrl+C 停止
echo.

:main_loop
echo 检查时间: %date% %time%

:: 获取端口PID
set "port_pid="
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":%PORT%" ^| findstr "LISTENING"') do set "port_pid=%%p"

:: 获取进程PID
set "proc_pid="
for /f "tokens=2" %%p in ('tasklist /fi "imagename eq Cinema 4D Team Render Client.exe" /fo csv ^| findstr /v "INFO"') do (
    set "proc_pid=%%~p"
)

echo 端口PID: !port_pid!
echo 进程PID: !proc_pid!

:: 情况A: 进程和端口都正常
if defined port_pid if defined proc_pid (
    :: 检查端口是否对应同一个进程
    if "!port_pid!"=="!proc_pid!" (
        echo 状态: 正常 (PID: !proc_pid!)
        echo 等待5分钟后再次检查...
        echo ========================================
        timeout /t %NORMAL_WAIT% /nobreak >nul
        goto main_loop
    )
)

:: 情况B: 进程不在
if not defined proc_pid (
    echo 状态: 进程不存在
    
    :: 如果端口还在监听，先清理
    if defined port_pid (
        echo 清理占用端口的进程 (PID: !port_pid!)...
        taskkill /f /pid !port_pid! >nul 2>&1
        timeout /t %KILL_WAIT% /nobreak >nul
    )
    
    echo 启动新进程...
    start "" "%EXE%"
    echo 等待2分钟启动完成...
    timeout /t %RESTART_WAIT% /nobreak >nul
    goto main_loop
)

:: 情况C: 进程在，但端口不在
if defined proc_pid if not defined port_pid (
    echo 状态: 进程卡死 (进程在，端口不在)
    echo 结束卡死进程 (PID: !proc_pid!)...
    taskkill /f /pid !proc_pid! >nobreak >nul 2>&1
    timeout /t %KILL_WAIT% /nobreak >nul
    
    echo 启动新进程...
    start "" "%EXE%"
    echo 等待2分钟启动完成...
    timeout /t %RESTART_WAIT% /nobreak >nul
    goto main_loop
)

:: 情况D: 端口在，但不是当前进程 (冲突)
if defined port_pid if defined proc_pid (
    if not "!port_pid!"=="!proc_pid!" (
        echo 状态: 端口冲突
        echo 清理占用端口的进程 (PID: !port_pid!)...
        taskkill /f /pid !port_pid! >nul 2>&1
        timeout /t %KILL_WAIT% /nobreak >nul
        
        echo 启动新进程...
        start "" "%EXE%"
        echo 等待2分钟启动完成...
        timeout /t %RESTART_WAIT% /nobreak >nul
        goto main_loop
    )
)

:: 如果走到这里，说明有其他未处理的情况
echo 状态: 未知
echo 等待5分钟后再次检查...
echo ========================================
timeout /t %NORMAL_WAIT% /nobreak >nul
goto main_loop