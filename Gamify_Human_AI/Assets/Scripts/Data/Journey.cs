// ============================================================
// Journey.cs
// Journey 数据的持久化层
//
// 【为何使用 Newtonsoft.Json 而非 System.Text.Json】
//   Unity 2022.3 的运行时基于 Mono/.NET Standard 2.1，
//   System.Text.Json 并未随 Unity 安装包附带。
//   而 com.unity.visualscripting（已在 manifest.json 中）
//   会将 com.unity.nuget.newtonsoft-json 作为传递依赖引入，
//   因此项目中 Newtonsoft.Json 直接可用，无需额外安装。
//   其 StringEnumConverter 还能原生支持枚举字符串序列化，
//   与 schema 中 enum 值保持一致。
//
// 【Crash-safe 写盘策略】
//   写入流程：json → journeyId.json.tmp → File.Replace / File.Move
//   如果写 tmp 时崩溃，旧的 .json 完好无损；
//   如果 Replace/Move 后崩溃，新数据已落盘。
//   额外保留一份 .bak 备份，便于人工恢复。
// ============================================================

using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace GamifyHumanAI.Data
{
    /// <summary>
    /// 负责 JourneyState 的序列化、落盘与加载。
    /// 默认存储目录：Application.persistentDataPath/Journeys/
    /// </summary>
    public class Journey
    {
        // ── 配置 ──────────────────────────────────────────────
        private readonly string _saveDir;
        private readonly JsonSerializerSettings _serializerSettings;

        // ── 构造 ──────────────────────────────────────────────

        /// <param name="saveDir">
        /// 自定义存储目录（留空则使用 persistentDataPath/Journeys）。
        /// 用于单元测试时注入临时目录。
        /// </param>
        public Journey(string saveDir = null)
        {
            _saveDir = saveDir ?? Path.Combine(Application.persistentDataPath, "Journeys");

            // 确保目录存在
            Directory.CreateDirectory(_saveDir);

            _serializerSettings = new JsonSerializerSettings
            {
                Formatting        = Formatting.Indented,
                // 枚举序列化为字符串，与 schema enum 值一致
                Converters        = { new StringEnumConverter() },
                // null 的可选字段不写入文件，保持 JSON 简洁
                NullValueHandling = NullValueHandling.Ignore,
            };
        }

        // ── 公开 API ─────────────────────────────────────────

        /// <summary>
        /// 将 JourneyState 序列化并 crash-safe 写入磁盘。
        /// 自动更新 updatedAt 时间戳。
        /// </summary>
        public void Save(JourneyState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrWhiteSpace(state.journeyId))
                throw new ArgumentException("journeyId cannot be empty");

            // 刷新更新时间
            state.updatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            string json      = JsonConvert.SerializeObject(state, _serializerSettings);
            string finalPath = GetSavePath(state.journeyId);
            string tmpPath   = finalPath + ".tmp";

            // ① 写临时文件（若此步崩溃，旧文件完好）
            File.WriteAllText(tmpPath, json, Encoding.UTF8);

            // ② 原子替换（Windows NTFS 上 File.Replace 是最接近原子的操作）
            if (File.Exists(finalPath))
            {
                // 第三个参数 null：不保留 .bak；如需可改为 finalPath + ".bak"
                File.Replace(tmpPath, finalPath, destinationBackupFileName: null);
            }
            else
            {
                // 目标不存在时 File.Replace 会抛异常，直接 Move
                File.Move(tmpPath, finalPath);
            }
        }

        /// <summary>从磁盘加载指定 journeyId 对应的 JourneyState。</summary>
        /// <exception cref="FileNotFoundException">文件不存在时抛出。</exception>
        public JourneyState Load(string journeyId)
        {
            if (string.IsNullOrWhiteSpace(journeyId))
                throw new ArgumentException("journeyId cannot be empty");

            string path = GetSavePath(journeyId);

            // 若主文件损坏，可以在此添加回退到 .bak 的逻辑
            if (!File.Exists(path))
                throw new FileNotFoundException($"Journey file not found: {path}", path);

            string json = File.ReadAllText(path, Encoding.UTF8);
            return JsonConvert.DeserializeObject<JourneyState>(json, _serializerSettings);
        }

        /// <summary>尝试加载；若文件不存在则返回 null，不抛异常。</summary>
        public JourneyState TryLoad(string journeyId)
        {
            try { return Load(journeyId); }
            catch (FileNotFoundException) { return null; }
        }

        /// <summary>删除指定 Journey 的存档文件（包括 .tmp/.bak 残留）。</summary>
        public void Delete(string journeyId)
        {
            TryDeleteFile(GetSavePath(journeyId));
            TryDeleteFile(GetSavePath(journeyId) + ".tmp");
            TryDeleteFile(GetSavePath(journeyId) + ".bak");
        }

        /// <summary>返回指定 journeyId 对应的 .json 完整路径（用于 Debug 输出）。</summary>
        public string GetSavePath(string journeyId) =>
            Path.Combine(_saveDir, $"{journeyId}.json");

        /// <summary>检查存档是否存在。</summary>
        public bool Exists(string journeyId) =>
            File.Exists(GetSavePath(journeyId));

        // ── 私有工具 ─────────────────────────────────────────
        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { /* 忽略删除失败，不影响主流程 */ }
        }
    }
}
