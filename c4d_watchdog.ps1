# ====== 配置区 ======
# 请根据你的实际安装路径修改
$serverExePath = "C:\Program Files\Maxon Cinema 4D 2025\Cinema 4D Team Render Server.exe"
$clientExePath = "C:\Program Files\Maxon Cinema 4D 2025\Cinema 4D Team Render Client.exe"

# 启动参数（一般留空即可）
$serverArgs = ""
$clientArgs = ""

# 检测间隔（秒），5分钟
$checkInterval = 300

# 启动后等待时间（秒），1分钟
$startupWaitTime = 60

# 端口检测超时（毫秒），3秒
$portTimeout = 3000

# 连续几次失败才重启（防抖）
$failureThreshold = 3

# 日志文件路径
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
function Write-Log($msg, $level = "INFO") {
    $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $levelText = $level.PadRight(5)
    $line = "$ts  [$levelText]  $msg"
    
    # 控制台输出（带颜色）
    $colorMap = @{
        "INFO"  = "White"
        "WARN"  = "Yellow"
        "ERROR" = "Red"
        "START" = "Green"
        "STOP"  = "Magenta"
    }
    $color = $colorMap[$level]
    Write-Host $line -ForegroundColor $color
    
    # 写入日志文件
    Add-Content -LiteralPath $logFile -Value $line -Encoding UTF8
}

function Test-PortInUse($port) {
    # 检测端口是否被占用（不一定是目标进程占用的）
    try {
        $listener = [System.Net.Sockets.TcpListener]$port
        $listener.Start()
        $listener.Stop()
        return $false  # 端口可用
    } catch {
        return $true   # 端口已被占用
    }
}

function Test-PortConnect($port) {
    # 检测端口是否能连接（服务是否响应）
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

function Get-ProcessByPort($port) {
    # 获取占用指定端口的进程（需要管理员权限）
    try {
        $conn = Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue
        if ($conn) {
            return Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
        }
    } catch {
        # 无管理员权限时静默失败
    }
    return $null
}

function Start-Target($target) {
    $name = $target.Name
    $exePath = $target.ExePath
    
    # 检查可执行文件是否存在
    if (-not (Test-Path $exePath)) {
        Write-Log "❌ 无法找到可执行文件: $exePath" "ERROR"
        return $false
    }
    
    Write-Log "🚀 启动 $name..." "START"
    
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName  = $exePath
        $psi.Arguments = $target.Args
        $psi.WorkingDirectory = Split-Path $exePath
        $psi.UseShellExecute = $false
        
        $process = [System.Diagnostics.Process]::Start($psi)
        if ($process) {
            Write-Log "✅ $name 启动成功 (PID: $($process.Id))" "START"
            return $true
        } else {
            Write-Log "❌ $name 启动失败: 进程创建失败" "ERROR"
            return $false
        }
    } catch {
        Write-Log "❌ $name 启动失败: $_" "ERROR"
        return $false
    }
}

function Stop-Target($target) {
    $name = $target.Name
    $procName = $target.ProcName
    
    $procs = Get-Process -Name $procName -ErrorAction SilentlyContinue
    if ($procs.Count -gt 0) {
        $pids = $procs.Id -join ', '
        Write-Log "🛑 停止 $name (PID: $pids)..." "STOP"
        $procs | Stop-Process -Force
        Start-Sleep -Seconds 3
        Write-Log "✅ $name 已停止" "STOP"
    }
}

# ====== 初始化检查 ======
Write-Log "=" * 60
Write-Log "🎬 C4D Team Render 看门狗 v2.0 启动" "INFO"
Write-Log "📁 日志文件: $logFile" "INFO"
Write-Log "⏰ 检测间隔: ${checkInterval}秒" "INFO"
Write-Log "⏳ 启动延迟: ${startupWaitTime}秒" "INFO"

# 检查可执行文件是否存在
$allOk = $true
foreach ($target in $targets) {
    if (-not (Test-Path $target.ExePath)) {
        Write-Log "❌ 找不到 $($target.Name) 可执行文件: $($target.ExePath)" "ERROR"
        $allOk = $false
    } else {
        Write-Log "✅ 找到 $($target.Name): $($target.ExePath)" "INFO"
    }
}

