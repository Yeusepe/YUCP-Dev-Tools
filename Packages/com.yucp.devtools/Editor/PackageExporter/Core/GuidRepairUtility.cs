using System;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal static class GuidRepairUtility
    {
        internal struct RepairResult
        {
            public bool success;
            public string oldGuid;
            public string newGuid;
            public int updatedCount;
            public string errorMessage;
            public string warningMessage;
        }

        internal static RepairResult RepairDerivedGuid(string assetPath, bool showProgress)
        {
            var result = new RepairResult
            {
                success = false,
                oldGuid = null,
                newGuid = null,
                updatedCount = 0,
                errorMessage = null,
                warningMessage = null
            };

            if (string.IsNullOrEmpty(assetPath))
            {
                result.errorMessage = "Asset path is empty.";
                return result;
            }

            string currentGuid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(currentGuid))
            {
                result.errorMessage = $"Could not read file information for:\n{assetPath}\n\nPlease make sure the file exists and try again.";
                return result;
            }

            try
            {
                if (showProgress)
                {
                    EditorUtility.DisplayProgressBar("Fixing File References", "Preparing...", 0.1f);
                }

                result.oldGuid = currentGuid;

                UnityEditor.GUID unityGuid = UnityEditor.GUID.Generate();
                string newDerivedGuid = unityGuid.ToString();

                if (showProgress)
                {
                    EditorUtility.DisplayProgressBar("Fixing File References", "Updating file ID...", 0.3f);
                }

                if (!MetaFileManager.ChangeGuidPreservingContent(assetPath, newDerivedGuid))
                {
                    result.errorMessage = "Failed to update the file. Please check the Console window for details and make sure the file is not locked or in use.";
                    return result;
                }

                if (showProgress)
                {
                    EditorUtility.DisplayProgressBar("Fixing File References", "Refreshing Unity...", 0.5f);
                }

                AssetDatabase.Refresh(ImportAssetOptions.Default);
                Thread.Sleep(300);

                string verifyGuid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(verifyGuid))
                {
                    result.errorMessage = "Could not verify the file was updated. Please check if the file exists and try again.";
                    return result;
                }

                if (verifyGuid != newDerivedGuid)
                {
                    string metaGuid = MetaFileManager.ReadGuid(assetPath);
                    if (!string.IsNullOrEmpty(metaGuid) && metaGuid == newDerivedGuid)
                    {
                        result.warningMessage =
                            $"GUID mismatch - .meta file has {newDerivedGuid} but Unity reports {verifyGuid}. This is likely a Unity cache issue.";
                    }
                    else
                    {
                        newDerivedGuid = verifyGuid;
                        result.warningMessage =
                            $"Unity assigned a different GUID: {verifyGuid} instead of our {newDerivedGuid}";
                    }
                }

                if (newDerivedGuid == currentGuid)
                {
                    result.errorMessage =
                        "The file ID did not change. Unity might be using cached information.\n\n" +
                        "Try one of these:\n" +
                        "- Close and reopen Unity\n" +
                        "- Restart your computer\n" +
                        "- Contact support if the problem persists";
                    return result;
                }

                if (showProgress)
                {
                    EditorUtility.DisplayProgressBar("Fixing File References", "Updating connections in prefabs and scenes...", 0.7f);
                }

                int updatedCount = GuidReferenceUpdater.UpdateReferences(currentGuid, newDerivedGuid, assetPath);

                if (showProgress)
                {
                    EditorUtility.DisplayProgressBar("Fixing File References", "Finishing up...", 0.9f);
                }

                AssetDatabase.Refresh();

                result.success = true;
                result.newGuid = newDerivedGuid;
                result.updatedCount = updatedCount;

                Debug.Log($"[YUCP] Regenerated GUID for derived FBX {assetPath}: {currentGuid} -> {newDerivedGuid}");
                Debug.Log($"[YUCP] GUID regeneration complete. Updated {updatedCount} file(s). Derived FBX now has GUID {newDerivedGuid}.");
                return result;
            }
            catch (Exception ex)
            {
                result.errorMessage = $"Failed to regenerate GUID: {ex.Message}";
                Debug.LogError($"[YUCP] Error regenerating GUID: {ex.Message}\n{ex.StackTrace}");
                return result;
            }
            finally
            {
                if (showProgress)
                {
                    EditorUtility.ClearProgressBar();
                }
            }
        }
    }
}
