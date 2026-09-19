namespace RenderServerGui.Models
{
    /// <summary>
    /// 三种运行模式。
    /// </summary>
    public enum RenderMode
    {
        /// <summary>Team Render 客户端看门狗（常驻启动/重启）。</summary>
        TeamRender,

        /// <summary>Cinema 4D.exe 单帧逐帧调度。</summary>
        Cinema4D,

        /// <summary>Commandline.exe 单帧逐帧调度。</summary>
        Commandline
    }

    /// <summary>
    /// 逐帧/分块模式在单帧连续失败达到"帧最大失败次数"后的处理策略。
    /// </summary>
    public enum OnFailBehaviour
    {
        /// <summary>停止调度并告警。</summary>
        Stop,

        /// <summary>跳过该帧，继续下一帧。</summary>
        Skip
    }
}