if (-not $allOk) {
    Write-Log "❌ 初始化检查失败，请检查配置文件中的路径" "ERROR"
    Write-Log "按任意键退出..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}

# ====== 初始启动 ======
Write-Log "🔧 执行初始启动检查..." "INFO"
foreach ($target in $targets) {
    $procs = Get-Process -Name $target.ProcName -ErrorAction SilentlyContinue
    if ($procs.Count -eq 0) {
        Write-Log "📤 $($target.Name) 未运行，正在启动..." "INFO"
        $null = Start-Target $target
    } else {
        Write-Log "✅ $($target.Name) 已在运行 (PID: $($procs[0].Id))" "INFO"
    }
}

# 等待服务完全启动
Write-Log "⏳ 等待 ${startupWaitTime}秒让服务完全启动..." "INFO"
Start-Sleep -Seconds $startupWaitTime

# ====== 主循环 ======
Write-Log "🔄 开始监控循环..." "INFO"

# 失败计数器
$failureCounters = @{}
foreach ($t in $targets) {
    $failureCounters[$t.Name] = 0
}

# 上次重启时间记录
$lastRestartTimes = @{}

while ($true) {
    $currentTime = Get-Date
    Write-Log "⏰ 开始新一轮检测: $($currentTime.ToString('HH:mm:ss'))" "INFO"
    
    foreach ($target in $targets) {
        $name = $target.Name
        $procName = $target.ProcName
        $port = $target.Port
        
        Write-Log "🔍 检查 $name..." "INFO"
        
        # 检查进程是否存在
        $procs = Get-Process -Name $procName -ErrorAction SilentlyContinue
        
        if ($procs.Count -eq 0) {
            Write-Log "⚠️  $name 进程不存在" "WARN"
            $failureCounters[$name]++
            
            if ($failureCounters[$name] -ge $failureThreshold) {
                Write-Log "🔄 $name 连续 $failureThreshold 次检测失败，执行重启..." "WARN"
                $null = Start-Target $target
                $lastRestartTimes[$name] = $currentTime
                $failureCounters[$name] = 0
                Write-Log "⏳ 等待 ${startupWaitTime}秒让 $name 启动..." "INFO"
                Start-Sleep -Seconds $startupWaitTime
            }
            continue
        }
        
        # 进程存在，检查端口占用
        $portInUse = Test-PortInUse $port
        
        if (-not $portInUse) {
            Write-Log "⚠️  $name 进程存在，但端口 $port 未被占用" "WARN"
            $failureCounters[$name]++
            
            if ($failureCounters[$name] -ge $failureThreshold) {
                Write-Log "🔄 $name 端口 $port 未被占用，停止并重启..." "WARN"
                Stop-Target $target
                $null = Start-Target $target
                $lastRestartTimes[$name] = $currentTime
                $failureCounters[$name] = 0
                Write-Log "⏳ 等待 ${startupWaitTime}秒让 $name 启动..." "INFO"
                Start-Sleep -Seconds $startupWaitTime
            }
            continue
        }
        
        # 端口被占用，尝试连接
        $portConnectable = Test-PortConnect $port
        
        if (-not $portConnectable) {
            Write-Log "⚠️  $name 端口 $port 被占用但无法连接" "WARN"
            
            # 检查是否被其他进程占用
            $portOwner = Get-ProcessByPort $port
            if ($portOwner -and $portOwner.ProcessName -ne $procName) {
                Write-Log "❌ 端口 $port 被其他进程占用: $($portOwner.ProcessName) (PID: $($portOwner.Id))" "ERROR"
                $failureCounters[$name]++
                
                if ($failureCounters[$name] -ge $failureThreshold) {
                    Write-Log "💀 端口被其他进程占用，停止 $name 进程..." "WARN"
                    Stop-Target $target
                    $failureCounters[$name] = 0
                }
            } else {
                $failureCounters[$name]++
                
                if ($failureCounters[$name] -ge $failureThreshold) {
                    Write-Log "🔄 $name 端口无法连接，停止并重启..." "WARN"
                    Stop-Target $target
                    $null = Start-Target $target
                    $lastRestartTimes[$name] = $currentTime
                    $failureCounters[$name] = 0
                    Write-Log "⏳ 等待 ${startupWaitTime}秒让 $name 启动..." "INFO"
                    Start-Sleep -Seconds $startupWaitTime
                }
            }
            continue
        }
        
        # 一切正常
        if ($failureCounters[$name] -gt 0) {
            Write-Log "✅ $name 已恢复正常" "INFO"
            $failureCounters[$name] = 0
        } else {
            Write-Log "✅ $name 运行正常 (PID: $($procs[0].Id), 端口: $port)" "INFO"
        }
    }
    
    # 显示状态总结
    Write-Log "📊 状态总结:" "INFO"
    foreach ($target in $targets) {
        $procs = Get-Process -Name $target.ProcName -ErrorAction SilentlyContinue
        $status = if ($procs.Count -gt 0) { "运行中" } else { "停止" }
        Write-Log "  $($target.Name): $status (失败计数: $($failureCounters[$target.Name]))" "INFO"
    }
    
    # 计算下次检测时间
    $nextCheck = $currentTime.AddSeconds($checkInterval)
    Write-Log "⏰ 下次检测: $($nextCheck.ToString('HH:mm:ss'))" "INFO"
    Write-Log "-" * 60
    
    # 等待下次检测
    Start-Sleep -Seconds $checkInterval
}