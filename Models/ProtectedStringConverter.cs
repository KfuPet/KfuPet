using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KfuPet.Services;

namespace KfuPet.Models
{
    public class ProtectedStringConverter : JsonConverter<string>
    {
        /// <summary>密文前缀，同时用于判断配置文件中是否还残留历史明文。</summary>
        public const string CipherPrefix = "dpapi:";

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (string.IsNullOrEmpty(value) || !value.StartsWith(CipherPrefix, StringComparison.Ordinal))
            {
                // 历史明文：原样返回
                return value ?? string.Empty;
            }

            try
            {
                var cipher = Convert.FromBase64String(value[CipherPrefix.Length..]);
                var plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex)
            {
                Log.Warning($"[配置] 密钥解密失败，需要重新填写：{ex.Message}");
                return string.Empty;
            }
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (string.IsNullOrEmpty(value))
            {
                writer.WriteStringValue(string.Empty);
                return;
            }

            try
            {
                var cipher = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
                writer.WriteStringValue(CipherPrefix + Convert.ToBase64String(cipher));
            }
            catch (Exception ex)
            {
                Log.Error($"[配置] 密钥加密失败，已跳过写入：{ex.Message}");
                writer.WriteStringValue(string.Empty);
            }
        }
    }
}
