// ============================================================
// JourneyStatusPanel.cs
// 当前 Journey 状态的只读展示面板
//
// 依赖：
//   · TextMeshPro (com.unity.textmeshpro 3.0.7)
//   · GamifyHumanAI.Data.JourneyManager
//
// 数据流：
//   JourneyManager.OnJourneyChanged → RefreshUI() → 写入 4 个 TMP_Text
//
// 订阅策略：
//   在 Start()（而非 OnEnable）中订阅，确保 JourneyManager.Initialize
//   已在其他脚本的 Awake() 中完成。RefreshUI 每次都从最新 Instance 读取。
// ============================================================

using System.Linq;
using System.Text;
using GamifyHumanAI.Data;
using TMPro;
using UnityEngine;

namespace GamifyHumanAI.UI
{
    /// <summary>
    /// 订阅 JourneyManager.OnJourneyChanged，实时把 Journey 状态写入 4 块文本。
    /// 挂载到含有 4 个 TMP_Text 子物体的 Canvas Panel 上。
    /// </summary>
    public class JourneyStatusPanel : MonoBehaviour
    {
        // ── Inspector 绑定 ────────────────────────────────────

        [Header("Text Bindings")]
        [Tooltip("显示 Journey 名称与整体状态")]
        public TMP_Text journeyTitleText;

        [Tooltip("逐行显示每个 Role 名称与当前状态")]
        public TMP_Text rolesText;

        [Tooltip("逐行显示路线图节点（序号 · 标题 [状态]）")]
        public TMP_Text roadmapText;

        [Tooltip("显示最近 3 条 eventLog 记录")]
        public TMP_Text eventsText;

        // ── 常量 ──────────────────────────────────────────────

        private const int RecentEventCount = 3;

        // ── 生命周期 ──────────────────────────────────────────

        private void Start()
        {
            JourneyManager.Instance.OnJourneyChanged += OnJourneyChanged;
            RefreshUI();
        }

        private void OnDestroy()
        {
            if (JourneyManager.Instance != null)
                JourneyManager.Instance.OnJourneyChanged -= OnJourneyChanged;
        }

        // ── 事件回调 ──────────────────────────────────────────

        private void OnJourneyChanged(JourneyState _) => RefreshUI();

        // ── 核心刷新 ──────────────────────────────────────────

        /// <summary>
        /// 从 JourneyManager 读取当前 active state 并写入所有 Text 组件。
        /// active journey 为 null 时显示占位文案，不抛出异常。
        /// </summary>
        public void RefreshUI()
        {
            var state = JourneyManager.Instance.GetActiveJourney();

            if (state == null)
            {
                SetSafe(journeyTitleText, "No Active Journey");
                SetSafe(rolesText,        "(none)");
                SetSafe(roadmapText,      "(none)");
                SetSafe(eventsText,       "(no events)");
                return;
            }

            // ── journeyTitleText ──────────────────────────────
            SetSafe(journeyTitleText,
                $"Journey: {state.name}\nStatus: {state.status}");

            // ── rolesText ─────────────────────────────────────
            if (state.roles == null || state.roles.Count == 0)
            {
                SetSafe(rolesText, "(none)");
            }
            else
            {
                var sb = new StringBuilder();
                foreach (var role in state.roles)
                    sb.AppendLine($"{role.name} - {role.roleState}");
                SetSafe(rolesText, sb.ToString().TrimEnd());
            }

            // ── roadmapText ───────────────────────────────────
            if (state.roadmap == null || state.roadmap.Count == 0)
            {
                SetSafe(roadmapText, "(none)");
            }
            else
            {
                var sb = new StringBuilder();
                foreach (var node in state.roadmap.OrderBy(n => n.order))
                    sb.AppendLine($"{node.order}. {node.title} [{node.status}]");
                SetSafe(roadmapText, sb.ToString().TrimEnd());
            }

            // ── eventsText（最近 N 条）────────────────────────
            if (state.eventLog == null || state.eventLog.Count == 0)
            {
                SetSafe(eventsText, "(no events)");
            }
            else
            {
                var recent = state.eventLog
                    .Skip(Mathf.Max(0, state.eventLog.Count - RecentEventCount))
                    .ToList();

                var sb = new StringBuilder();
                foreach (var evt in recent)
                    sb.AppendLine($"[{evt.eventType}] {evt.summary}");
                SetSafe(eventsText, sb.ToString().TrimEnd());
            }
        }

        // ── 私有工具 ──────────────────────────────────────────

        private static void SetSafe(TMP_Text target, string value)
        {
            if (target == null)
            {
                UnityEngine.Debug.LogWarning(
                    "[JourneyStatusPanel] TMP_Text not assigned in Inspector.");
                return;
            }
            target.text = value;
        }
    }
}
