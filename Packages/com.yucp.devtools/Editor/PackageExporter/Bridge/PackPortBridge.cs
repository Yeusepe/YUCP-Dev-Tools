using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter.Bridge
{
    /// <summary>
    /// Bridge for PackPort to interact with Unity in batchmode.
    /// Usage: Unity.exe -batchmode -quit -projectPath ... -executeMethod YUCP.DevTools.Editor.PackageExporter.Bridge.PackPortBridge.Run -packportArgs "base64_json"
    /// </summary>
    public static class PackPortBridge
    {
        [Serializable]
        public class BridgeCommand
        {
            public string command;
            public string payload; // JSON string
        }

        [Serializable]
        public class ScanPayload
        {
            public string projectPath;
        }

        [Serializable]
        public class ProfilePayload
        {
            public string guid;
            public string json; // For set_profile
        }

        public static void Run()
        {
            try
            {
                string[] args = System.Environment.GetCommandLineArgs();
                string jsonArgs = null;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "-packportArgs" && i + 1 < args.Length)
                    {
                        jsonArgs = args[i + 1];
                        break;
                    }
                }

                if (string.IsNullOrEmpty(jsonArgs))
                {
                    Console.WriteLine("[PackPort-Error] No arguments provided via -packportArgs");
                    return;
                }

                // Decode Base64
                byte[] bytes = Convert.FromBase64String(jsonArgs);
                string json = System.Text.Encoding.UTF8.GetString(bytes);
                
                BridgeCommand cmd = JsonUtility.FromJson<BridgeCommand>(json);

                if (cmd == null)
                {
                    Console.WriteLine("[PackPort-Error] Failed to parse command JSON");
                    return;
                }

                LogJson("ack", "Command received: " + cmd.command);

                switch (cmd.command)
                {
                    case "scan_profiles":
                        ScanProfiles();
                        break;
                    // ... (Other commands like reflect_schema, get_profile, etc.)
                    default:
                        Console.WriteLine($"[PackPort-Error] Unknown command: {cmd.command}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PackPort-Error] Exception: {ex}");
            }
        }

        private static void LogJson(string type, object data)
        {
            string json = JsonUtility.ToJson(data);
            if (data is string str) json = str;
            else json = EditorJsonUtility.ToJson(data);

            Console.WriteLine($"[PackPort-JSON] {{\"type\": \"{type}\", \"data\": {json}}}");
        }

        private static void ScanProfiles()
        {
            // Assuming ExportProfile is the type name
            string[] guids = AssetDatabase.FindAssets("t:ExportProfile");
            var list = new List<object>();

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (obj != null)
                {
                    SerializedObject so = new SerializedObject(obj);
                    // Adjust property names based on YUCP-Components code if needed
                    string name = so.FindProperty("profileName")?.stringValue ?? obj.name; 
                    string version = so.FindProperty("version")?.stringValue ?? "0.0.0";
                    string pkgId = so.FindProperty("packageId")?.stringValue ?? "";

                    list.Add(new
                    {
                        guid = guid,
                        path = path,
                        name = name,
                        version = version,
                        packageId = pkgId
                    });
                }
            }
            
            string jsonList = "[";
            for(int i=0; i<list.Count; i++)
            {
                dynamic item = list[i];
                jsonList += $"{{\"guid\":\"{item.guid}\",\"path\":\"{item.path.Replace("\\", "\\\\")}\",\"name\":\"{item.name}\",\"version\":\"{item.version}\",\"packageId\":\"{item.packageId}\"}}";
                if(i < list.Count -1) jsonList += ",";
            }
            jsonList += "]";

            Console.WriteLine($"[PackPort-JSON] {{\"type\": \"scan_result\", \"data\": {jsonList}}}");
        }
    }
}
