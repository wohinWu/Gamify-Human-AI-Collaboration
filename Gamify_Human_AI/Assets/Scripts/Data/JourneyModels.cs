// ============================================================
// JourneyModels.cs
// 数据模型定义 + 轻量运行时校验器
// 对应 Schema：Assets/Schemas/journey.schema.json  schemaVersion=1
// ============================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace GamifyHumanAI.Data
{
    // ──────────────────────────────────────────────────────────
    // 枚举（全部序列化为字符串，与 schema enum 值完全对齐）
    // ──────────────────────────────────────────────────────────

    /// <summary>Journey 整体状态</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum JourneyStatus
    {
        Draft,
        Active,
        Paused,
        Finished
    }

    /// <summary>AI 角色当前工作状态</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum RoleState
    {
        Idle,
        Generating,
        FinishedWaitingView,
        Error,
        NotFound
    }

    /// <summary>路线图节点的解锁/完成状态</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum NodeStatus
    {
        locked,
        unlocked,
        in_progress,
        completed
    }

    /// <summary>事件日志的事件类型</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum EventType
    {
        user_action,
        role_state_changed,
        map_update_applied,
        reminder
    }

    // ──────────────────────────────────────────────────────────
    // 子对象
    // ──────────────────────────────────────────────────────────

    /// <summary>角色绑定的 OS 窗口/浏览器标签信息</summary>
    [Serializable]
    public class WindowBinding
    {
        /// <summary>窗口/标签唯一 ID（由外部窗口管理器分配）</summary>
        public string windowId;

        /// <summary>用于识别窗口的标题关键词</summary>
        public string titleHint;

        /// <summary>（可选）URL 匹配提示，仅浏览器标签适用</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string urlHint;
    }

    /// <summary>参与本 Journey 的 AI 角色</summary>
    [Serializable]
    public class Role
    {
        public string roleId;
        public string name;

        /// <summary>头像资源键名，用于 UI 查找 Sprite</summary>
        public string avatarKey;

        public WindowBinding windowBinding;
        public RoleState roleState;

        /// <summary>角色状态最后变更时间（Unix 秒）</summary>
        public long lastUpdateAt;
    }

    /// <summary>节点已完成的证据（由 AI 角色输出触发）</summary>
    [Serializable]
    public class Evidence
    {
        /// <summary>提供证据的角色 ID</summary>
        public string sourceRoleId;

        /// <summary>原文引用片段</summary>
        public string quote;

        /// <summary>触发完成判定的规则名称</summary>
        public string rule;

        /// <summary>置信度，范围 [0, 1]</summary>
        public float confidence;
    }

    /// <summary>路线图中的单个学习节点</summary>
    [Serializable]
    public class RoadmapNode
    {
        public string nodeId;
        public string title;

        /// <summary>（可选）节点详细描述</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string description;

        /// <summary>显示顺序，从 0 开始，不可为负</summary>
        public int order;

        public NodeStatus status;

        /// <summary>（可选）节点完成证据，status=completed 时应填充</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Evidence evidence;
    }

    /// <summary>事件日志条目，替代 journal 做简单事件记录</summary>
    [Serializable]
    public class EventRecord
    {
        public string eventId;
        public EventType eventType;

        /// <summary>事件发生时间（Unix 秒）</summary>
        public long timestamp;

        /// <summary>人类可读的事件摘要</summary>
        public string summary;

        /// <summary>（可选）关联的路线图节点 ID</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string relatedNodeId;

        /// <summary>（可选）触发本事件的角色 ID</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string sourceRoleId;
    }

    // ──────────────────────────────────────────────────────────
    // 根对象
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Journey 的完整数据状态，对应 journey.schema.json。
    /// 通过 JourneyRepository 持久化，通过 JourneyValidator 做基础校验。
    /// </summary>
    [Serializable]
    public class JourneyState
    {
        /// <summary>Schema 版本，当前固定为 1，用于未来迁移判断</summary>
        public int schemaVersion = 1;

        /// <summary>全局唯一标识（GUID 字符串），同时作为文件名</summary>
        public string journeyId;

        public string name;
        public string goal;
        public JourneyStatus status;

        /// <summary>创建时间（Unix 秒）</summary>
        public long createdAt;

        /// <summary>最后更新时间（Unix 秒），Save 时自动刷新</summary>
        public long updatedAt;

        public List<Role>        roles    = new List<Role>();
        public List<RoadmapNode> roadmap  = new List<RoadmapNode>();
        public List<EventRecord> eventLog = new List<EventRecord>();
    }

    // ──────────────────────────────────────────────────────────
    // 校验结果
    // ──────────────────────────────────────────────────────────

    /// <summary>ValidateBasics 的返回结构</summary>
    public class ValidationResult
    {
        public bool         IsValid;
        public List<string> Errors = new List<string>();

        public override string ToString() =>
            IsValid ? "Validation passed" : $"Validation failed ({Errors.Count} errors):\n" + string.Join("\n", Errors);
    }

    // ──────────────────────────────────────────────────────────
    // 轻量运行时校验器（覆盖 schema 的 required / enum / 唯一性约束）
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 不依赖第三方 JSON Schema 库，手动检查最关键的约束。
    /// 校验范围：required 字段非空、enum 合法性、order 非负、nodeId 唯一。
    /// </summary>
    public static class JourneyValidator
    {
        /// <summary>对 JourneyState 执行基础校验，返回含所有错误的 ValidationResult。</summary>
        public static ValidationResult ValidateBasics(JourneyState state)
        {
            var result = new ValidationResult();
            if (state == null)
            {
                result.Errors.Add("JourneyState is null");
                return result;
            }

            // ── 根字段 ──────────────────────────────────────
            if (state.schemaVersion != 1)
                result.Errors.Add($"schemaVersion must be 1, got {state.schemaVersion}");

            RequireNonEmpty(result, state.journeyId, "journeyId");
            RequireNonEmpty(result, state.name,      "name");
            RequireNonEmpty(result, state.goal,      "goal");

            if (state.createdAt <= 0)
                result.Errors.Add("createdAt must be > 0");

            // ── Roles ────────────────────────────────────────
            for (int i = 0; i < state.roles.Count; i++)
            {
                var role   = state.roles[i];
                string pfx = $"roles[{i}]";

                RequireNonEmpty(result, role.roleId,    $"{pfx}.roleId");
                RequireNonEmpty(result, role.name,      $"{pfx}.name");
                RequireNonEmpty(result, role.avatarKey, $"{pfx}.avatarKey");

                if (role.windowBinding == null)
                {
                    result.Errors.Add($"{pfx}.windowBinding is null");
                }
                else
                {
                    RequireNonEmpty(result, role.windowBinding.windowId, $"{pfx}.windowBinding.windowId");
                    RequireNonEmpty(result, role.windowBinding.titleHint, $"{pfx}.windowBinding.titleHint");
                }

                if (role.lastUpdateAt <= 0)
                    result.Errors.Add($"{pfx}.lastUpdateAt must be > 0");
            }

            // ── RoadmapNodes（含 nodeId 唯一性）────────────────
            var seenNodeIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < state.roadmap.Count; i++)
            {
                var node   = state.roadmap[i];
                string pfx = $"roadmap[{i}]";

                RequireNonEmpty(result, node.nodeId, $"{pfx}.nodeId");
                RequireNonEmpty(result, node.title,  $"{pfx}.title");

                if (!string.IsNullOrEmpty(node.nodeId))
                {
                    if (!seenNodeIds.Add(node.nodeId))
                        result.Errors.Add($"Duplicate nodeId: \"{node.nodeId}\"");
                }

                if (node.order < 0)
                    result.Errors.Add($"{pfx}.order must be >= 0 (got {node.order})");

                if (node.evidence != null)
                {
                    RequireNonEmpty(result, node.evidence.sourceRoleId, $"{pfx}.evidence.sourceRoleId");
                    RequireNonEmpty(result, node.evidence.rule,         $"{pfx}.evidence.rule");

                    if (node.evidence.confidence < 0f || node.evidence.confidence > 1f)
                        result.Errors.Add($"{pfx}.evidence.confidence must be [0,1] (got {node.evidence.confidence})");
                }
            }

            // ── EventLog ─────────────────────────────────────
            for (int i = 0; i < state.eventLog.Count; i++)
            {
                var evt    = state.eventLog[i];
                string pfx = $"eventLog[{i}]";

                RequireNonEmpty(result, evt.eventId, $"{pfx}.eventId");
                RequireNonEmpty(result, evt.summary, $"{pfx}.summary");

                if (evt.timestamp <= 0)
                    result.Errors.Add($"{pfx}.timestamp must be > 0");

                // 校验 relatedNodeId 若填写则必须存在于 roadmap
                if (!string.IsNullOrEmpty(evt.relatedNodeId) && !seenNodeIds.Contains(evt.relatedNodeId))
                    result.Errors.Add($"{pfx}.relatedNodeId \"{evt.relatedNodeId}\" not found in roadmap");
            }

            result.IsValid = result.Errors.Count == 0;
            return result;
        }

        // ── 私有工具 ─────────────────────────────────────────
        private static void RequireNonEmpty(ValidationResult r, string value, string fieldPath)
        {
            if (string.IsNullOrWhiteSpace(value))
                r.Errors.Add($"{fieldPath} cannot be empty");
        }
    }
}
