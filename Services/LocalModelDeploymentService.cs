using System.Text.Json;
using KfuPet.Models;

namespace KfuPet.Services
{
    /// <summary>deployed-models.json 的存储结构：多个已下载模型 + 当前使用的一个。</summary>
    internal sealed class DeployedModelLibrary
    {
        /// <summary>已下载模型的记录列表。</summary>
        public List<DeployedModel> Models { get; set; } = new();

        /// <summary>当前使用的模型文件名；空表示尚未选择。</summary>
        public string? ActiveFileName { get; set; }
    }

    /// <summary>管理所有已下载模型：新增、切换当前模型、卸载单个模型。</summary>
    internal sealed class LocalModelDeploymentService
    {
        private const string ConfigFileName = "deployed-models.json";

        private static readonly string ConfigFilePath = Path.Combine(AppPaths.ConfigDirectory, ConfigFileName);

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly object _sync = new();

        /// <summary>模型默认存放目录（缓存根目录下的 Models 子目录）。</summary>
        public static string DefaultDirectory => AppPaths.ModelsDirectory;

        /// <summary>当前模型存放目录：设置里指定了目录时用自定义路径，否则用默认目录。</summary>
        public static string ResolveDirectory()
        {
            var configured = SettingsService.Instance.LocalModelDirectory;
            return string.IsNullOrWhiteSpace(configured) ? DefaultDirectory : configured;
        }

        /// <summary>读取全部已下载模型记录；缺失文件时按模型目录里已有的 GGUF 自动重建。</summary>
        public IReadOnlyList<DeployedModel> LoadAll()
        {
            lock (_sync)
            {
                return LoadValidated().Models.ToList();
            }
        }

        /// <summary>读取当前使用的模型；未启用任何模型时返回 null。</summary>
        public DeployedModel? LoadActive()
        {
            lock (_sync)
            {
                var library = LoadValidated();
                return library.Models.FirstOrDefault(m => m.FileName == library.ActiveFileName);
            }
        }

        /// <summary>新增或更新模型记录，并把新下载的模型设为当前使用。</summary>
        public void Save(DeployedModel model)
        {
            lock (_sync)
            {
                var library = LoadValidated();
                library.Models.RemoveAll(m =>
                    string.Equals(m.FileName, model.FileName, StringComparison.OrdinalIgnoreCase));
                library.Models.Add(model);
                library.ActiveFileName = model.FileName;
                Write(library);
            }
        }

