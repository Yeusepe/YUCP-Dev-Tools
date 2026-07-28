using System;
using System.IO;
using System.Text;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal static class UnityPackageStagingWriter
    {
        internal static bool TryInjectPrecompiledEditorBinary(string tempExtractDir, string sourcePath, string targetPath, string seed)
        {
            if (!File.Exists(sourcePath))
            {
                return false;
            }

            string guid = CreateDeterministicInjectedGuid(seed);
            string metaPath = sourcePath + ".meta";
            string metaContent = File.Exists(metaPath)
                ? File.ReadAllText(metaPath)
                : GenerateEditorOnlyDllMeta(guid);

            WriteFileAsset(tempExtractDir, sourcePath, targetPath, guid, metaContent);
            return true;
        }

        internal static void WriteTextAsset(string tempExtractDir, string content, string targetPath, string metaWithoutGuid)
        {
            string guid = Guid.NewGuid().ToString("N");
            string folder = Path.Combine(tempExtractDir, guid);
            Directory.CreateDirectory(folder);

            File.WriteAllText(Path.Combine(folder, "asset"), content ?? string.Empty);
            File.WriteAllText(Path.Combine(folder, "pathname"), targetPath);
            File.WriteAllText(Path.Combine(folder, "asset.meta"), metaWithoutGuid.Replace("__GUID__", guid));
        }

        internal static void WriteFileAsset(string tempExtractDir, string sourcePath, string targetPath, string guid, string metaContent)
        {
            string folder = Path.Combine(tempExtractDir, guid);
            Directory.CreateDirectory(folder);

            File.Copy(sourcePath, Path.Combine(folder, "asset"), true);
            File.WriteAllText(Path.Combine(folder, "pathname"), targetPath);
            File.WriteAllText(Path.Combine(folder, "asset.meta"), metaContent);
        }

        internal static string MonoImporterMeta()
        {
            return "fileFormatVersion: 2\nguid: __GUID__\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
        }

        internal static string DefaultImporterMeta(string guid)
        {
            return "fileFormatVersion: 2\nguid: " + guid + "\nDefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
        }

        internal static string CreateDeterministicInjectedGuid(string seed)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                string normalizedSeed = (seed ?? string.Empty)
                    .Replace('\\', '/')
                    .ToLowerInvariant();
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(normalizedSeed));
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }

                return sb.ToString();
            }
        }

        internal static string GenerateEditorOnlyDllMeta(string guid)
        {
            return
                "fileFormatVersion: 2\n" +
                "guid: " + guid + "\n" +
                "PluginImporter:\n" +
                "  externalObjects: {}\n" +
                "  serializedVersion: 2\n" +
                "  iconMap: {}\n" +
                "  executionOrder: {}\n" +
                "  defineConstraints: []\n" +
                "  isPreloaded: 0\n" +
                "  isOverridable: 0\n" +
                "  isExplicitlyReferenced: 0\n" +
                "  validateReferences: 1\n" +
                "  platformData:\n" +
                "  - first:\n" +
                "      : Any\n" +
                "    second:\n" +
                "      enabled: 0\n" +
                "      settings:\n" +
                "        Exclude Editor: 0\n" +
                "        Exclude Linux64: 1\n" +
                "        Exclude OSXUniversal: 1\n" +
                "        Exclude Win: 0\n" +
                "        Exclude Win64: 0\n" +
                "  - first:\n" +
                "      Any: \n" +
                "    second:\n" +
                "      enabled: 1\n" +
                "      settings: {}\n" +
                "  - first:\n" +
                "      Editor: Editor\n" +
                "    second:\n" +
                "      enabled: 1\n" +
                "      settings:\n" +
                "        CPU: AnyCPU\n" +
                "        DefaultValueInitialized: true\n" +
                "        OS: AnyOS\n" +
                "  - first:\n" +
                "      Standalone: Linux64\n" +
                "    second:\n" +
                "      enabled: 0\n" +
                "      settings:\n" +
                "        CPU: None\n" +
                "  - first:\n" +
                "      Standalone: OSXUniversal\n" +
                "    second:\n" +
                "      enabled: 0\n" +
                "      settings:\n" +
                "        CPU: None\n" +
                "  - first:\n" +
                "      Standalone: Win\n" +
                "    second:\n" +
                "      enabled: 0\n" +
                "      settings:\n" +
                "        CPU: None\n" +
                "  - first:\n" +
                "      Standalone: Win64\n" +
                "    second:\n" +
                "      enabled: 0\n" +
                "      settings:\n" +
                "        CPU: None\n" +
                "  userData: \n" +
                "  assetBundleName: \n" +
                "  assetBundleVariant: \n";
        }
    }
}
