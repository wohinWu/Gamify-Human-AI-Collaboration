// ============================================================
// JourneyManager.cs
// Journey 数据层的统一服务入口
//
// 职责：
//   · 管理当前 active JourneyState（内存中唯一一份）
//   · 提供所有写操作（每次写操作均自动更新 updatedAt 并追加 eventLog）
//   · 通过 OnJourneyChanged 事件通知 UI 层刷新
//   · 持久化委托给 Journey（persistence layer）
//
// 设计选择：
//   纯 C# 类 + 懒加载静态 Instance，不继承 MonoBehaviour。
//   如需在 Inspector 配置存储路径，可由 MonoBehaviour 持有者调用
//   JourneyManager.Initialize(customSaveDir) 完成初始化。
// ============================================================

using System;
using System.Collections.Generic;
using GamifyHumanAI.Net;

namespace GamifyHumanAI.Data
{
    /// <summary>
    /// Journey 数据层的统一服务入口（纯 C# 单例）。
    /// 所有写操作都经由此类，保证 updatedAt 与 eventLog 始终一致。
    /// </summary>
    public class JourneyManager
    {
        // ── 静态入口 ──────────────────────────────────────────

        private static JourneyManager _instance;

        /// <summary>
        /// 懒加载单例。首次访问时使用默认存储目录创建实例。
        /// 若需自定义目录，请在首次访问前调用 <see cref="Initialize"/>。
        /// </summary>
        public static JourneyManager Instance => _instance ??= new JourneyManager();

        /// <summary>
        /// 使用自定义存储目录（或测试用 mock）初始化单例。
        /// 必须在首次访问 Instance 之前调用，否则无效。
        /// </summary>
        public static void Initialize(string saveDir = null)
        {
            _instance = new JourneyManager(new Journey(saveDir));
        }

        // ── 内部状态 ──────────────────────────────────────────

        private JourneyState _active;
        private readonly Journey _repo;

        // ── 事件 ──────────────────────────────────────────────

        /// <summary>
        /// 每当 active JourneyState 发生任何修改（或切换）时触发。
        /// UI 层订阅此事件以响应刷新。
        /// </summary>
        public event Action<JourneyState> OnJourneyChanged;

        // ── 构造 ──────────────────────────────────────────────

        private JourneyManager() : this(new Journey()) { }

        private JourneyManager(Journey repo)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        }

        // ── Journey 生命周期 ──────────────────────────────────

        /// <summary>
        /// 创建一个全新的 Journey，设为 active 并返回。
        /// 初始状态为 Draft，自动写入第一条 user_action 事件。
        /// </summary>
        public JourneyState CreateNewJourney(string name, string goal)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name 不可为空");
            if (string.IsNullOrWhiteSpace(goal)) throw new ArgumentException("goal 不可为空");

            long now = Now();
            _active = new JourneyState
            {
                journeyId = Guid.NewGuid().ToString(),
                name      = name,
                goal      = goal,
                status    = JourneyStatus.Draft,
                createdAt = now,
                updatedAt = now,
            };

