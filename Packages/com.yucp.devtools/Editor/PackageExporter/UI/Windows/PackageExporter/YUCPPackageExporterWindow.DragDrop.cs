using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using YUCP.DevTools.Components;
using YUCP.DevTools.Editor.PackageExporter.UI.Components;
using YUCP.Motion;
using YUCP.Motion.Core;

namespace YUCP.DevTools.Editor.PackageExporter
{
    public partial class YUCPPackageExporterWindow
    {
        /// <summary>
        /// Every row in the profile list in on-screen order: folder headers, root profiles and profiles
        /// inside expanded folders. Folder headers also carry "yucp-profile-item".
        /// </summary>
        private static List<VisualElement> GetListRows(VisualElement listContainer)
        {
            return listContainer.Query(className: "yucp-profile-item").ToList();
        }

        /// <summary>The top-level profile list containing a row (rows in folders sit one level deeper).</summary>
        private static VisualElement GetListRoot(VisualElement row)
        {
            var parent = row.parent;
            return parent != null && parent.ClassListContains("yucp-folder-content") ? parent.parent : parent;
        }

        /// <summary>
        /// Finds where a dragged row would land: beside the returned row (<paramref name="after"/> says which side),
        /// or at the end of the list when it returns null. With <paramref name="allowFolderTarget"/>, hovering the body
        /// of a folder header returns null and sets <paramref name="folderTarget"/> (drop into that folder).
        /// </summary>
        private VisualElement FindDropRow(VisualElement listContainer, Vector2 mousePosition, bool allowFolderTarget,
            out bool after, out VisualElement folderTarget)
        {
            after = false;
            folderTarget = null;
            foreach (var row in GetListRows(listContainer))
            {
                Rect bounds = row.worldBound;
                if (mousePosition.y > bounds.yMax) continue;

                // Top quarter of a folder header still means "above the folder"
                if (allowFolderTarget && row.ClassListContains("yucp-folder-header") &&
                    mousePosition.y >= bounds.y + bounds.height * 0.25f)
                {
                    folderTarget = row;
                    return null;
                }

                after = mousePosition.y >= bounds.center.y;
                return row;
            }
            return null;
        }

        /// <summary>Row index whose top gets the insertion gap, matching <see cref="UpdateGapAnimations"/>.</summary>
        private static int GetGapIndex(List<VisualElement> rows, VisualElement dropRow, bool after)
        {
            return dropRow == null ? rows.Count : rows.IndexOf(dropRow) + (after ? 1 : 0);
        }

