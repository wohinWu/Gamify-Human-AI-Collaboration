// ============================================================
// AgentEventModels.cs
// Agent 通信事件的数据传输对象（DTO）层
//
// 职责：
//   · 定义从 Agent 侧（未来通过 WebSocket/HTTP）收到的事件结构
//   · 当前版本为"平铺信封"设计：所有事件字段共用同一个 AgentEventEnvelope，
//     通过 type 字段区分语义，未用字段保持 null / 0。
//   · 不含任何业务逻辑，仅作反序列化承载体。
//
// 关于 JSON 库选择（System.Text.Json vs Newtonsoft.Json）：
//   Unity 2022.3 基于 Mono + .NET Standard 2.1，
//   System.Text.Json 并未随 Unity 安装包附带，需额外引入 NuGet。
//   而 com.unity.visualscripting（项目已安装）已将 Newtonsoft.Json
//   作为传递依赖引入，故统一使用 Newtonsoft.Json，
//   避免引入额外依赖造成包体膨胀或 IL2CPP 兼容问题。
// ============================================================

using Newtonsoft.Json;

namespace GamifyHumanAI.Net
{
    // ──────────────────────────────────────────────────────────
    // 事件类型常量（对应 schema eventType enum 的扩展集）
    // ──────────────────────────────────────────────────────────

    /// <summary>Agent 事件的 type 字段合法值</summary>
    public static class AgentEventTypes
    {
        /// <summary>角色工作状态变更</summary>
        public const string RoleStateChanged  = "role_state_changed";

        /// <summary>地图节点状态更新提案（含置信度与证据）</summary>
        public const string MapUpdateProposal = "map_update_proposal";

        /// <summary>Agent 发出的提醒/催办事件</summary>
        public const string ReminderProposal  = "reminder_proposal";
    }

    // ──────────────────────────────────────────────────────────
    // 公共信封（平铺结构，字段按事件类型分组注释）
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 所有 Agent 事件共用的 JSON 信封。
    /// 反序列化后通过 <see cref="type"/> 判断有效字段集。
    ///
    /// 示例 JSON（role_state_changed）：
    /// <code>
    /// {
    ///   "type": "role_state_changed",
    ///   "roleId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    ///   "roleState": "Generating",
    ///   "contentSnippet": "正在分析第 2 章..."
    /// }
    /// </code>
    ///
    /// 示例 JSON（map_update_proposal）：
    /// <code>
    /// {
    ///   "type": "map_update_proposal",
    ///   "nodeId": "node-data-layer",
    ///   "proposedStatus": "completed",
    ///   "confidence": 0.92,
    ///   "evidence": {
    ///     "sourceRoleId": "xxxxxxxx-...",
    ///     "quote": "数据层所有单元测试通过。",
    ///     "rule": "all_unit_tests_pass",
    ///     "confidence": 0.92
    ///   }
    /// }
    /// </code>
    ///
    /// 示例 JSON（reminder_proposal）：
    /// <code>
    /// {
    ///   "type": "reminder_proposal",
    ///   "severity": "normal",
    ///   "message": "距离目标截止日期还有 2 天，建议推进第 2 节点。"
    /// }
    /// </code>
    /// </summary>
    public class AgentEventEnvelope
    {
        // ── 公共字段 ─────────────────────────────────────────
        /// <summary>事件类型标识符，见 <see cref="AgentEventTypes"/>。</summary>
        [JsonProperty("type")]
        public string type;

        // ── role_state_changed 专属 ───────────────────────────
        /// <summary>目标角色的 roleId（对应 JourneyState.roles[].roleId）。</summary>
        [JsonProperty("roleId")]
        public string roleId;

        /// <summary>角色新状态字符串（对应 RoleState 枚举名）。</summary>
        [JsonProperty("roleState")]
        public string roleState;

        /// <summary>（可选）角色当前输出片段，仅写入 eventLog summary，不入核心字段。</summary>
        [JsonProperty("contentSnippet")]
        public string contentSnippet;

        // ── map_update_proposal 专属 ─────────────────────────
        /// <summary>提案目标节点的 nodeId（对应 JourneyState.roadmap[].nodeId）。</summary>
        [JsonProperty("nodeId")]
        public string nodeId;

        /// <summary>提案节点新状态字符串（对应 NodeStatus 枚举名）。</summary>
        [JsonProperty("proposedStatus")]
        public string proposedStatus;

        /// <summary>整体提案置信度 [0, 1]，≥ 0.8 且证据充分时自动采纳。</summary>
        [JsonProperty("confidence")]
        public float confidence;

        /// <summary>支撑本次提案的证据，confidence ≥ 0.8 时必须提供。</summary>
        [JsonProperty("evidence")]
        public EvidenceDto evidence;

        // ── reminder_proposal 专属 ───────────────────────────
        /// <summary>提醒严重程度：gentle / normal / strict。</summary>
        [JsonProperty("severity")]
        public string severity;

        /// <summary>提醒消息正文。</summary>
        [JsonProperty("message")]
        public string message;
    }

    // ──────────────────────────────────────────────────────────
    // 证据 DTO
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// map_update_proposal 中携带的证据信息。
    /// 采纳后会被映射为 <see cref="GamifyHumanAI.Data.Evidence"/>。
    /// </summary>
    public class EvidenceDto
    {
        /// <summary>提供证据的角色 roleId。</summary>
        [JsonProperty("sourceRoleId")]
        public string sourceRoleId;

        /// <summary>原文引用片段（非空时才视为有效证据）。</summary>
        [JsonProperty("quote")]
        public string quote;

        /// <summary>触发完成判定的规则名称。</summary>
        [JsonProperty("rule")]
        public string rule;

        /// <summary>证据本身的置信度 [0, 1]。</summary>
        [JsonProperty("confidence")]
        public float confidence;
    }
}