            AppendEvent(EventType.user_action, $"Journey 已创建：{name}");
            NotifyChanged();
            return _active;
        }

        /// <summary>将外部（通常是 Load 后）的 JourneyState 设为 active。</summary>
        public void SetActiveJourney(JourneyState state)
        {
            _active = state ?? throw new ArgumentNullException(nameof(state));
            NotifyChanged();
        }

        /// <summary>返回当前 active JourneyState；若尚未设置则返回 null。</summary>
        public JourneyState GetActiveJourney() => _active;

        // ── Role 操作 ─────────────────────────────────────────

        /// <summary>
        /// 向 active Journey 添加一个角色。
        /// roleId 重复时抛出 <see cref="InvalidOperationException"/>。
        /// </summary>
        public void AddRole(Role role)
        {
            AssertActive();
            if (role == null) throw new ArgumentNullException(nameof(role));
            if (_active.roles.Exists(r => r.roleId == role.roleId))
                throw new InvalidOperationException($"roleId 已存在: {role.roleId}");

            _active.roles.Add(role);
            Touch();
            AppendEvent(EventType.user_action,
                $"角色已添加：{role.name}",
                sourceRoleId: role.roleId);
            NotifyChanged();
        }

        /// <summary>
        /// 更新指定角色的 roleState。
        /// 自动记录 role_state_changed 事件。
        /// </summary>
        /// <param name="lastUpdateAt">角色状态变更时间（Unix 秒）；留 null 则取当前时间。</param>
        public void UpdateRoleState(string roleId, RoleState newState, long? lastUpdateAt = null)
        {
            AssertActive();
            var role = FindRole(roleId);
            var prev = role.roleState;

            role.roleState    = newState;
            role.lastUpdateAt = lastUpdateAt ?? Now();
            Touch();
            AppendEvent(EventType.role_state_changed,
                $"角色 [{role.name}] 状态变更：{prev} → {newState}",
                sourceRoleId: roleId);
            NotifyChanged();
        }

        // ── RoadmapNode 操作 ──────────────────────────────────

        /// <summary>
        /// 向 active Journey 添加一个路线图节点。
        /// nodeId 重复时抛出 <see cref="InvalidOperationException"/>。
        /// </summary>
        public void AddRoadmapNode(RoadmapNode node)
        {
            AssertActive();
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (_active.roadmap.Exists(n => n.nodeId == node.nodeId))
                throw new InvalidOperationException($"nodeId 已存在: {node.nodeId}");

            _active.roadmap.Add(node);
            Touch();
            AppendEvent(EventType.map_update_applied,
                $"节点已添加：{node.title}",
                relatedNodeId: node.nodeId);
            NotifyChanged();
        }

        /// <summary>
        /// 修改指定节点的状态，可选附加完成证据。
        /// 自动记录 map_update_applied 事件。
        /// </summary>
        public void SetNodeStatus(string nodeId, NodeStatus newStatus, Evidence evidence = null)
        {
            AssertActive();
            var node = FindNode(nodeId);
            var prev = node.status;

            node.status   = newStatus;
            if (evidence != null) node.evidence = evidence;
            Touch();
            AppendEvent(EventType.map_update_applied,
                $"节点 [{node.title}] 状态变更：{prev} → {newStatus}",
                relatedNodeId: nodeId);
            NotifyChanged();
        }

        // ── Journey 状态 ──────────────────────────────────────

        /// <summary>
        /// 更新 Journey 整体状态（Draft / Active / Paused / Finished）。
        /// </summary>
        public void SetJourneyStatus(JourneyStatus newStatus)
        {
            AssertActive();
            var prev = _active.status;
            _active.status = newStatus;
            Touch();
            AppendEvent(EventType.user_action,
                $"Journey 状态变更：{prev} → {newStatus}");
            NotifyChanged();
        }

        // ── Agent 事件处理 ────────────────────────────────────

        /// <summary>
        /// 将从 Agent 侧收到的事件应用到 active Journey。
        /// 支持三种类型：role_state_changed / map_update_proposal / reminder_proposal。
        /// 每次调用都会更新 updatedAt、追加 eventLog、触发 OnJourneyChanged。
        /// </summary>
        /// <exception cref="InvalidOperationException">当前无 active Journey 时抛出。</exception>
        public void ApplyAgentEvent(AgentEventEnvelope evt)
        {
            AssertActive();
            if (evt == null) throw new ArgumentNullException(nameof(evt));

            switch (evt.type)
            {
                case AgentEventTypes.RoleStateChanged:
                    ApplyRoleStateChanged(evt);
                    break;

                case AgentEventTypes.MapUpdateProposal:
                    ApplyMapUpdateProposal(evt);
                    break;

                case AgentEventTypes.ReminderProposal:
                    ApplyReminderProposal(evt);
                    break;

                default:
                    // 未知类型：只记录，不修改业务数据
                    Touch();
                    AppendEvent(EventType.user_action,
                        $"收到未知 Agent 事件类型：{evt.type}（已忽略）");
                    NotifyChanged();
                    break;
            }
        }

        // ── Agent 事件私有处理器 ──────────────────────────────

        /// <summary>
        /// 处理 role_state_changed：
        /// 将 roleState 字符串解析为枚举后调用 UpdateRoleState。
        /// 若携带 contentSnippet，则将其追加到事件摘要（不写入角色核心字段）。
        /// </summary>
        private void ApplyRoleStateChanged(AgentEventEnvelope evt)
        {
            if (string.IsNullOrWhiteSpace(evt.roleId))
            {
                AppendEventOnly(EventType.role_state_changed,
                    "role_state_changed 事件缺少 roleId，已忽略");
                return;
            }

            if (!Enum.TryParse<RoleState>(evt.roleState, ignoreCase: true, out var newState))
            {
                AppendEventOnly(EventType.role_state_changed,
                    $"role_state_changed 事件 roleState 无法解析：\"{evt.roleState}\"，已忽略");
                return;
            }

            // 调用标准写操作（内部会 Touch + AppendEvent + NotifyChanged）
            UpdateRoleState(evt.roleId, newState);

            // 若携带 snippet，将其补写到最后一条 eventLog 的 summary 中
            if (!string.IsNullOrWhiteSpace(evt.contentSnippet))
            {
                var last = _active.eventLog[_active.eventLog.Count - 1];
                last.summary += $"  |  snippet: {evt.contentSnippet}";
            }
        }

        /// <summary>
        /// 处理 map_update_proposal：
        /// · confidence ≥ 0.8 且 evidence.quote 非空 → 自动采纳，调用 SetNodeStatus。
        /// · 否则 → 不改节点，仅写一条 "proposal pending" eventLog。
        /// </summary>
        private void ApplyMapUpdateProposal(AgentEventEnvelope evt)
        {
            if (string.IsNullOrWhiteSpace(evt.nodeId))
            {
                AppendEventOnly(EventType.map_update_applied,
                    "map_update_proposal 事件缺少 nodeId，已忽略");
                return;
            }

            if (!Enum.TryParse<NodeStatus>(evt.proposedStatus, ignoreCase: true, out var proposedStatus))
            {
                AppendEventOnly(EventType.map_update_applied,
                    $"map_update_proposal proposedStatus 无法解析：\"{evt.proposedStatus}\"，已忽略",
                    relatedNodeId: evt.nodeId);
                return;
            }

            bool hasEvidence = evt.evidence != null
                               && !string.IsNullOrWhiteSpace(evt.evidence.quote);
            bool autoAdopt   = evt.confidence >= 0.8f && hasEvidence;

            if (autoAdopt)
            {
                // 将 EvidenceDto 映射为领域对象 Evidence
                var evidence = new Evidence
                {
                    sourceRoleId = evt.evidence.sourceRoleId,
                    quote        = evt.evidence.quote,
                    rule         = evt.evidence.rule,
                    confidence   = evt.evidence.confidence,
                };
                SetNodeStatus(evt.nodeId, proposedStatus, evidence);
            }
            else
            {
                // 置信度不足或无有效证据：挂起提案，仅记录 eventLog
                string reason = !hasEvidence
                    ? "证据为空"
                    : $"置信度 {evt.confidence:F2} < 0.8";

                Touch();
                AppendEvent(EventType.map_update_applied,
                    $"节点 [{evt.nodeId}] 提案 pending（{reason}）：proposedStatus={proposedStatus}",
                    relatedNodeId: evt.nodeId);
                NotifyChanged();
            }
        }

        /// <summary>
        /// 处理 reminder_proposal：
        /// 不修改 roadmap，仅向 eventLog 写入 reminder 类型条目（含 severity）。
        /// </summary>
        private void ApplyReminderProposal(AgentEventEnvelope evt)
        {
            string severity = string.IsNullOrWhiteSpace(evt.severity) ? "normal" : evt.severity;
            string message  = string.IsNullOrWhiteSpace(evt.message)  ? "（无消息体）" : evt.message;

            Touch();
            AppendEvent(EventType.reminder,
                $"[{severity.ToUpper()}] {message}");
            NotifyChanged();
        }

        /// <summary>仅追加 eventLog 条目，不触发 Touch/NotifyChanged（用于错误记录）。</summary>
        private void AppendEventOnly(
            EventType type,
            string    summary,
            string    relatedNodeId = null,
            string    sourceRoleId  = null)
        {
            AppendEvent(type, summary, relatedNodeId, sourceRoleId);
        }

        // ── 持久化 ────────────────────────────────────────────

        /// <summary>将 active Journey 保存到磁盘（crash-safe）。</summary>
        public void Save()
        {
            AssertActive();
            _repo.Save(_active);
        }

        /// <summary>
        /// 从磁盘加载指定 Journey 并设为 active。
        /// 加载成功后触发 OnJourneyChanged。
        /// </summary>
        public void Load(string journeyId)
        {
            _active = _repo.Load(journeyId);
            NotifyChanged();
        }

        /// <summary>返回 active Journey 的磁盘路径（供 Debug 输出使用）。</summary>
        public string GetSavePath() => _active != null ? _repo.GetSavePath(_active.journeyId) : string.Empty;

        // ── 私有工具 ──────────────────────────────────────────

        /// <summary>更新 updatedAt 为当前 Unix 秒。</summary>
        private void Touch() => _active.updatedAt = Now();

        /// <summary>获取当前 Unix 秒时间戳。</summary>
        private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>向 eventLog 追加一条事件记录。</summary>
        private void AppendEvent(
            EventType type,
            string    summary,
            string    relatedNodeId = null,
            string    sourceRoleId  = null)
        {
            _active.eventLog.Add(new EventRecord
            {
                eventId       = Guid.NewGuid().ToString(),
                eventType     = type,
                timestamp     = Now(),
                summary       = summary,
                relatedNodeId = relatedNodeId,
                sourceRoleId  = sourceRoleId,
            });
        }

        /// <summary>触发 OnJourneyChanged 事件。</summary>
        private void NotifyChanged() => OnJourneyChanged?.Invoke(_active);

        /// <summary>断言当前存在 active Journey，否则抛出异常。</summary>
        private void AssertActive()
        {
            if (_active == null)
                throw new InvalidOperationException(
                    "当前没有 active Journey，请先调用 CreateNewJourney 或 Load。");
        }

        /// <summary>按 roleId 查找角色，找不到则抛出异常。</summary>
        private Role FindRole(string roleId)
        {
            var r = _active.roles.Find(x => x.roleId == roleId);
            if (r == null) throw new KeyNotFoundException($"roleId 不存在: {roleId}");
            return r;
        }

        /// <summary>按 nodeId 查找节点，找不到则抛出异常。</summary>
        private RoadmapNode FindNode(string nodeId)
        {
            var n = _active.roadmap.Find(x => x.nodeId == nodeId);
            if (n == null) throw new KeyNotFoundException($"nodeId 不存在: {nodeId}");
            return n;
        }
    }
}
