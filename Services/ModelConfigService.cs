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
        public ModelConfig Add(string baseUrl, string apiKey, string modelName, string modelId, string embeddingModelId = "")
        {
            var model = new ModelConfig
            {
                BaseUrl = baseUrl,
                ApiKey = apiKey,
                ModelName = modelName,
                ModelId = modelId,
                EmbeddingModelId = embeddingModelId,
                IsActive = _models.Count == 0
            };
            _models.Add(model);
            Save();
            return model;
        }

        /// <summary>按标识移除模型配置。</summary>
        public void Remove(string id)
        {
            var model = _models.FirstOrDefault(m => m.Id == id);
            if (model == null) return;

            _models.Remove(model);
            Save();
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
        }

        /// <summary>
        /// 更新已有模型配置，按标识定位。
        /// </summary>
        public void Update(string id, string baseUrl, string apiKey, string modelName, string modelId, string embeddingModelId = "")
        {
            var model = _models.FirstOrDefault(m => m.Id == id);
            if (model == null) return;

            model.BaseUrl = baseUrl;
            model.ApiKey = apiKey;
            model.ModelName = modelName;
            model.ModelId = modelId;
            model.EmbeddingModelId = embeddingModelId;
            Save();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(ConfigFilePath)) return;

                var json = File.ReadAllText(ConfigFilePath);
                var models = JsonSerializer.Deserialize<List<ModelConfig>>(json);
                if (models != null)
                {
                    _models.AddRange(models);
                }
            }
            catch
            {
                // 配置缺失或损坏时保持空列表，避免影响启动
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
            catch
            {
                // 写入失败不阻断界面操作
            }
        }
    }
}
