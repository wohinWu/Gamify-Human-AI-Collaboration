// ============================================================
// JourneyDebugMenu.cs
// JourneyManager 集成验收测试入口
//
// Start() 完整流程：
//   ①  初始化 JourneyManager（注入自定义目录）
//   ②  订阅 OnJourneyChanged 事件（验证 UI 回调机制）
//   ③  CreateNewJourney
//   ④  AddRole × 2
//   ⑤  AddRoadmapNode × 3
//   ⑥  SetNodeStatus（改变一个节点状态 + 附加证据）
//   ⑦  Save
//   ⑧  Load（重新从磁盘加载，还原为新实例）
//   ⑨  通过 Manager 打印状态摘要与完整 JSON
//   ⑩  模拟 3 条 Agent 事件 JSON → ApplyAgentEvent → 打印变化
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
    /// JourneyManager 的集成验收测试脚本。
    /// 挂载到场景中任意 GameObject，进入 Play Mode 后在 Console 中查看输出。
    /// </summary>
    public class JourneyDebugMenu : MonoBehaviour
    {
        // ── Inspector 参数 ────────────────────────────────────

        [Tooltip("自定义存储目录（留空则使用 persistentDataPath/Journeys）")]
        [SerializeField] private string _customSaveDir = "";

        [Tooltip("勾选后在测试结束时自动删除测试存档文件")]
        [SerializeField] private bool _cleanupAfterTest = false;

        // ── 生命周期 ──────────────────────────────────────────

        private void Start()
        {
            // ① 初始化 JourneyManager（注入存储目录）
            string dir = string.IsNullOrWhiteSpace(_customSaveDir) ? null : _customSaveDir;
            JourneyManager.Initialize(dir);
            var mgr = JourneyManager.Instance;

            // ② 订阅 OnJourneyChanged，验证每次修改都会触发回调
            int changeCount = 0;
            mgr.OnJourneyChanged += state =>
            {
                changeCount++;
                UnityEngine.Debug.Log($"  [OnJourneyChanged #{changeCount}] updatedAt={state.updatedAt}  events={state.eventLog.Count}");
            };

            Log("══════════════════════════════════════════");
            Log("[JourneyDebugMenu] ▶ 开始 JourneyManager 集成验收测试");
            Log("══════════════════════════════════════════");

            // ③ 创建 Journey
            var state = mgr.CreateNewJourney(
                name: "掌握 Unity AI 集成",
                goal: "能够独立在 Unity 2022 中接入并调度多个 AI 角色"
            );
            Log($"[Step 3] Journey 已创建  id={state.journeyId}  status={state.status}");

            // ④ 添加两个角色
            string role1Id = Guid.NewGuid().ToString();
            string role2Id = Guid.NewGuid().ToString();
            long   now     = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            mgr.AddRole(new Role
            {
                roleId        = role1Id,
                name          = "导师 AI",
                avatarKey     = "avatar_tutor",
                windowBinding = new WindowBinding
                {
                    windowId  = "win-chatgpt-01",
                    titleHint = "ChatGPT",
                    urlHint   = "https://chat.openai.com"
                },
                roleState    = RoleState.Idle,
                lastUpdateAt = now
            });
            Log($"[Step 4a] Role 已添加：导师 AI  ({role1Id})");

            mgr.AddRole(new Role
            {
                roleId        = role2Id,
                name          = "代码助手",
                avatarKey     = "avatar_coder",
                windowBinding = new WindowBinding
                {
                    windowId  = "win-copilot-01",
                    titleHint = "GitHub Copilot Chat"
                },
                roleState    = RoleState.Idle,
                lastUpdateAt = now
            });
            Log($"[Step 4b] Role 已添加：代码助手  ({role2Id})");

            // ⑤ 添加三个路线图节点
            mgr.AddRoadmapNode(new RoadmapNode
            {
                nodeId      = "node-env-setup",
                title       = "环境搭建",
                description = "安装 Unity 2022.3 LTS、配置 IDE 与包管理器",
                order       = 0,
                status      = NodeStatus.unlocked
            });
            mgr.AddRoadmapNode(new RoadmapNode
            {
                nodeId      = "node-data-layer",
                title       = "数据层设计",
                description = "实现 Journey 数据模型、Manager 与校验器",
                order       = 1,
                status      = NodeStatus.locked
            });
            mgr.AddRoadmapNode(new RoadmapNode
            {
                nodeId  = "node-ai-integration",
                title   = "AI 角色集成",
                order   = 2,
                status  = NodeStatus.locked
            });
            Log("[Step 5] 三个 RoadmapNode 已添加");

            // ⑥ 改变节点状态：将 node-env-setup 标记为 completed，附加证据
            mgr.SetNodeStatus(
                nodeId:    "node-env-setup",
                newStatus: NodeStatus.completed,
                evidence:  new Evidence
                {
                    sourceRoleId = role1Id,
                    quote        = "SampleScene 已成功打开，Console 无报错。",
                    rule         = "scene_opens_without_error",
                    confidence   = 0.95f
                }
            );
            Log("[Step 6] node-env-setup 状态 → completed（含证据）");

            // 同时模拟角色状态变更
            mgr.UpdateRoleState(role1Id, RoleState.Generating);
            Log($"[Step 6b] 角色 [{role1Id}] 状态 → Generating");

            // ⑦ 保存
            mgr.Save();
            Log($"[Step 7] 已保存 → {mgr.GetSavePath()}");

            // ⑧ 重新加载（从磁盘还原，覆盖内存中的 _active）
            string journeyId = mgr.GetActiveJourney().journeyId;
            mgr.Load(journeyId);
            Log("[Step 8] 已重新从磁盘加载");

            // ⑨ 通过 Manager 打印最终状态
            var loaded = mgr.GetActiveJourney();
            PrintSummary(loaded);

            string fullJson = JsonConvert.SerializeObject(
                loaded, Formatting.Indented, new StringEnumConverter());
            Log($"[Step 9b] 完整 JSON:\n{fullJson}");

            Log($"[Step 9c] OnJourneyChanged 累计触发次数: {changeCount}");

            // ── Step 10：模拟收到 3 条 Agent 事件 JSON ────────
            //
            // 注意：此处使用 Newtonsoft.Json 而非 System.Text.Json，
            // 原因见 AgentEventModels.cs 文件头注释。
            Log("");
            Log("══════════════════════════════════════════");
            Log("[Step 10] ▶ 模拟 Agent 事件处理");
            Log("══════════════════════════════════════════");

            SimulateAgentEvents(mgr, role2Id, ref changeCount);

            // ── 打印 Agent 事件应用后的最终状态 ──────────────
            var final = mgr.GetActiveJourney();
            Log("[Step 10d] Agent 事件处理后节点状态：" +
                string.Join(" | ", final.roadmap.Select(n => $"{n.title}={n.status}")));
            Log("[Step 10e] Agent 事件处理后 eventLog 总条数：" + final.eventLog.Count);
            Log("[Step 10f] 新增事件摘要：");
            foreach (var e in final.eventLog.Skip(final.eventLog.Count - 3))
                Log($"    [{e.eventType}] {e.summary}");

            // ── 可选清理 ──────────────────────────────────────
            if (_cleanupAfterTest)
            {
                new Journey(dir).Delete(journeyId);
                Log("[Cleanup] 测试存档已删除");
            }

            Log("══════════════════════════════════════════");
            Log("[JourneyDebugMenu] ✓ 全部验收测试完成");
            Log("══════════════════════════════════════════");
        }

        // ── 私有工具 ─────────────────────────────────────────

        /// <summary>打印 JourneyState 关键字段摘要。</summary>
        private static void PrintSummary(JourneyState j)
        {
            UnityEngine.Debug.Log(
                "[Step 9a] 最终状态摘要\n" +
                $"  name        : {j.name}\n" +
                $"  status      : {j.status}\n" +
                $"  roles 数量  : {j.roles.Count}\n" +
                $"  roles 状态  : {string.Join(" | ", j.roles.Select(r => $"{r.name}={r.roleState}"))}\n" +
                $"  nodes 状态  : {string.Join(" | ", j.roadmap.Select(n => $"{n.title}={n.status}"))}\n" +
                $"  events 数量 : {j.eventLog.Count}\n" +
                $"  event 摘要  :\n    {string.Join("\n    ", j.eventLog.Select(e => $"[{e.eventType}] {e.summary}"))}\n" +
                $"  updatedAt   : {DateTimeOffset.FromUnixTimeSeconds(j.updatedAt):yyyy-MM-dd HH:mm:ss} UTC"
            );
        }

        private static void Log(string msg) => UnityEngine.Debug.Log(msg);

        // ── Agent 事件模拟 ────────────────────────────────────

        /// <summary>
        /// 模拟从网络收到 3 条 Agent 事件 JSON，
        /// 反序列化为 AgentEventEnvelope 后交由 JourneyManager.ApplyAgentEvent 处理。
        /// </summary>
        private static void SimulateAgentEvents(
            JourneyManager mgr,
            string         role2Id,
            ref int        changeCount)
        {
            // 取当前节点列表，用于在 JSON 中填入真实 nodeId
            var journey = mgr.GetActiveJourney();
            string nodeDataLayerId = journey.roadmap
                .Find(n => n.nodeId == "node-data-layer")?.nodeId ?? "node-data-layer";

            // ── 事件 A：role_state_changed（含 contentSnippet） ──
            // 模拟"代码助手"角色切换到 Generating 并附带当前输出片段
            string eventAJson = $@"{{
  ""type"": ""role_state_changed"",
  ""roleId"": ""{role2Id}"",
  ""roleState"": ""Generating"",
  ""contentSnippet"": ""正在生成单元测试骨架，预计 30 秒完成...""
}}";
            var evtA = JsonConvert.DeserializeObject<AgentEventEnvelope>(eventAJson);
            mgr.ApplyAgentEvent(evtA);
            Log($"[Step 10a] 事件 A 已应用（role_state_changed）" +
                $"  角色状态={mgr.GetActiveJourney().roles.Find(r => r.roleId == role2Id)?.roleState}");

            // ── 事件 B：map_update_proposal（confidence=0.92 → 自动采纳）──
            // 模拟 Agent 提案将"数据层设计"节点标记为 completed
            string role2Name = journey.roles.Find(r => r.roleId == role2Id)?.roleId ?? role2Id;
            string eventBJson = $@"{{
  ""type"": ""map_update_proposal"",
  ""nodeId"": ""{nodeDataLayerId}"",
  ""proposedStatus"": ""completed"",
  ""confidence"": 0.92,
  ""evidence"": {{
    ""sourceRoleId"": ""{role2Id}"",
    ""quote"": ""JourneyManager 所有方法验证通过，eventLog 写入正常。"",
    ""rule"": ""manager_integration_test_pass"",
    ""confidence"": 0.92
  }}
}}";
            var evtB = JsonConvert.DeserializeObject<AgentEventEnvelope>(eventBJson);
            int eventsBefore = mgr.GetActiveJourney().eventLog.Count;
            mgr.ApplyAgentEvent(evtB);
            var nodeAfterB = mgr.GetActiveJourney().roadmap.Find(n => n.nodeId == nodeDataLayerId);
            Log($"[Step 10b] 事件 B 已应用（map_update_proposal，confidence=0.92）" +
                $"  节点={nodeDataLayerId} status={nodeAfterB?.status}" +
                $"  新增 events={mgr.GetActiveJourney().eventLog.Count - eventsBefore}");

            // ── 事件 C：map_update_proposal（confidence=0.6 → pending，不采纳）──
            // 模拟低置信度提案，应只写 eventLog 不改节点
            string eventCLowJson = $@"{{
  ""type"": ""map_update_proposal"",
  ""nodeId"": ""node-ai-integration"",
  ""proposedStatus"": ""in_progress"",
  ""confidence"": 0.6,
  ""evidence"": {{
    ""sourceRoleId"": ""{role2Id}"",
    ""quote"": """",
    ""rule"": ""partial_progress"",
    ""confidence"": 0.6
  }}
}}";
            var evtC = JsonConvert.DeserializeObject<AgentEventEnvelope>(eventCLowJson);
            var nodeAIBefore = mgr.GetActiveJourney().roadmap.Find(n => n.nodeId == "node-ai-integration")?.status;
            mgr.ApplyAgentEvent(evtC);
            var nodeAIAfter = mgr.GetActiveJourney().roadmap.Find(n => n.nodeId == "node-ai-integration")?.status;
            Log($"[Step 10c-1] 事件 C-1 已应用（map_update_proposal，confidence=0.6，应 pending）" +
                $"  节点 node-ai-integration：{nodeAIBefore} → {nodeAIAfter}（应保持不变）");

            // ── 事件 D：reminder_proposal ──────────────────────
            // 模拟 Agent 发出提醒，不改 roadmap
            string eventDJson = @"{
  ""type"": ""reminder_proposal"",
  ""severity"": ""normal"",
  ""message"": ""距离本周目标截止还有 2 天，建议推进第 3 节点 AI 角色集成。""
}";
            var evtD = JsonConvert.DeserializeObject<AgentEventEnvelope>(eventDJson);
            int nodesCountBefore = mgr.GetActiveJourney().roadmap.Count;
            mgr.ApplyAgentEvent(evtD);
            Log($"[Step 10d-1] 事件 D 已应用（reminder_proposal）" +
                $"  roadmap 节点数不变={mgr.GetActiveJourney().roadmap.Count == nodesCountBefore}");
        }
    }
}
