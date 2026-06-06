@echo off
chcp 65001 >nul
title C4D 进程状态检测
cls
echo ========================================
echo  C4D Team Render Client 状态检测
echo  每5分钟检测一次
echo  仅结束未响应进程
echo ========================================
echo.

:loop
echo 检测时间: %time%

:: 使用 PowerShell 检测并处理
powershell -Command "
$process = Get-Process 'Cinema 4D Team Render Client' -ErrorAction SilentlyContinue
if ($process) {
    Write-Host '进程存在' -ForegroundColor Green
    Write-Host '进程PID: ' $process.Id
    Write-Host '进程状态: ' $process.Responding
    if (-not $process.Responding) {
        Write-Host '发现未响应进程，正在结束...' -ForegroundColor Yellow
        $process | Stop-Process -Force
        Write-Host '进程已结束' -ForegroundColor Red
    }
} else {
    Write-Host '进程未运行' -ForegroundColor Gray
}
"

echo 等待5分钟后再次检测...
timeout /t 300 /nobreak >nul
goto loop