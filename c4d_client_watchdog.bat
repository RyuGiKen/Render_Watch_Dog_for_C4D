@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ========== 配置 ==========
set "EXE=C:\Program Files\Maxon Cinema 4D 2026\Cinema 4D Team Render Client.exe"
set "PORT=5401"
set "LOG=c4d_monitor.log"
:: =========================

title C4D Client PID 监控
cls
echo ========================================
echo   C4D Team Render Client 监控 (PID方式)
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

:: 获取当前PID
:GET_PID
set "CURRENT_PID="
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":%PORT%" ^| findstr "LISTENING"') do (
    set "CURRENT_PID=%%p"
)
if not defined CURRENT_PID (
    echo [%time:~0,8%] 端口 %PORT% 未监听，启动程序...
    echo [%date% %time:~0,8%] 端口未监听，启动程序 >> "%LOG%"
    
    :: 修复启动命令 - 使用正确的语法
    if exist "%EXE%" (
        cd /d "%~dp0"
        start "" "%EXE%"
    ) else (
        echo [错误] 程序文件不存在: %EXE%
        pause
        exit /b
    )
    
    timeout /t 60 /nobreak >nul
    goto GET_PID
)

echo [%time:~0,8%] 找到进程 PID: !CURRENT_PID!
echo [%date% %time:~0,8%] 初始PID: !CURRENT_PID! >> "%LOG%"

:: 主监控循环
:MAIN_LOOP
echo.
echo ========================================
echo 检查时间: %date% %time:~0,8%
echo 目标PID: !CURRENT_PID!
echo.

:: 检查1: 端口是否还在监听
set "PORT_OK=0"
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":%PORT%" ^| findstr "LISTENING"') do (
    if "%%p"=="!CURRENT_PID!" set PORT_OK=1
)

if "!PORT_OK!"=="0" (
    echo [警告] 端口 %PORT% 不再监听
    echo [%date% %time:~0,8%] 端口不再监听，重启程序 >> "%LOG%"
    
    :: 尝试结束原进程
    taskkill /f /pid !CURRENT_PID! >nul 2>&1
    timeout /t 3 /nobreak >nul
    
    :: 启动新进程
    if exist "%EXE%" (
        cd /d "%~dp0"
        start "" "%EXE%"
    )
    echo [信息] 已重启程序，等待2分钟...
    timeout /t 120 /nobreak >nul
    
    :: 获取新PID
    set "CURRENT_PID="
    for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":%PORT%" ^| findstr "LISTENING"') do (
        set "CURRENT_PID=%%p"
    )
    
    if defined CURRENT_PID (
        echo [成功] 新PID: !CURRENT_PID!
        echo [%date% %time:~0,8%] 重启成功，新PID: !CURRENT_PID! >> "%LOG%"
    ) else (
        echo [错误] 无法获取新PID
        echo [%date% %time:~0,8%] 重启失败 >> "%LOG%"
    )
) else (
    :: 检查2: 进程是否还在运行
    tasklist /fi "PID eq !CURRENT_PID!" | findstr "!CURRENT_PID!" >nul
    if errorlevel 1 (
        echo [警告] PID !CURRENT_PID! 进程不存在
        echo [%date% %time:~0,8%] PID !CURRENT_PID! 进程不存在 >> "%LOG%"
        
        :: 启动新进程
        if exist "%EXE%" (
            cd /d "%~dp0"
            start "" "%EXE%"
        )
        echo [信息] 已启动程序，等待2分钟...
        timeout /t 120 /nobreak >nul
        
        :: 获取新PID
        set "CURRENT_PID="
        for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":%PORT%" ^| findstr "LISTENING"') do (
            set "CURRENT_PID=%%p"
        )
        
        if defined CURRENT_PID (
            echo [成功] 新PID: !CURRENT_PID!
            echo [%date% %time:~0,8%] 新进程PID: !CURRENT_PID! >> "%LOG%"
        )
    ) else (
        echo [正常] 进程运行中 (PID: !CURRENT_PID!)
        echo [%date% %time:~0,8%] 状态正常 >> "%LOG%"
    )
)

:: 等待5分钟
echo.
echo [信息] 等待5分钟后再次检查...
echo ========================================
timeout /t 300 /nobreak >nul
goto MAIN_LOOP