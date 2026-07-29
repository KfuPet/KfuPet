using System.IO.Pipes;
using System.Text.Json;

namespace KfuPet.Ipc.Client
{
    public class SkeletonPipeClient : IDisposable
    {
        private const string PipeName = "KfuPet.Skeleton";
        private const int DefaultTimeoutMs = 5000;

        private readonly string _pipeName;
        private readonly int _timeoutMs;
        private bool _disposed;

        public SkeletonPipeClient(string pipeName = PipeName, int timeoutMs = DefaultTimeoutMs)
        {
            _pipeName = pipeName;
            _timeoutMs = timeoutMs;
        }

        private async Task<IpcResponse> SendRequestAsync(string service, string action, object? parameters, CancellationToken ct)
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            try
            {
                await client.ConnectAsync(_timeoutMs, ct);
            }
            catch (TimeoutException)
            {
                return IpcResponse.Fail("Connection timeout");
            }

            using var reader = new StreamReader(client);
            using var writer = new StreamWriter(client) { AutoFlush = true };

            var request = new IpcRequest
            {
                Service = service,
                Action = action,
                Params = parameters != null
                    ? JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(parameters))
                    : null
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), ct);
            var responseJson = await reader.ReadLineAsync(ct);

            if (string.IsNullOrEmpty(responseJson))
            {
                return IpcResponse.Fail("Empty response");
            }

            var response = JsonSerializer.Deserialize<IpcResponse>(responseJson);
            return response ?? IpcResponse.Fail("Invalid response");
        }

        private async Task<T?> CallSkeletonAsync<T>(string action, object? parameters = null, CancellationToken ct = default)
        {
            var response = await SendRequestAsync("skeleton", action, parameters, ct);
            if (!response.Success)
            {
                throw new InvalidOperationException(response.Error ?? "Unknown error");
            }
            if (response.Data == null) return default;
            if (response.Data is JsonElement je)
            {
                return je.Deserialize<T>();
            }
            if (response.Data is T typed) return typed;
            return default;
        }

        private async Task<bool> CallSkeletonBoolAsync(string action, object? parameters = null, CancellationToken ct = default)
        {
            var response = await SendRequestAsync("skeleton", action, parameters, ct);
            return response.Success;
        }

        /// <summary>
        /// 专门用于返回 JSON 对象（如坐标 {x,y}）的调用，
        /// 通过 ValueKind 判断空值，避免 JsonElement? 的成员访问限制。
        /// </summary>
        private async Task<JsonElement> CallSkeletonJsonAsync(string action, object? parameters = null, CancellationToken ct = default)
        {
            var response = await SendRequestAsync("skeleton", action, parameters, ct);
            if (!response.Success)
            {
                throw new InvalidOperationException(response.Error ?? "Unknown error");
            }
            if (response.Data is JsonElement je) return je;
            return default;
        }

        /// <summary>
        /// 从 JsonElement 中提取 (x, y) 坐标，空值返回 null。
        /// </summary>
        private static (double X, double Y)? ExtractPoint(in JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Null || je.ValueKind == JsonValueKind.Undefined) return null;
            return (je.GetProperty("x").GetDouble(), je.GetProperty("y").GetDouble());
        }

        public IReadOnlyList<string> GetBoneIds()
        {
            return CallSkeletonAsync<IReadOnlyList<string>>("GetBoneIds").GetAwaiter().GetResult()
                ?? Array.Empty<string>();
        }

        public bool BoneExists(string boneId)
        {
            return CallSkeletonBoolAsync("BoneExists", new { boneId }).GetAwaiter().GetResult();
        }

        public string? GetBoneName(string boneId)
        {
            return CallSkeletonAsync<string?>("GetBoneName", new { boneId }).GetAwaiter().GetResult();
        }

        public string? GetParentBoneId(string boneId)
        {
            return CallSkeletonAsync<string?>("GetParentBoneId", new { boneId }).GetAwaiter().GetResult();
        }

        public IReadOnlyList<string> GetChildBoneIds(string boneId)
        {
            return CallSkeletonAsync<IReadOnlyList<string>>("GetChildBoneIds", new { boneId }).GetAwaiter().GetResult()
                ?? Array.Empty<string>();
        }

        public bool SetPosition(string boneId, double x, double y)
        {
            return CallSkeletonBoolAsync("SetPosition", new { boneId, x, y }).GetAwaiter().GetResult();
        }

        public async Task<bool> SetPositionAsync(string boneId, double x, double y, CancellationToken ct = default)
        {
            return await CallSkeletonBoolAsync("SetPosition", new { boneId, x, y }, ct);
        }

        public (double X, double Y)? GetPosition(string boneId)
        {
            var je = CallSkeletonJsonAsync("GetPosition", new { boneId }).GetAwaiter().GetResult();
            return ExtractPoint(je);
        }

        public bool Translate(string boneId, double deltaX, double deltaY)
        {
            return CallSkeletonBoolAsync("Translate", new { boneId, deltaX, deltaY }).GetAwaiter().GetResult();
        }

        public bool SetRotation(string boneId, double degrees)
        {
            return CallSkeletonBoolAsync("SetRotation", new { boneId, degrees }).GetAwaiter().GetResult();
        }

        public async Task<bool> SetRotationAsync(string boneId, double degrees, CancellationToken ct = default)
        {
            return await CallSkeletonBoolAsync("SetRotation", new { boneId, degrees }, ct);
        }

        public double? GetRotation(string boneId)
        {
            return CallSkeletonAsync<double?>("GetRotation", new { boneId }).GetAwaiter().GetResult();
        }

        public bool Rotate(string boneId, double deltaDegrees)
        {
            return CallSkeletonBoolAsync("Rotate", new { boneId, deltaDegrees }).GetAwaiter().GetResult();
        }

        public bool SetScale(string boneId, double scaleX, double scaleY)
        {
            return CallSkeletonBoolAsync("SetScale", new { boneId, scaleX, scaleY }).GetAwaiter().GetResult();
        }

        public (double X, double Y)? GetScale(string boneId)
        {
            var je = CallSkeletonJsonAsync("GetScale", new { boneId }).GetAwaiter().GetResult();
            return ExtractPoint(je);
        }

        public bool SetActive(string boneId, bool isActive)
        {
            return CallSkeletonBoolAsync("SetActive", new { boneId, isActive }).GetAwaiter().GetResult();
        }

        public bool? IsActive(string boneId)
        {
            return CallSkeletonAsync<bool?>("IsActive", new { boneId }).GetAwaiter().GetResult();
        }

        public bool ResetBone(string boneId)
        {
            return CallSkeletonBoolAsync("ResetBone", new { boneId }).GetAwaiter().GetResult();
        }

        public void ResetAll()
        {
            CallSkeletonBoolAsync("ResetAll").GetAwaiter().GetResult();
        }

        /// <summary>
        /// 心跳检测，检查 KfuPet 服务是否存活。
        /// 不会抛出异常，返回 true 表示连接正常。
        /// </summary>
        public bool Ping()
        {
            return PingAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// 心跳检测（异步），检查 KfuPet 服务是否存活。
        /// 不会抛出异常，返回 true 表示连接正常。
        /// </summary>
        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            try
            {
                var response = await SendRequestAsync("skeleton", "Ping", null, ct);
                return response.Success;
            }
            catch
            {
                return false;
            }
        }

        public (double X, double Y)? GetWorldPosition(string boneId)
        {
            var je = CallSkeletonJsonAsync("GetWorldPosition", new { boneId }).GetAwaiter().GetResult();
            return ExtractPoint(je);
        }

        public bool Batch(Action<BatchBuilder> build)
        {
            var builder = new BatchBuilder();
            build(builder);
            return CallSkeletonBoolAsync("Batch", new { operations = builder.Operations }).GetAwaiter().GetResult();
        }

        public async Task<bool> BatchAsync(Action<BatchBuilder> build, CancellationToken ct = default)
        {
            var builder = new BatchBuilder();
            build(builder);
            return await CallSkeletonBoolAsync("Batch", new { operations = builder.Operations }, ct);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }

    public class BatchBuilder
    {
        public List<object> Operations { get; } = new List<object>();

        public BatchBuilder SetPosition(string boneId, double x, double y)
        {
            Operations.Add(new { action = "SetPosition", @params = new { boneId, x, y } });
            return this;
        }

        public BatchBuilder SetRotation(string boneId, double degrees)
        {
            Operations.Add(new { action = "SetRotation", @params = new { boneId, degrees } });
            return this;
        }

        public BatchBuilder SetScale(string boneId, double scaleX, double scaleY)
        {
            Operations.Add(new { action = "SetScale", @params = new { boneId, scaleX, scaleY } });
            return this;
        }

        public BatchBuilder Translate(string boneId, double deltaX, double deltaY)
        {
            Operations.Add(new { action = "Translate", @params = new { boneId, deltaX, deltaY } });
            return this;
        }

        public BatchBuilder Rotate(string boneId, double deltaDegrees)
        {
            Operations.Add(new { action = "Rotate", @params = new { boneId, deltaDegrees } });
            return this;
        }

        public BatchBuilder SetActive(string boneId, bool isActive)
        {
            Operations.Add(new { action = "SetActive", @params = new { boneId, isActive } });
            return this;
        }

        public BatchBuilder ResetBone(string boneId)
        {
            Operations.Add(new { action = "ResetBone", @params = new { boneId } });
            return this;
        }
    }

    internal class IpcRequest
    {
        public string Service { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public Dictionary<string, object>? Params { get; set; }
    }

    internal class IpcResponse
    {
        public bool Success { get; set; }
        public object? Data { get; set; }
        public string? Error { get; set; }

        public static IpcResponse Fail(string error)
        {
            return new IpcResponse { Success = false, Error = error };
        }
    }
}
