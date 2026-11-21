using Verse;

namespace Botania
{
    /// <summary>
    /// 调试日志开关。
    ///
    /// 模组设置里默认关闭；只有玩家在设置里勾选「输出调试日志」后，
    /// 才会把 [Botania] 开头的流程日志写进 Player.log。
    /// 错误与警告（Log.Error / Log.Warning）始终输出，不受此开关影响。
    /// </summary>
    public static class BotaniaDebug
    {
        private static BotaniaModSettings settings;

        public static bool Enabled
        {
            get
            {
                if (settings == null)
                {
                    settings = BotaniaModSettings.Instance;
                }
                if (settings != null)
                {
                    return settings.debugLogging;
                }
                // 设置对象还没加载好时，按 Mod 的 settings 再取一次
                BotaniaMod mod = LoadedModManager.GetMod<BotaniaMod>();
                if (mod != null)
                {
                    settings = mod.GetSettings<BotaniaModSettings>();
                    if (settings != null)
                    {
                        return settings.debugLogging;
                    }
                }
                return false;
            }
        }

        /// <summary>仅在开启调试日志时输出。</summary>
        public static void Log(string message)
        {
            if (Enabled)
            {
                Verse.Log.Message(message);
            }
        }

        /// <summary>无论开关都输出（用于初始化、版本等一次性信息）。</summary>
        public static void LogAlways(string message)
        {
            Verse.Log.Message(message);
        }
    }
}
