using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>
    /// 管理 AI 模型配置，负责加载 / 保存到本地 JSON 文件（%AppData%\KfuPet\models.json）。
    /// </summary>
    internal class ModelConfigService
    {
        private static readonly string ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KfuPet");

        private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "models.json");

        private readonly List<ModelConfig> _models = new();

        /// <summary>当前已配置的模型列表（只读视图）。</summary>
        public IReadOnlyList<ModelConfig> Models => _models;

        public ModelConfigService()
        {
            Load();
        }

        /// <summary>
        /// 新增一条模型配置。若当前没有任何模型，则新模型默认设为当前使用。
        /// </summary>
        public ModelConfig Add(string baseUrl, string apiKey, string modelName, string modelId)
        {
            var model = new ModelConfig
            {
                BaseUrl = baseUrl,
                ApiKey = apiKey,
                ModelName = modelName,
                ModelId = modelId,
                IsActive = _models.Count == 0
            };
            _models.Add(model);
            Save();
            Log.Info($"[配置] 新增模型：{modelName}（{modelId}）" + (model.IsActive ? "，已设为当前使用" : string.Empty));
            return model;
        }

        /// <summary>按标识移除模型配置。</summary>
        public void Remove(string id)
        {
            var model = _models.FirstOrDefault(m => m.Id == id);
            if (model == null) return;

            _models.Remove(model);
            Save();
            Log.Info($"[配置] 删除模型：{model.ModelName}（{model.ModelId}）");
        }

        /// <summary>
        /// 将指定标识的模型设为当前使用；传入 null 表示取消当前模型（不选中任何模型）。
        /// </summary>
        public void SetActiveModel(string? id)
        {
            foreach (var model in _models)
            {
                model.IsActive = id != null && model.Id == id;
            }
            Save();

            var active = _models.FirstOrDefault(m => m.IsActive);
            Log.Info(active == null
                ? "[配置] 已取消当前使用模型"
                : $"[配置] 当前使用模型已切换为：{active.ModelName}（{active.ModelId}）");
        }

        /// <summary>
        /// 更新已有模型配置，按标识定位。
        /// </summary>
        public void Update(string id, string baseUrl, string apiKey, string modelName, string modelId)
        {
            var model = _models.FirstOrDefault(m => m.Id == id);
            if (model == null) return;

            model.BaseUrl = baseUrl;
            model.ApiKey = apiKey;
            model.ModelName = modelName;
            model.ModelId = modelId;
            Save();
            Log.Info($"[配置] 更新模型：{modelName}（{modelId}）");
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                {
                    Log.Debug("[配置] 未找到模型配置文件，按空列表启动");
                    return;
                }

                var json = File.ReadAllText(ConfigFilePath);
                var models = JsonSerializer.Deserialize<List<ModelConfig>>(json);
                if (models != null)
                {
                    _models.AddRange(models);
                }
                Log.Info($"[配置] 模型配置已加载：{_models.Count} 条");
            }
            catch (Exception ex)
            {
                // 配置缺失或损坏时保持空列表，避免影响启动
                Log.Warning($"[配置] 模型配置读取失败，按空列表启动：{ex.Message}");
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                var json = JsonSerializer.Serialize(_models, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                // 写入失败不阻断界面操作
                Log.Error($"[配置] 模型配置写入失败：{ex.Message}");
            }
        }
    }
}
