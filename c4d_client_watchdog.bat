@echo off
chcp 65001 >nul
title C4D 进程响应检测
cls
echo ========================================
echo  C4D Team Render Client 未响应检测
echo  每5分钟检测一次
echo  仅结束未响应进程
echo  重启由 Watchdog 处理
echo ========================================
echo.

:loop
echo 检测时间: %time%

:: 使用 PowerShell 检测未响应进程
for /f "tokens=2" %%p in ('powershell -Command "Get-Process 'Cinema 4D Team Render Client' -ErrorAction SilentlyContinue | Where-Object { $_.Responding -eq $false } | Select-Object -ExpandProperty Id"') do (
    if not "%%p"=="" (
        echo 发现未响应进程 PID: %%p
        echo 结束进程...
        taskkill /f /pid %%p >nul
        echo 已结束未响应进程
    )
)

:: 如果没有找到未响应进程
if errorlevel 1 (
    echo 未发现未响应进程
)

echo 等待5分钟后再次检测...
echo ========================================
timeout /t 300 /nobreak >nul
goto loop