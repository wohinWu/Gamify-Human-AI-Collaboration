// ============================================================
// JourneyTestRunner.cs
// 全流程集成测试入口（替代旧 JourneyDebugMenu）
//
// Awake()：初始化 JourneyManager（保证在所有 Start 之前完成）
// Start()：按节拍执行全部测试步骤，每步修改都会通过
//          OnJourneyChanged 驱动 JourneyStatusPanel 实时刷新。
//
// 测试流程：
//   Phase 1 — 数据层基础
//     ① CreateNewJourney
//     ② AddRole × 2
//     ③ AddRoadmapNode × 3
//     ④ SetNodeStatus（节点完成 + 证据）
//     ⑤ UpdateRoleState
//     ⑥ Save → Load 往返验证
//
//   Phase 2 — Agent 事件模拟
//     ⑦ role_state_changed（含 contentSnippet）
//     ⑧ map_update_proposal（confidence=0.92 → 自动采纳）
//     ⑨ map_update_proposal（confidence=0.6 → pending 不采纳）
//     ⑩ reminder_proposal
//
//   Phase 3 — 最终状态摘要
// ============================================================

using System;
using System.Linq;
using GamifyHumanAI.Data;
using GamifyHumanAI.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace GamifyHumanAI.Debug
{
    /// <summary>
    /// 挂载到场景中任意 GameObject，进入 Play Mode 即运行全流程测试。
    /// JourneyStatusPanel 挂在同一场景的 Canvas 上即可实时看到每步变化。
    /// </summary>
    public class JourneyTestRunner : MonoBehaviour
    {
        [Tooltip("自定义存储目录（留空则使用 persistentDataPath/Journeys）")]
        [SerializeField] private string _customSaveDir = "";

        [Tooltip("勾选后在测试结束时删除存档文件")]
        [SerializeField] private bool _cleanupAfterTest = false;

        private string _role1Id;
        private string _role2Id;

        // ── Awake：比所有 Start 更早，保证单例初始化一次 ──────

        private void Awake()
        {
            string dir = string.IsNullOrWhiteSpace(_customSaveDir) ? null : _customSaveDir;
            JourneyManager.Initialize(dir);
        }

        // ── Start：执行全部测试步骤 ──────────────────────────

        private void Start()
        {
            var mgr = JourneyManager.Instance;

            Log("════════════════════════════════════════════════");
            Log("[JourneyTestRunner] ▶ Full integration test started");
            Log("════════════════════════════════════════════════");

            Phase1_DataLayer(mgr);
            Phase2_AgentEvents(mgr);
            Phase3_Summary(mgr);

            if (_cleanupAfterTest)
            {
                string dir = string.IsNullOrWhiteSpace(_customSaveDir) ? null : _customSaveDir;
                string journeyId = mgr.GetActiveJourney()?.journeyId;
                if (!string.IsNullOrEmpty(journeyId))
                {
                    new Journey(dir).Delete(journeyId);
                    Log("[Cleanup] Test save deleted");
                }
            }

            Log("════════════════════════════════════════════════");
            Log("[JourneyTestRunner] ✓ Full integration test completed");
            Log("════════════════════════════════════════════════");
        }

        // ── Phase 1：数据层基础 ──────────────────────────────

        private void Phase1_DataLayer(JourneyManager mgr)
        {
            Log("── Phase 1: Data Layer ──────────────────────");

            // ① 创建 Journey
            var state = mgr.CreateNewJourney(
                name: "Master Unity AI Integration",
                goal: "Integrate and orchestrate multiple AI roles in Unity 2022"
            );
            Log($"[1] Journey created  id={state.journeyId}  status={state.status}");

            // ② 添加两个角色
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _role1Id = Guid.NewGuid().ToString();
            _role2Id = Guid.NewGuid().ToString();

            mgr.AddRole(new Role
            {
                roleId        = _role1Id,
                name          = "Tutor AI",
                avatarKey     = "avatar_tutor",
                windowBinding = new WindowBinding
                {
                    windowId  = "win-chatgpt-01",
                    titleHint = "ChatGPT",
                    urlHint   = "https://chat.openai.com"
                },
                roleState    = RoleState.Idle,
                lastUpdateAt = now,
            });
            mgr.AddRole(new Role
            {
                roleId        = _role2Id,
                name          = "Code Assistant",
                avatarKey     = "avatar_coder",
                windowBinding = new WindowBinding
                {
                    windowId  = "win-copilot-01",
                    titleHint = "GitHub Copilot Chat",
                },
                roleState    = RoleState.Idle,
                lastUpdateAt = now,
            });
            Log("[2] 2 Roles added");

            // ③ 添加三个节点
            mgr.AddRoadmapNode(new RoadmapNode
            {
                nodeId      = "node-env-setup",
                title       = "Environment Setup",
                description = "Install Unity 2022.3 LTS, configure IDE and package manager",
                order       = 0,
                status      = NodeStatus.unlocked,
            });
            mgr.AddRoadmapNode(new RoadmapNode
            {
                nodeId      = "node-data-layer",
                title       = "Data Layer Design",
                description = "Implement Journey models, Manager and validator",
                order       = 1,
                status      = NodeStatus.locked,
            });
            mgr.AddRoadmapNode(new RoadmapNode
            {
                nodeId = "node-ai-integration",
                title  = "AI Role Integration",
                order  = 2,
                status = NodeStatus.locked,
            });
            Log("[3] 3 RoadmapNodes added");

            // ④ 完成第一个节点
            mgr.SetNodeStatus("node-env-setup", NodeStatus.completed, new Evidence
            {
                sourceRoleId = _role1Id,
                quote        = "SampleScene opened successfully, Console has no errors.",
                rule         = "scene_opens_without_error",
                confidence   = 0.95f,
            });
            Log("[4] node-env-setup → completed");

            // ⑤ 角色状态变更
            mgr.UpdateRoleState(_role1Id, RoleState.Generating);
            Log("[5] Tutor AI → Generating");

            // ⑥ Save → Load 往返
            mgr.Save();
            string savedPath = mgr.GetSavePath();
            string journeyId = mgr.GetActiveJourney().journeyId;
            mgr.Load(journeyId);
            Log($"[6] Save → Load roundtrip done  path={savedPath}");
        }

        // ── Phase 2：Agent 事件模拟 ──────────────────────────

        private void Phase2_AgentEvents(JourneyManager mgr)
        {
            Log("── Phase 2: Agent Events ─────────────────");

            // ⑦ role_state_changed（含 contentSnippet）
            ApplyJson(mgr, $@"{{
                ""type"": ""role_state_changed"",
                ""roleId"": ""{_role2Id}"",
                ""roleState"": ""Generating"",
                ""contentSnippet"": ""Generating unit test skeleton...""
            }}");
            var r2 = mgr.GetActiveJourney().roles.Find(r => r.roleId == _role2Id);
            Log($"[7] role_state_changed → Code Assistant status={r2?.roleState}");

            // ⑧ map_update_proposal（confidence=0.92 → 自动采纳）
            ApplyJson(mgr, $@"{{
                ""type"": ""map_update_proposal"",
                ""nodeId"": ""node-data-layer"",
                ""proposedStatus"": ""completed"",
                ""confidence"": 0.92,
                ""evidence"": {{
                    ""sourceRoleId"": ""{_role2Id}"",
                    ""quote"": ""All JourneyManager methods verified."",
                    ""rule"": ""manager_integration_test_pass"",
                    ""confidence"": 0.92
                }}
            }}");
            var nodeB = mgr.GetActiveJourney().roadmap.Find(n => n.nodeId == "node-data-layer");
            Log($"[8] map_update_proposal(0.92) → node-data-layer={nodeB?.status} (expected completed)");

            // ⑨ map_update_proposal（confidence=0.6 → pending）
            var nodeBefore = mgr.GetActiveJourney().roadmap.Find(n => n.nodeId == "node-ai-integration")?.status;
            ApplyJson(mgr, $@"{{
                ""type"": ""map_update_proposal"",
                ""nodeId"": ""node-ai-integration"",
                ""proposedStatus"": ""in_progress"",
                ""confidence"": 0.6,
                ""evidence"": {{
                    ""sourceRoleId"": ""{_role2Id}"",
                    ""quote"": """",
                    ""rule"": ""partial_progress"",
                    ""confidence"": 0.6
                }}
            }}");
            var nodeAfter = mgr.GetActiveJourney().roadmap.Find(n => n.nodeId == "node-ai-integration")?.status;
            Log($"[9] map_update_proposal(0.6) → node-ai-integration: {nodeBefore}→{nodeAfter} (expected unchanged)");

            // ⑩ reminder_proposal
            ApplyJson(mgr, @"{
                ""type"": ""reminder_proposal"",
                ""severity"": ""normal"",
                ""message"": ""2 days until deadline. Consider advancing AI integration.""
            }");
            Log("[10] reminder_proposal written to eventLog");
        }

        // ── Phase 3：最终状态摘要 ────────────────────────────

        private void Phase3_Summary(JourneyManager mgr)
        {
            var j = mgr.GetActiveJourney();
            Log("── Phase 3: Final Summary ──────────────────");
            Log($"  Journey   : {j.name} [{j.status}]");
            Log($"  Roles     : {string.Join(" | ", j.roles.Select(r => $"{r.name}={r.roleState}"))}");
            Log($"  Roadmap   : {string.Join(" | ", j.roadmap.Select(n => $"{n.title}={n.status}"))}");
            Log($"  EventLog  : {j.eventLog.Count} entries");

            int showCount = Mathf.Min(5, j.eventLog.Count);
            Log($"  Last {showCount} events:");
            foreach (var e in j.eventLog.Skip(j.eventLog.Count - showCount))
                Log($"    [{e.eventType}] {e.summary}");

            string fullJson = JsonConvert.SerializeObject(j, Formatting.Indented, new StringEnumConverter());
            Log($"  Full JSON:\n{fullJson}");
        }

        // ── 私有工具 ──────────────────────────────────────────

        private static void ApplyJson(JourneyManager mgr, string json)
        {
            var evt = JsonConvert.DeserializeObject<AgentEventEnvelope>(json);
            mgr.ApplyAgentEvent(evt);
        }

        private static void Log(string msg) => UnityEngine.Debug.Log(msg);
    }
}
