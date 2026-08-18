using System.Text.Json.Serialization;

namespace KfuPet.Models
{
    /// <summary>
    /// 日志级别。
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// 单条日志记录，通过日志管道以 JSON 逐行推送给外部工具。
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }

        public LogLevel Level { get; set; }

        public string Message { get; set; } = string.Empty;
    }
}
