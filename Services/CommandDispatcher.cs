using KfuPet.Services.Commands;

namespace KfuPet.Services
{
    internal class CommandDispatcher
    {
        private readonly Dictionary<string, ICommandService> _services = new();

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
                    Log.Debug($"[IPC] {serviceKey}.{request.Action} 执行成功");
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
