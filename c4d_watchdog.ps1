# ====== 配置区 ======
# 请根据你的实际安装路径修改下面两个路径
$serverExePath = "C:\Program Files\Maxon Cinema 4D 2025\Cinema 4D Team Render Server.exe"
$clientExePath = "C:\Program Files\Maxon Cinema 4D 2025\Cinema 4D Team Render Client.exe"

# 启动参数（一般留空即可，如需指定配置文件或端口可在这里加）
$serverArgs = ""
$clientArgs = ""

# 检测间隔（秒）
$checkInterval = 10

# 端口连接超时（毫秒）
$portTimeout = 3000

# 连续几次检测失败才判定为挂掉（防抖）
$failureThreshold = 2

# 日志文件路径（默认放在脚本同目录）
$logFile = "$PSScriptRoot\c4d_watchdog.log"

# ====== 监控目标定义 ======
$targets = @(
    @{
        Name     = "Team Render Server"
        ExePath  = $serverExePath
        Args     = $serverArgs
        Port     = 5402
        ProcName = "Cinema 4D Team Render Server"
    },
    @{
        Name     = "Team Render Client"
        ExePath  = $clientExePath
        Args     = $clientArgs
        Port     = 5401
        ProcName = "Cinema 4D Team Render Client"
    }
)

# ====== 函数 ======
function Write-Log($msg) {
    $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $line = "$ts  $msg"
    Write-Host $line -ForegroundColor Cyan
    Add-Content -LiteralPath $logFile -Value $line -Encoding UTF8
}

function Test-Port($port) {
    # 测试 TCP 端口是否可连接
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $async = $tcp.BeginConnect("127.0.0.1", $port, $null, $null)
        $wait = $async.AsyncWaitHandle.WaitOne($portTimeout, $false)
        $tcp.Close()
        return $wait
    } catch {
        return $false
    }
}

function Start-Target($target) {
    Write-Log "▶ 启动 $($target.Name): $($target.ExePath)"
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName  = $target.ExePath
        $psi.Arguments = $target.Args
        $psi.WorkingDirectory = Split-Path $target.ExePath
        $psi.UseShellExecute = $false
        [System.Diagnostics.Process]::Start($psi) | Out-Null
        Write-Log "✓ $($target.Name) 启动成功"
        return $true
    } catch {
        Write-Log "✗ $($target.Name) 启动失败: $_"
        return $false
    }
}

function Stop-Target($target) {
    $procs = Get-Process -Name $target.ProcName -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Log "🔪 强制终止 $($target.Name) (PID: $($procs.Id -join ', '))"
        $procs | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
}

# ====== 初始化 ======
Write-Log "===== C4D Team Render 看门狗启动 ====="
Write-Log "监控目标: $($targets.Count) 个"
Write-Log "日志文件: $logFile"

# 失败计数器（每个目标独立）
$failureCounters = @{}
foreach ($t in $targets) {
    $failureCounters[$t.Name] = 0
}

# ====== 主循环 ======
while ($true) {
    foreach ($target in $targets) {
        $name = $target.Name
        $procName = $target.ProcName
        $port = $target.Port

        # 1. 检查进程是否存在
        $proc = Get-Process -Name $procName -ErrorAction SilentlyContinue | Select-Object -First 1

        # 2. 检查端口是否可连
        $portOk = Test-Port $port

        if (-not $proc -or -not $portOk) {
            # 进程不存在 或 端口连不上 → 记录失败
            $failureCounters[$name]++

            $reason = if (-not $proc) { "进程不存在" } else { "端口 $port 无响应" }
            Write-Log "⚠ $name 异常 ($reason) - 失败计数: $($failureCounters[$name])/$failureThreshold"

            if ($failureCounters[$name] -ge $failureThreshold) {
                # 达到阈值，执行重启
                Write-Log "🔄 $name 达到失败阈值，执行重启..."
                Stop-Target $target
                Start-Sleep -Seconds 3
                $started = Start-Target $target
                if ($started) {
                    Write-Log "✓ $name 重启完成"
                }
                $failureCounters[$name] = 0  # 重置计数器
            }
        } else {
            # 一切正常，重置计数器
            if ($failureCounters[$name] -gt 0) {
                Write-Log "✓ $name 恢复正常"
                $failureCounters[$name] = 0
            }
        }
    }

    # 等待下一次检测
    Start-Sleep -Seconds $checkInterval
}