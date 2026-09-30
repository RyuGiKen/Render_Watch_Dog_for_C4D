# Render Server GUI

Cinema 4D 渲染看门狗与多任务逐帧/分块调度器（Windows 桌面 GUI）  
A Cinema 4D watchdog + multi-task per-frame/chunk render scheduler (Windows desktop GUI)

---

## 模式 / Modes

| 模式 / Mode | 说明 / Description |
|---|---|
| **Team Render（看门狗）** | 常驻监控 `Cinema 4D Team Render Client.exe`，无响应/端口断开/崩溃报告连续达上限即杀并重启；含过热自动休息。Keeps the Team Render client alive; auto-restarts on hangs or crash reports. |
| **Cinema 4D.exe（多任务队列）** | GUI 进程按任务队列串行逐帧/分块渲染，异常自动接续。GUI process, queued per-frame/chunk rendering with auto-resume. |
| **Commandline.exe（多任务队列）** | 与上一模式同一段代码。Same code as above. |

## 关键特性 / Key Features

- **多任务队列** / Task queue  
  C4D / Commandline 各支持最多 99 个任务串行执行；每任务独立设置工程文件、输出模板、帧范围、最大分块与无进展超时，冷却/检查间隔/异常次数等全局共用；非首个任务启动前先等一次帧间冷却。  
  Up to 99 tasks per frame mode run in order; each task has its own project file, output template, frame range, chunk size and no-progress timeout, while cooldowns, intervals and failure limits are global; an inter-task cooldown precedes every start but the first.
- **任务主键** / Task IDs  
  列表显示「序号.任务NNN  工程名 [起-止]」；主键添加时按现有最大值+1 分配、删除不回收（号可不连续），不可编辑，仅作存档标识。  
  Items show "No.TaskNNN  scene [start-end]"; IDs auto-increment on add, are never reused after delete, are read-only, and serve as archive keys only.
- **单帧/分块 N** / Per-frame == chunk with N=1  
  最大分块长度 N≥1（自动夹到任务范围），N=5 时一个进程连渲 5 帧。  
  Max chunk length N≥1 (clamped to the task range); N=5 renders 5 frames per process.
- **进度判定** / Progress detection  
  产物须"存在·非空·距写入≥帧间冷却"才算落定，据此续渲与跳过已完成帧。  
  Output counts as settled only if it exists, is non-empty and hasn't been written for the cooldown; used for resume and skip.
- **异常去抖** / Debounced abnormal counting  
  崩溃报告按时间窗计数：命中+1、达上限才杀、出窗清零。  
  Crash reports are counted within a time window: +1 on hit, kill only at the limit, clear when out of window.
- **挂起=无进展超时** / Hang = no-progress timeout  
  GUI 高负载"无响应"不杀进程；只要产物仍有写入活动（新帧落定或文件更新中）就持续重置计时，块内逐帧产出不空等；超过无进展超时且完全无产物写入才杀进程重试（首现记录、下一轮复查确认）。  
  Under heavy load "not responding" is normal; any output write activity (a settled frame or a file being written) keeps resetting the timer, so multi-frame chunks never idle-wait. The process is killed only after the no-progress timeout with zero output writes (first seen, then re-checked next round).
- **热崩冷却** / Cooldown after kill  
  每次杀进程后按结果插入 1/30/60s 的空载冷却，兼顾满载散热。  
  After each kill an idle cooldown (1/30/60 s by outcome) lets the machine cool down.
- **熔断跳任务** / Circuit breaker skips the task  
  连续多块在起点即失败且无进展 → 判全局问题（资产缺失/未烘缓存/授权），跳过当前任务继续下一个，不再终止整场。  
  Consecutive no-progress chunk starts indicate a global issue (missing asset / unbaked cache / license); the current task is skipped and the queue continues.
- **多语言** / Localization  
  简体中文/繁體中文/English，界面与后续日志一并切换。  
  zh-CN / zh-HK / en-US, switching UI and subsequent logs together.
- **配置持久化** / Persistent settings  
  所有参数存 `settings.xml`，重启恢复。  
  All parameters are saved to `settings.xml` and restored on restart.
- **单实例** / Single instance  
  同一 exe 只允许运行一份，重复启动会静默退出。  
  Only one instance of the exe may run; duplicate launches exit silently.

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
2. 选模式，填主程序/进程名/异常记录路径；单帧模式点"+"添加任务，逐个填工程文件、输出模板、帧范围、分块与无进展超时，可继续"+"复制更多任务。  
Pick a mode and fill paths; in frame modes, add tasks with "+" and set each task's project file, output template, frame range, chunk size and timeout; "+" duplicates the selected task.
3. 点"预览输出文件名并校验目录"核对命名规则与工程实际输出一致。  
Use "Preview" to confirm the naming rule matches the project's real output.
4. 设全局时间参数后"启动"，队列按顺序执行；重开后确认缺帧再填范围续渲。  
Set global timing and Start; the queue runs in order. After a restart, fill the missing range to resume.

## 注意 / Caveats

- 输出必须为图片序列，勿用视频格式。  
Output image sequences, not video containers.
- 未烘焙的动力学/布料/粒子缓存会导致中间帧起渲出错画面，先烘焙。  
Unbaked simulations render wrong pixels from mid-range, bake first.
- 分块省冷启动但块内连渲无冷却，N 取小值并保留块间冷却。  
Chunking saves cold starts but renders back-to-back; keep N modest.
- 任务队列上限 99 个；主键达 int 上限同样拒绝添加（防护，正常到不了）。  
The queue holds at most 99 tasks; IDs are also capped at the int limit (safety guard only).

## 目录 / Layout

```
Render Server GUI/
├─ Program.cs                  入口 + 全局异常兜底 / entry + exception handler
├─ Models/                     参数与配置 / parameters & config
│  ├─ RenderMode.cs            模式枚举 / mode enum
│  ├─ RenderTask.cs            单条渲染任务（工程/模板/范围/分块/超时）/ one render task
│  ├─ ModeProfile.cs           模式参数集（含任务队列）/ per-mode params incl. task queue
│  └─ AppConfig.cs             settings.xml 持久化 / settings persistence
├─ Services/
│  ├─ IRenderController.cs     控制器契约 / controller contract
│  ├─ TeamRenderController.cs  ① 看门狗 / watchdog
│  ├─ FrameRenderController.cs ②③ 任务队列 + 单帧/分块引擎 / task queue + per-frame & chunk engine
│  ├─ FrameScanner.cs          命名展开与落定判定 / naming & settle checks
│  ├─ ProcessHealth.cs         健康信号 / health signals
│  └─ Localizer.cs             多语言 / i18n
├─ UI/MainForm.cs(+Designer)   界面 / UI
├─ i18n/                       zh-CN / zh-HK / en-US
└─ 逐帧渲染调度流程图.html       流程图 / flowchart
```
