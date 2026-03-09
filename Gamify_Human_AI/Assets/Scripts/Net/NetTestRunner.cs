using System;
using System.Collections;
using System.Collections.Generic;
using GamifyHumanAI.Data;
using Newtonsoft.Json;
using UnityEngine;

namespace GamifyHumanAI.Net
{
    /// <summary>
    /// 手动启动 Python 服务后，在 Unity 中验证 HTTP 往返 + AgentEvent 应用链路。
    /// </summary>
    public class NetTestRunner : MonoBehaviour
    {
        [SerializeField] private string _baseUrl = "http://127.0.0.1:8000";
        [SerializeField] private string _customSaveDir = "";

        private readonly PythonHttpClient _client = new PythonHttpClient();

        private const int LogMax = 500;

        private IEnumerator Start()
        {
            JourneyManager.Initialize(string.IsNullOrWhiteSpace(_customSaveDir) ? null : _customSaveDir);
            _client.BaseUrl = _baseUrl;

            var manager = JourneyManager.Instance;
            EnsureActiveJourney(manager);

            // 1) HealthCheck
            bool healthOk = false;
            string healthBody = string.Empty;
            LogInfo($"[Health] GET {_client.BaseUrl}/health");
            yield return _client.HealthCheck((ok, body) =>
            {
                healthOk = ok;
                healthBody = body ?? string.Empty;
            });
            LogInfo($"[Health] code={_client.LastResponseCode} {(healthOk ? "SUCCESS" : "FAIL")} body={Short(healthBody)}");

            if (!healthOk)
            {
                LogInfo("请先运行 AgentService/start_agent.bat");
                yield break;
            }

            // 2) 发送 map_update_proposal
            string outboundJson = BuildOutboundEventJson();
            bool postOk = false;
            string responseBody = string.Empty;

            LogInfo($"[POST] URL={_client.BaseUrl}/agent/event");
            LogInfo($"[POST] requestJson={Short(outboundJson)}");
            yield return _client.PostAgentEvent(outboundJson, (ok, body) =>
            {
                postOk = ok;
                responseBody = body ?? string.Empty;
            });
            LogInfo($"[POST] code={_client.LastResponseCode} {(postOk ? "SUCCESS" : "FAIL")} responseBody={Short(responseBody)}");

            if (!postOk)
                yield break;

            // 3) 解析响应并应用事件
            List<AgentEventEnvelope> events;
            if (!TryParseEvents(responseBody, out events))
            {
                LogInfo($"[Parse] 反序列化失败，原始 response={Short(responseBody)}");
                yield break;
            }

            foreach (var evt in events)
            {
                manager.ApplyAgentEvent(evt);
            }

            // 4) 打印最终状态
            PrintFinalState(manager);
        }

        private static void EnsureActiveJourney(JourneyManager manager)
        {
            var state = manager.GetActiveJourney();
            if (state == null)
            {
                state = manager.CreateNewJourney("HTTP 联调测试", "验证 Unity 与 Python 的 AgentEvent 通信");
            }

            if (!state.roadmap.Exists(n => n.nodeId == "n1"))
            {
                manager.AddRoadmapNode(new RoadmapNode
                {
                    nodeId = "n1",
                    title = "网络回包验证节点",
                    description = "收到 map_update_proposal 后应变为 completed",
                    order = state.roadmap.Count,
                    status = NodeStatus.locked,
                });
            }
        }

        private static string BuildOutboundEventJson()
        {
            var outbound = new AgentEventEnvelope
            {
                type = AgentEventTypes.MapUpdateProposal,
                nodeId = "n1",
                proposedStatus = "completed",
                confidence = 0.9f,
                evidence = new EvidenceDto
                {
                    sourceRoleId = "local-test-role",
                    quote = "local test done",
                    rule = "manual_http_roundtrip_ok",
                    confidence = 0.9f,
                }
            };

            return JsonConvert.SerializeObject(outbound, Formatting.None);
        }

        private static bool TryParseEvents(string body, out List<AgentEventEnvelope> events)
        {
            events = new List<AgentEventEnvelope>();
            if (string.IsNullOrWhiteSpace(body))
                return false;

            try
            {
                // 1) 先尝试单事件格式
                var single = JsonConvert.DeserializeObject<AgentEventEnvelope>(body);
                if (single != null && !string.IsNullOrWhiteSpace(single.type))
                {
                    events.Add(single);
                    return true;
                }
            }
            catch
            {
                // ignore and try wrapped format
            }

            try
            {
                // 2) 再尝试 { "events": [ ... ] } 包装格式
                var wrapped = JsonConvert.DeserializeObject<AgentEventBatchResponse>(body);
                if (wrapped != null && wrapped.events != null && wrapped.events.Count > 0)
                {
                    events.AddRange(wrapped.events);
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static void PrintFinalState(JourneyManager manager)
        {
            var state = manager.GetActiveJourney();
            var node = state.roadmap.Find(n => n.nodeId == "n1");
            string nodeStatus = node != null ? node.status.ToString() : "missing";
            int eventCount = state.eventLog != null ? state.eventLog.Count : 0;
            string lastSummary = eventCount > 0 ? state.eventLog[eventCount - 1].summary : "(none)";

            LogInfo($"[Result] node n1 status={nodeStatus}");
            LogInfo($"[Result] eventLog count={eventCount}");
            LogInfo($"[Result] last event summary={Short(lastSummary)}");
        }

        private static string Short(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Length <= LogMax ? s : s.Substring(0, LogMax) + "...(truncated)";
        }

        private static void LogInfo(string msg)
        {
            UnityEngine.Debug.Log("[NetTestRunner] " + msg);
        }

        [Serializable]
        private class AgentEventBatchResponse
        {
            public List<AgentEventEnvelope> events;
        }
    }
}
