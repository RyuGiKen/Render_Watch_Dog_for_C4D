using System;
using System.Runtime.InteropServices;

namespace RenderServerGui.Services
{
    /// <summary>
    /// 系统唤醒保持：渲染运行期间阻止系统睡眠（不影响显示器熄灭）。
    /// 基于 SetThreadExecutionState：ES_CONTINUOUS 置位后请求持续到下次清除，
    /// 由 UI 主线程调用（进程存续期间不退出），进程结束系统自动回收。
    /// </summary>
    public static class KeepAwake
    {
        /// <summary>持续生效直到下次调用清除。</summary>
        private const uint ES_CONTINUOUS = 0x80000000;
        /// <summary>要求系统保持唤醒（不进入睡眠）。</summary>
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        /// <summary>当前是否已置位（避免重复调用）。</summary>
        public static bool IsActive { get; private set; }

        /// <summary>置位：阻止系统进入睡眠。</summary>
        public static void Enable()
        {
            if (IsActive) return;
            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
            IsActive = true;
        }

        /// <summary>清除：恢复系统默认电源策略。</summary>
        public static void Disable()
        {
            if (!IsActive) return;
            SetThreadExecutionState(ES_CONTINUOUS);
            IsActive = false;
        }
    }
}