        /// <summary>切换当前使用的模型。</summary>
        public void SetActive(string fileName)
        {
            lock (_sync)
            {
                var library = LoadValidated();
                if (library.Models.Any(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    library.ActiveFileName = fileName;
                    Write(library);
                }
                else
                {
                    Log.Warning($"[本地模型] 要启用的模型不在部署记录中：{fileName}");
                }
            }
        }

        /// <summary>取消当前使用的模型（不选任何本地模型）。</summary>
        public void ClearActive()
        {
            lock (_sync)
            {
                var library = LoadValidated();
                library.ActiveFileName = null;
                Write(library);
            }
        }

        /// <summary>删除单个模型文件与记录；若删的是当前启用的模型，则回到“未启用”，不会自动启用其他模型。</summary>
        public void DeleteFiles(DeployedModel model)
        {
            lock (_sync)
            {
                var library = LoadValidated();
                var wasActive = string.Equals(library.ActiveFileName, model.FileName, StringComparison.OrdinalIgnoreCase);
                library.Models.RemoveAll(m =>
                    string.Equals(m.FileName, model.FileName, StringComparison.OrdinalIgnoreCase));
                if (wasActive)
                {
                    library.ActiveFileName = null;
                }
                Write(library);

                try
                {
                    if (File.Exists(model.FilePath))
                    {
                        File.Delete(model.FilePath);
                    }
                }
                catch (Exception ex)
                {
                    // 文件删不掉时记录已移除，下次启动不会再引用它
                    Log.Warning($"[本地模型] 模型文件删除失败：{model.FilePath}（{ex.Message}）");
                }
            }
        }

        /// <summary>模型在本地磁盘的落地路径；directory 为空时使用设置里的目录。</summary>
        public string GetTargetPath(LocalModelOption option, string? directory = null)
        {
            var root = string.IsNullOrWhiteSpace(directory) ? ResolveDirectory() : directory;
            return Path.Combine(root, option.FileName);
        }

        private DeployedModelLibrary LoadValidated()
        {
            // deployed-models.json 丢失时（例如缓存清理删除过），按模型目录里已有的 GGUF 自动重建
            if (!File.Exists(ConfigFilePath))
            {
                var recovered = TryRecoverFromModelDirectory();
                if (recovered != null)
                {
                    Write(recovered);
                    Log.Info($"[本地模型] 部署记录缺失，已按模型目录重建 {recovered.Models.Count} 条");
                    return recovered;
                }
            }

            var library = ReadLibrary();

            var missing = library.Models.RemoveAll(m => !File.Exists(m.FilePath));
            if (library.Models.Count == 0 ||
                string.IsNullOrEmpty(library.ActiveFileName) ||
                library.Models.All(m => m.FileName != library.ActiveFileName))
            {
                // 未启用或原启用模型已不存在：保持“不启用任何模型”，由界面开关重新选择
                library.ActiveFileName = null;
            }

            if (missing > 0)
            {
                Log.Warning($"[本地模型] 有 {missing} 条部署记录对应的模型文件已不存在，已从记录中移除");
                Write(library);
            }

            return library;
        }

        /// <summary>扫描当前模型目录，把目录里定义的已知 GGUF 重新登记为已下载记录。</summary>
        private static DeployedModelLibrary? TryRecoverFromModelDirectory()
        {
            var root = ResolveDirectory();
            var library = new DeployedModelLibrary();

            foreach (var option in LocalModelCatalog.All)
            {
                var path = Path.Combine(root, option.FileName);
                if (!File.Exists(path))
                {
                    continue;
                }

                library.Models.Add(new DeployedModel
                {
                    ModelName = option.Name,
                    FileName = option.FileName,
                    FilePath = path,
                    SizeBytes = new FileInfo(path).Length,
                    DeployedAt = File.GetLastWriteTime(path),
                    Parameters = option.Parameters,
                    Quantization = option.Quantization
                });
            }

            if (library.Models.Count == 0)
            {
                return null;
            }

            // 默认选目录中最大的已下载模型（目录顺序为 1.7B、4B、8B）
            library.ActiveFileName = library.Models[^1].FileName;
            return library;
        }

        /// <summary>读取配置；兼容早期只存单个模型的 deployed-models.json。</summary>
        private static DeployedModelLibrary ReadLibrary()
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                {
                    return new DeployedModelLibrary();
                }

                var json = File.ReadAllText(ConfigFilePath);

                try
                {
                    var library = JsonSerializer.Deserialize<DeployedModelLibrary>(json);
                    if (library?.Models is { Count: > 0 })
                    {
                        return library;
                    }
                }
                catch
                {
                    // 旧格式是单个对象，继续尝试下面的兼容读取
                }

                var single = JsonSerializer.Deserialize<DeployedModel>(json);
                if (single != null)
                {
                    return new DeployedModelLibrary
                    {
                        Models = new List<DeployedModel> { single },
                        ActiveFileName = single.FileName
                    };
                }
            }
            catch (Exception ex)
            {
                // 配置损坏时按空库处理
                Log.Warning($"[本地模型] 部署记录读取失败，按空库处理：{ex.Message}");
            }

            return new DeployedModelLibrary();
        }

        private static void Write(DeployedModelLibrary library)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.ConfigDirectory);
                File.WriteAllText(ConfigFilePath,
                    JsonSerializer.Serialize(library, JsonOptions));
            }
            catch (Exception ex)
            {
                // 记录写入失败不影响当前会话；下次启动会按文件存在性重新校验
                Log.Error($"[本地模型] 部署记录写入失败：{ex.Message}");
            }
        }
    }
}
