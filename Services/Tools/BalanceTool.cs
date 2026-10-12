namespace KfuPet.Services.Tools
{
    /// <summary>
    /// 账户余额查询工具：仅在用户主动询问余额时由模型调用，
    /// 复用 <see cref="ModelBalanceService"/> 查询“当前使用模型”所属服务商的账户余额。
    /// </summary>
    internal class BalanceTool : ITool
    {
        private readonly ModelConfigService _modelConfigService;

        /// <summary>用模型配置服务构造，用于定位当前正在使用的模型。</summary>
        /// <param name="modelConfigService">本地模型配置服务。</param>
        public BalanceTool(ModelConfigService modelConfigService)
        {
            _modelConfigService = modelConfigService;
        }

        public string Name => "query_balance";

        public string Description =>
            "查询当前使用模型的账户余额（还剩多少钱）。" +
            "只有在用户明确询问余额、剩余额度、账户还有多少钱、费用是否充足时才调用；" +
            "其他任何情况都不要调用本工具。";

        public string ParametersSchemaJson => """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """;

        public async Task<string> ExecuteAsync(string argumentsJson)
        {
            var model = _modelConfigService.Models.FirstOrDefault(m => m.IsActive);
            if (model == null)
            {
                return "当前尚未配置正在使用的模型，无法查询余额。";
            }

            var balance = await ModelBalanceService.QueryAsync(model.BaseUrl, model.ApiKey);
            if (balance == null)
            {
                return $"当前使用模型「{model.ModelName}」的服务商不支持余额查询，或本次查询失败。";
            }

            return $"当前使用模型「{model.ModelName}」的账户余额为 {balance}。";
        }
    }
}
