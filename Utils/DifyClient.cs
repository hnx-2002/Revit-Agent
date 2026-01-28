using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace AILayoutAgent.Utils
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
        private sealed class ChatMessagesResponse
        {
            [DataMember(Name = "answer")]
            public string Answer { get; set; }
        }

        internal static string SendChatMessageBlocking(string query)
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
            // var url = settings.ApiUrl;
            var request = new ChatMessagesRequest
            {
                Inputs = new System.Collections.Generic.Dictionary<string, object>(),
                Query = query,
                ResponseMode = "blocking",
                ConversationId = string.Empty,
                User = settings.User
            };

            var payload = Serialize(request);

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(90)
            };
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = client.PostAsync(url, content).GetAwaiter().GetResult();
            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!resp.IsSuccessStatusCode)
            {
                return $"Dify 请求失败：HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}";
            }

            var parsed = Deserialize<ChatMessagesResponse>(body);
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.Answer))
            {
                return string.IsNullOrWhiteSpace(body) ? "Dify 返回为空。" : body;
            }

            return parsed.Answer;
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