        private void UpdateDropTargets(VisualElement listContainer, int gapIndex)
        {
            var rows = GetListRows(listContainer);
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].EnableInClassList("yucp-profile-item-drop-target", i == gapIndex);
            }
        }

        private void ClearDropTargets(VisualElement listContainer)
        {
            foreach (var row in GetListRows(listContainer))
            {
                row.RemoveFromClassList("yucp-profile-item-drop-target");
            }
        }

        private void UpdateGapAnimations()
        {
            if (!hasDragged)
            {

                if (rowGaps.Count > 0)
                {
                    rowGaps.Clear();
                }
                return;
            }

            var listContainer = _draggingListContainer ?? _profileListContainer;
            if (listContainer == null) return;


            double currentTime = EditorApplication.timeSinceStartup;
            float deltaTime = (float)(currentTime - (lastGapUpdateTime > 0 ? lastGapUpdateTime : currentTime));
            if (deltaTime > 0.05f) deltaTime = 0.0166f;
            lastGapUpdateTime = currentTime;

            float lerpSpeed = 10f;
            float rowHeight = draggingElement != null ? draggingElement.resolvedStyle.height : 60f;
            if (rowHeight <= 0) rowHeight = 60f;


            // The dragged row lives in the floating drag container, so it is not part of this list
            var allItems = GetListRows(listContainer);


            for (int i = 0; i < allItems.Count; i++)
            {
                if (!rowGaps.ContainsKey(i))
                {
                    rowGaps[i] = 0f;
                }
            }


            bool needsRepaint = false;
            var keysToUpdate = rowGaps.Keys.ToList();

            foreach (var idx in keysToUpdate)
            {
                float currentGap = rowGaps[idx];

                float targetGap = (hasDragged && idx == insertDraggedItemAtIndex) ? rowHeight : 0f;


                float newGap = Mathf.Lerp(currentGap, targetGap, lerpSpeed * deltaTime);


                if (idx >= 0 && idx < allItems.Count)
                {
                    var child = allItems[idx];
                    child.style.marginTop = newGap;
                    rowGaps[idx] = newGap;

                    if (Mathf.Abs(currentGap - newGap) > 0.1f)
                    {
                        needsRepaint = true;
                    }
                }
            }

            if (needsRepaint)
            {
                Repaint();
            }
        }


        private void UpdateFolderDropTargets(VisualElement listContainer, VisualElement dropTarget)
        {

             foreach (var child in listContainer.Children())
            {
                child.RemoveFromClassList("yucp-folder-header-active-target");

                if (child == dropTarget && child.ClassListContains("yucp-folder-header"))
                {
                    child.AddToClassList("yucp-folder-header-active-target");
                }
            }
        }

        private void ResetStackState()
        {
            if (stackTargetElement != null)
            {
                stackTargetElement.RemoveFromClassList("yucp-profile-item-stack-target");
            }
            stackTargetElement = null;
            stackTargetIndex = -1;
            stackHoverStartTime = 0;
            stackHoverPosition = Vector2.zero;
            isStackReady = false;
        }

        /// <summary>unifiedOrder index of the top-level item a row belongs to (rows inside a folder map to the folder).</summary>
        private int FindTopLevelOrderIndex(VisualElement row)
        {
            if (row?.userData is string folderName)
                return unifiedOrder.FindIndex(item => item.isFolder && item.identifier == folderName);

            if (row?.userData is int idx && idx >= 0 && idx < allProfiles.Count)
            {
                var profile = allProfiles[idx];
                if (IsInProjectFolder(profile))
                    return unifiedOrder.FindIndex(item => item.isFolder && item.identifier == profile.folderName);

                string guid = GetProfileGuid(profile);
                return unifiedOrder.FindIndex(item => !item.isFolder && item.identifier == guid);
            }

            return -1;
        }

        /// <summary>
        /// Moves a profile beside <paramref name="targetRow"/>. Landing next to a profile inside a folder puts it in
        /// that folder; landing next to a root profile, a folder header, or at the end (null row) puts it at the root.
        /// </summary>
        private void MoveProfileNextTo(ExportProfile profile, VisualElement targetRow, bool after)
        {
            ExportProfile targetProfile = targetRow?.userData is int idx && idx >= 0 && idx < allProfiles.Count
                ? allProfiles[idx]
                : null;
            if (profile == null || targetProfile == profile) return;

            string folder = targetProfile != null && IsInProjectFolder(targetProfile) ? targetProfile.folderName : "";
            if ((profile.folderName ?? "") != folder)
            {
                profile.folderName = folder;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }

            string guid = GetProfileGuid(profile);
            unifiedOrder.RemoveAll(item => !item.isFolder && item.identifier == guid);

            if (folder.Length == 0)
            {
                int targetIndex = FindTopLevelOrderIndex(targetRow);
                unifiedOrder.Insert(targetIndex < 0 ? unifiedOrder.Count : targetIndex + (after ? 1 : 0),
                    new UnifiedOrderItem { isFolder = false, identifier = guid });
            }
            else
            {
                // Folder contents are ordered by allProfiles. Reordering it shifts indices, so carry the selection over by profile.
                var selected = selectedProfileIndices.Where(i => i >= 0 && i < allProfiles.Count).Select(i => allProfiles[i]).ToList();
                var anchor = lastClickedProfileIndex >= 0 && lastClickedProfileIndex < allProfiles.Count ? allProfiles[lastClickedProfileIndex] : null;

                allProfiles.Remove(profile);
                allProfiles.Insert(allProfiles.IndexOf(targetProfile) + (after ? 1 : 0), profile);

                selectedProfileIndices = new HashSet<int>(selected.Select(p => allProfiles.IndexOf(p)));
                lastClickedProfileIndex = anchor != null ? allProfiles.IndexOf(anchor) : -1;
            }

            SaveCustomOrder(); // allProfiles order + unified order
            UpdateProfileList();
        }

        /// <summary>Moves a folder beside the top-level item that <paramref name="targetRow"/> belongs to (null = end).</summary>
        private void MoveFolderNextTo(string folderName, VisualElement targetRow, bool after)
        {
            // Rows inside a folder stand for that folder; landing among them means "after it"
            if (targetRow?.userData is int idx && idx >= 0 && idx < allProfiles.Count && IsInProjectFolder(allProfiles[idx]))
            {
                if (allProfiles[idx].folderName == folderName) return;
                after = true;
            }
            if (targetRow?.userData is string targetFolder && targetFolder == folderName) return;

            var item = unifiedOrder.Find(i => i.isFolder && i.identifier == folderName)
                ?? new UnifiedOrderItem { isFolder = true, identifier = folderName };
            unifiedOrder.Remove(item);

            int targetIndex = FindTopLevelOrderIndex(targetRow);
            unifiedOrder.Insert(targetIndex < 0 ? unifiedOrder.Count : targetIndex + (after ? 1 : 0), item);

            SaveUnifiedOrder();
            UpdateProfileList();
        }

        /// <summary>Ctrl/Cmd+Up/Down: moves a profile one row, stepping into and out of folders along the way.</summary>
        private void MoveProfileOneRow(ExportProfile profile, bool isUp)
        {
            if (currentSortOption != ProfileSortOption.Custom)
            {
                // Manual order only exists in the Custom view
                currentSortOption = ProfileSortOption.Custom;
                UpdateProfileList();
            }
            if (_profileListContainer == null) return;

            int profileIndex = allProfiles.IndexOf(profile);
            var rows = GetListRows(_profileListContainer);
            int row = rows.FindIndex(r => r.userData is int idx && idx == profileIndex);
            int targetRow = row + (isUp ? -1 : 1);
            if (row < 0 || targetRow < 0 || targetRow >= rows.Count) return;

            var target = rows[targetRow];
            if (target.userData is string folderName)
            {
                if (isUp || IsInProjectFolder(profile))
                {
                    // Leave the current folder, or hop over a collapsed one, landing at the root
                    MoveProfileNextTo(profile, target, false);
                }
                else if (targetRow + 1 < rows.Count && rows[targetRow + 1].userData is int first &&
                         allProfiles[first].folderName == folderName)
                {
                    // Enter an expanded folder at the top
                    MoveProfileNextTo(profile, rows[targetRow + 1], false);
                }
                else
                {
                    MoveProfileNextTo(profile, target, true);
                }
                return;
            }

            // Swap within the same folder/root; crossing a boundary enters the folder at the near end or leaves it
            bool sameFolder = (allProfiles[(int)target.userData].folderName ?? "") == (profile.folderName ?? "");
            MoveProfileNextTo(profile, target, isUp ? !sameFolder : sameFolder);
        }

    }
}
