# Render Server GUI

Cinema 4D 渲染看门狗与逐帧/分块调度器（Windows 桌面 GUI）  
A Cinema 4D watchdog + per-frame/chunk render scheduler (Windows desktop GUI)

---

## 模式 / Modes

| 模式 / Mode | 说明 / Description |
|---|---|
| **Team Render（看门狗）** | 常驻监控 `Cinema 4D Team Render Client.exe`，无响应/端口断开/崩溃报告连续达上限即杀并重启；含过热自动休息。Keeps the Team Render client alive; auto-restarts on hangs or crash reports. |
| **Cinema 4D.exe（单帧/分块）** | GUI 进程逐帧/分块渲染，异常自动接续。GUI process, per-frame or chunk rendering with auto-resume. |
| **Commandline.exe（单帧/分块）** | 与上一模式同一段代码。Same code as above. |

## 关键特性 / Key Features

- **单帧/分块 N** / Per-frame == chunk with N=1  
  最大分块长度 N≥1（自动夹到任务范围），N=5 时一个进程连渲 5 帧。  
  Max chunk length N≥1 (clamped to the range); N=5 renders 5 frames per process.
- **进度判定** / Progress detection  
  产物须"存在·非空·距写入≥帧间冷却"才算落定，据此续渲与跳过已完成帧。  
  Output counts as settled only if it exists, is non-empty and hasn't been written for the cooldown; used for resume and skip.
- **异常去抖** / Debounced abnormal counting  
  崩溃报告按时间窗计数：命中+1、达上限才杀、出窗清零。  
  Crash reports are counted within a time window: +1 on hit, kill only at the limit, clear when out of window.
- **挂起=无进展超时** / Hang = no-progress timeout  
  GUI 高负载"无响应"不杀进程；连续无新帧落定超过无进展超时才判挂起。  
  Under heavy load "not responding" is normal; a hang is declared only when no new frame appears within the no-progress timeout.
- **热崩冷却** / Cooldown after kill  
  每次杀进程后按结果插入 1/30/60s 的空载冷却，兼顾满载散热。  
  After each kill an idle cooldown (1/30/60 s by outcome) lets the machine cool down.
- **熔断** / Circuit breaker  
  连续多块在起点即失败且无进展 → 停止并告警，防整片被"跳着跳着就当渲完"。  
  Consecutive no-progress chunk starts stop the run with an alert instead of silently skipping everything.
- **多语言** / Localization  
  简体中文/繁體中文/English，界面与后续日志一并切换。  
  zh-CN / zh-HK / en-US, switching UI and subsequent logs together.
- **配置持久化** / Persistent settings  
  所有参数存 `settings.xml`，重启恢复。  
  All parameters are saved to `settings.xml` and restored on restart.

## 多语言 / Localization

- 翻译在 `i18n/*.txt`：文件名=语言码，每行 `key=value`，首行 `#name=` 显示名。  
  Translations live in `i18n/*.txt`: filename = language code, `key=value` per line, `#name=` display name.
- 加语言=放一个新文件，下拉自动出现。  
  Adding a language = drop in a new file.
- 取词处保留中文备用词，缺键/丢文件自动回退简体中文。  
  Every call site keeps a Chinese fallback; missing keys or files fall back to Simplified Chinese without errors.
- 切换后整套 UI 与后续日志生效，已输出旧日志不变。  
  Switching refreshes the whole UI and subsequent logs; older log lines are left as-is.

## 使用 / Quick Start

1. 在渲染机完成 C4D 授权登录，并手动命令行渲一帧确认出图。  
Log in to C4D once and confirm a manual command-line render works.
2. 选模式，填主程序/进程名/异常记录路径；单帧模式再填工程文件与输出模板。  
Pick a mode and fill paths; frame modes also need the project file and output template.
3. 点"预览输出文件名并校验目录"核对命名规则与工程实际输出一致。  
Use "Preview" to confirm the naming rule matches the project's real output.
4. 设帧范围与时间参数后"启动"；重开后确认缺帧再填范围续渲。  
Set range & timing and Start; after a restart, fill the missing range to resume.

## 注意 / Caveats

- 输出必须为图片序列，勿用视频格式。  
Output image sequences, not video containers.
- 未烘焙的动力学/布料/粒子缓存会导致中间帧起渲出错画面，先烘焙。  
Unbaked simulations render wrong pixels from mid-range, bake first.
- 分块省冷启动但块内连渲无冷却，N 取小值并保留块间冷却。  
Chunking saves cold starts but renders back-to-back; keep N modest.

## 目录 / Layout

```
Render Server GUI/
├─ Program.cs                  入口 + 全局异常兜底 / entry + exception handler
├─ Models/                     参数与配置 / parameters & config
├─ Services/
│  ├─ TeamRenderController.cs  ① 看门狗 / watchdog
│  ├─ FrameRenderController.cs ②③ 单帧/分块引擎 / per-frame & chunk engine
│  ├─ FrameScanner.cs          命名展开与落定判定 / naming & settle checks
│  ├─ ProcessHealth.cs         健康信号 / health signals
│  └─ Localizer.cs             多语言 / i18n
├─ UI/MainForm.cs(+Designer)   界面 / UI
├─ i18n/                       zh-CN / zh-HK / en-US
└─ 逐帧渲染调度流程图.html       流程图 / flowchart
```
