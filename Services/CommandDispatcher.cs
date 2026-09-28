using KfuPet.Services.Commands;

namespace KfuPet.Services
{
    internal class CommandDispatcher
    {
        /// <summary>
        /// 工具端成批调用的骨骼只读命令：连接时逐根骨骼拉取状态会一次性发出上百条，
        /// 逐条记录会刷屏，因此统一合并成一条摘要日志。
        /// </summary>
        private static readonly HashSet<string> SkeletonReadActions = new(StringComparer.OrdinalIgnoreCase)
        {
            "GetBoneIds", "BoneExists", "GetBoneName", "GetParentBoneId", "GetChildBoneIds",
            "GetPosition", "GetRotation", "GetScale", "IsActive", "GetWorldPosition",
            "GetAttachment", "GetBoneAttachments", "GetAttachmentScale", "GetDebugSkeleton"
        };

        /// <summary>批量查询的静默窗口：窗口内到达的只读命令合并为一条日志。</summary>
        private static readonly TimeSpan ReadSummaryWindow = TimeSpan.FromMilliseconds(300);

        private readonly Dictionary<string, ICommandService> _services = new();
        private readonly object _readSummaryGate = new();

        /// <summary>当前静默窗口内累计的只读命令数，仅在 <see cref="_readSummaryGate"/> 锁内读写。</summary>
        private int _pendingReadCount;

        /// <summary>本批中的最后一个只读命令名，仅用于「窗口内只有一条」时恢复原文日志。</summary>
        private string? _pendingReadAction;

        /// <summary>静默窗口到期后输出摘要的定时器，首次用到时才创建。</summary>
        private System.Threading.Timer? _readSummaryTimer;

        public void RegisterService(ICommandService service)
        {
            _services[service.ServiceName.ToLowerInvariant()] = service;
        }

        public CommandResponse Dispatch(CommandRequest request)
        {
            if (string.IsNullOrEmpty(request.Service))
            {
                Log.Warning("[IPC] 命令缺少 service 字段，已拒绝");
                return CommandResponse.Fail("Service name is required");
            }

            if (string.IsNullOrEmpty(request.Action))
            {
                Log.Warning($"[IPC] 命令缺少 action 字段，已拒绝（service={request.Service}）");
                return CommandResponse.Fail("Action is required");
            }

            var serviceKey = request.Service.ToLowerInvariant();
            if (!_services.TryGetValue(serviceKey, out var service))
            {
                Log.Warning($"[IPC] 未知服务：{request.Service}");
                return CommandResponse.Fail($"Unknown service: {request.Service}");
            }

            try
            {
                var response = service.Execute(request.Action, request.Params);
                if (response.Success)
                {
                    LogSuccess(serviceKey, request.Action);
                }
                else
                {
                    Log.Warning($"[IPC] {serviceKey}.{request.Action} 执行失败：{response.Error}");
                }
                return response;
            }
            catch (Exception ex)
            {
                Log.Error($"[IPC] {serviceKey}.{request.Action} 抛出异常：{ex.Message}");
                return CommandResponse.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 记录一次成功调用。心跳与成批的骨骼查询不逐条记录，避免日志被刷屏。
        /// </summary>
        private void LogSuccess(string serviceKey, string action)
        {
            // 心跳由工具端高频发送，成功的 Ping 没有诊断价值（失败仍会记 Warning）
            if (string.Equals(action, "Ping", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (SkeletonReadActions.Contains(action))
            {
                AccumulateReadQuery(action);
                return;
            }

            Log.Debug($"[IPC] {serviceKey}.{action} 执行成功");
        }

        /// <summary>
        /// 累计一次骨骼只读查询，并把日志推迟到静默窗口之后输出，
        /// 这样一次点击引发的一整批查询只会留下一条日志。
        /// </summary>
        private void AccumulateReadQuery(string action)
        {
            lock (_readSummaryGate)
            {
                _pendingReadCount++;
                _pendingReadAction = action;
                _readSummaryTimer ??= new System.Threading.Timer(
                    _ => FlushReadSummary(), null, ReadSummaryWindow, Timeout.InfiniteTimeSpan);
                _readSummaryTimer.Change(ReadSummaryWindow, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>
        /// 静默窗口到期：成批查询汇总为一条，零星单条查询仍按原文记录（便于定位具体命令）。
        /// </summary>
        private void FlushReadSummary()
        {
            int count;
            string? action;
            lock (_readSummaryGate)
            {
                count = _pendingReadCount;
                action = _pendingReadAction;
                _pendingReadCount = 0;
                _pendingReadAction = null;
            }

            if (count <= 0)
            {
                return;
            }

            if (count == 1 && action != null)
            {
                Log.Debug($"[IPC] skeleton.{action} 执行成功");
            }
            else
            {
                Log.Debug($"[IPC] skeleton 查询骨骼成功（本次 {count} 项）");
            }
        }

        public CommandResponse Dispatch(string service, string action, Dictionary<string, object>? parameters = null)
        {
            return Dispatch(new CommandRequest
            {
                Service = service,
                Action = action,
                Params = parameters
            });
        }
    }
}
