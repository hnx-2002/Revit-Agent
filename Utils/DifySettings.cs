using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace AILayoutAgent.Utils
{
    [DataContract]
    internal sealed class DifySettings
    {
        [DataMember(Name = "api_url")]
        internal string ApiUrl { get; set; }

        [DataMember(Name = "api_key")]
        internal string ApiKey { get; set; }

        [DataMember(Name = "user")]
        internal string User { get; set; }

        internal static DifySettings Load()
        {
            var settings = new DifySettings
            {
                ApiUrl = Environment.GetEnvironmentVariable("DIFY_API_URL") ?? string.Empty,
                ApiKey = Environment.GetEnvironmentVariable("DIFY_API_KEY") ?? string.Empty,
                User = Environment.GetEnvironmentVariable("DIFY_USER") ?? Environment.UserName
            };

            if (!string.IsNullOrWhiteSpace(settings.ApiUrl) && !string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                return Normalize(settings);
            }

            // Fallback: load from file next to the plugin dll (recommended for Revit add-ins).
            try
            {
                // var dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                
                var configpath = @"C:\Users\Administrator\Desktop\AILayoutAgent\Config\";
                
                var path = Path.Combine(configpath, "dify.settings.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    var fromFile = Deserialize(json);
                    if (fromFile != null)
                    {
                        if (string.IsNullOrWhiteSpace(settings.ApiUrl)) settings.ApiUrl = fromFile.ApiUrl;
                        if (string.IsNullOrWhiteSpace(settings.ApiKey)) settings.ApiKey = fromFile.ApiKey;
                        if (string.IsNullOrWhiteSpace(settings.User)) settings.User = fromFile.User;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return Normalize(settings);
        }

        private static DifySettings Normalize(DifySettings settings)
        {
            settings ??= new DifySettings();

            var apiUrl = (settings.ApiUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                // Default to the same base url used by the python reference (self-hosted / internal).
                apiUrl = "http://10.2.80.119/v1";
            }

            settings.ApiUrl = apiUrl.TrimEnd('/');
            settings.ApiKey = (settings.ApiKey ?? string.Empty).Trim();
            settings.User = string.IsNullOrWhiteSpace(settings.User) ? "revit-user" : settings.User.Trim();
            return settings;
        }

        private static DifySettings Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(DifySettings));
                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
                return serializer.ReadObject(ms) as DifySettings;
            }
            catch
            {
                return null;
            }
        }
    }
}

