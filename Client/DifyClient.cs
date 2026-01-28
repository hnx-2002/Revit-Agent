using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AILayoutAgent.Utils;

namespace AILayoutAgent.Client
{
    internal static class DifyClient
    {
        [DataContract]
        private sealed class ChatMessagesRequest
        {
            [DataMember(Name = "inputs")]
            public System.Collections.Generic.Dictionary<string, object> Inputs { get; set; }

            [DataMember(Name = "query")]
            public string Query { get; set; }

            [DataMember(Name = "response_mode")]
            public string ResponseMode { get; set; }

            [DataMember(Name = "conversation_id")]
            public string ConversationId { get; set; }

            [DataMember(Name = "user")]
            public string User { get; set; }
        }

        [DataContract]
        private sealed class ChatMessagesStreamEvent
        {
            [DataMember(Name = "event")]
            public string Event { get; set; }

            [DataMember(Name = "answer")]
            public string Answer { get; set; }

            [DataMember(Name = "message")]
            public string Message { get; set; }
        }

        internal static async Task<string> SendChatMessageStreamingAsync(
            string query,
            Action<string> onDelta,
            CancellationToken cancellationToken)
        {
            var settings = DifySettings.Load();
            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                return "未配置 Dify API Key。\n请设置环境变量 DIFY_API_KEY（可选：DIFY_API_URL），或在插件 DLL 同目录放置 dify.settings.json。";
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return "请求内容为空，已取消。";
            }

            // Ensure TLS 1.2 for https endpoints (cloud.dify.ai / api.dify.ai).
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
                // ignore
            }

            var url = settings.ApiUrl.TrimEnd('/') + "/chat-messages";
            var request = new ChatMessagesRequest
            {
                Inputs = new System.Collections.Generic.Dictionary<string, object>(),
                Query = query,
                ResponseMode = "streaming",
                ConversationId = string.Empty,
                User = settings.User
            };

            var payload = Serialize(request);

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
            using var cancelReg = cancellationToken.Register(() =>
            {
                try { client.CancelPendingRequests(); } catch { /* ignore */ }
            });

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var resp = await client.PostAsync(url, content, cancellationToken).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return $"Dify 请求失败：HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}";
            }

            var full = new StringBuilder();
            var lastAnswerSoFar = string.Empty;

            using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var data = line.Substring("data:".Length).Trim();
                if (string.IsNullOrWhiteSpace(data))
                {
                    continue;
                }

                if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var evt = Deserialize<ChatMessagesStreamEvent>(data);
                if (evt == null)
                {
                    continue;
                }

                if (string.Equals(evt.Event, "error", StringComparison.OrdinalIgnoreCase))
                {
                    var msg = string.IsNullOrWhiteSpace(evt.Message) ? "Dify 返回 error 事件。" : evt.Message;
                    return $"Dify 流式返回错误：{msg}";
                }

                if (string.IsNullOrEmpty(evt.Answer))
                {
                    continue;
                }

                string delta;
                if (evt.Answer.Length >= lastAnswerSoFar.Length && evt.Answer.StartsWith(lastAnswerSoFar))
                {
                    // Some deployments return cumulative answer each time.
                    delta = evt.Answer.Substring(lastAnswerSoFar.Length);
                    lastAnswerSoFar = evt.Answer;
                }
                else
                {
                    // Some deployments return incremental deltas.
                    delta = evt.Answer;
                    lastAnswerSoFar += delta;
                }

                if (delta.Length == 0)
                {
                    continue;
                }

                full.Append(delta);
                try { onDelta?.Invoke(delta); } catch { /* ignore UI callback issues */ }
            }

            return full.Length == 0 ? "Dify 返回为空。" : full.ToString();
        }

        private static string Serialize<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using var ms = new MemoryStream();
            serializer.WriteObject(ms, value);
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static T Deserialize<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
                return serializer.ReadObject(ms) as T;
            }
            catch
            {
                return null;
            }
        }
    }
}
