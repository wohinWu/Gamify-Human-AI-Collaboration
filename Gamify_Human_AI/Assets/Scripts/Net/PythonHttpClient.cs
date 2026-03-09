using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace GamifyHumanAI.Net
{
    /// <summary>
    /// Python HTTP 通信客户端（UnityWebRequest 版本）。
    /// 说明：当前项目已稳定使用 Newtonsoft.Json，System.Text.Json 在 Unity 2022.3
    /// 默认环境不总是可用，故本文件仅负责网络收发，JSON 解析交由调用方处理。
    /// </summary>
    [Serializable]
    public class PythonHttpClient
    {
        public string BaseUrl = "http://127.0.0.1:8000";
        public long LastResponseCode { get; private set; }
        public string LastRequestUrl { get; private set; }
        public string LastError { get; private set; }

        private const int TimeoutSeconds = 5;

        public IEnumerator HealthCheck(Action<bool, string> cb)
        {
            string url = BuildUrl("/health");
            LastRequestUrl = url;
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = TimeoutSeconds;
                yield return req.SendWebRequest();

                string body = req.downloadHandler != null ? req.downloadHandler.text : string.Empty;
                LastResponseCode = req.responseCode;
                LastError = req.error;
                bool ok = req.result == UnityWebRequest.Result.Success &&
                          req.responseCode >= 200 && req.responseCode < 300;

                if (ok)
                {
                    cb?.Invoke(true, body);
                }
                else
                {
                    string msg = $"HTTP {(int)req.responseCode}; error={req.error}; body={body}";
                    cb?.Invoke(false, msg);
                }
            }
        }

        public IEnumerator PostAgentEvent(string eventJson, Action<bool, string> cb)
        {
            string url = BuildUrl("/agent/event");
            LastRequestUrl = url;
            byte[] payload = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(eventJson) ? "{}" : eventJson);

            using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(payload);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = TimeoutSeconds;
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept", "application/json");

                yield return req.SendWebRequest();

                string body = req.downloadHandler != null ? req.downloadHandler.text : string.Empty;
                LastResponseCode = req.responseCode;
                LastError = req.error;
                bool ok = req.result == UnityWebRequest.Result.Success &&
                          req.responseCode >= 200 && req.responseCode < 300;

                if (ok)
                {
                    cb?.Invoke(true, body);
                }
                else
                {
                    string msg = $"HTTP {(int)req.responseCode}; error={req.error}; body={body}";
                    cb?.Invoke(false, msg);
                }
            }
        }

        private string BuildUrl(string path)
        {
            string baseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? "http://127.0.0.1:8000" : BaseUrl.TrimEnd('/');
            return $"{baseUrl}{path}";
        }
    }
}
