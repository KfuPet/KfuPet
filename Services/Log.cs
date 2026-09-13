namespace KfuPet.Services
{
    /// <summary>
    /// 全局日志静态入口：任何类都可以直接写日志，无需持有 LogService 实例。
    /// 全应用共用同一个实例，日志先进入内存缓冲，再由日志管道推送给开发者工具。
    /// </summary>
    internal static class Log
    {
        private static readonly LogService Service = new();

        /// <summary>全局日志服务实例（日志缓冲与管道推送的唯一来源）。</summary>
        public static LogService Instance => Service;

        public static void Debug(string message) => Service.Debug(message);

        public static void Info(string message) => Service.Info(message);

        public static void Warning(string message) => Service.Warning(message);

        public static void Error(string message) => Service.Error(message);
    }
}
