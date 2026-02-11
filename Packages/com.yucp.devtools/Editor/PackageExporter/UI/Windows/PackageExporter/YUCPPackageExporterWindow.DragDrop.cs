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
        private void UpdateDropTargets(VisualElement listContainer, int targetIndex, VisualElement draggingItem)
        {
            int visualIndex = 0;
            foreach (var child in listContainer.Children())
            {
                if (child == draggingItem) continue;
                if (child.ClassListContains("yucp-folder-content")) continue;

                if (child.ClassListContains("yucp-folder-header") || child.ClassListContains("yucp-profile-item"))
                {
                    if (visualIndex == targetIndex)
                    {
                        child.AddToClassList("yucp-profile-item-drop-target");
                    }
                    else
                    {
                        child.RemoveFromClassList("yucp-profile-item-drop-target");
                    }
                    visualIndex++;
                }
            }
        }

        private void ClearDropTargets(VisualElement listContainer)
        {
            foreach (var child in listContainer.Children())
            {
                child.RemoveFromClassList("yucp-profile-item-drop-target");
                child.style.borderTopWidth = 0;
            }
            potentialDropIndex = -1;
        }

        private void UpdateGapAnimations()
        {
            if (!hasDragged)
            {
                // Clear gaps when not dragging
                if (rowGaps.Count > 0)
                {
                    rowGaps.Clear();
                }
                return;
            }
            
            var listContainer = _draggingListContainer ?? _profileListContainer;
            if (listContainer == null) return;
            
            // Calculate deltaTime (vFavorites approach)
            double currentTime = EditorApplication.timeSinceStartup;
            float deltaTime = (float)(currentTime - (lastGapUpdateTime > 0 ? lastGapUpdateTime : currentTime));
            if (deltaTime > 0.05f) deltaTime = 0.0166f;
            lastGapUpdateTime = currentTime;
            
            float lerpSpeed = 10f; // vFavorites uses lerpSpeed = 10
            float rowHeight = draggingElement != null ? draggingElement.resolvedStyle.height : 60f;
            if (rowHeight <= 0) rowHeight = 60f;
            
            // Get all items (profiles and folders) in visual order, excluding dragged element
            var allItems = new List<VisualElement>();
            foreach (var child in listContainer.Children())
            {
                if (child.ClassListContains("yucp-folder-content")) continue;
                if (child == draggingElement) continue;
                
                // Include both profile items and folder headers
                if (child.ClassListContains("yucp-profile-item") || child.ClassListContains("yucp-folder-header"))
                {
                    allItems.Add(child);
                }
            }
            
            // Sort by visual Y position
            allItems = allItems.OrderBy(el => el.worldBound.y).ToList();
            
            // Ensure rowGaps has entries for all visual indices
            for (int i = 0; i < allItems.Count; i++)
            {
                if (!rowGaps.ContainsKey(i))
                {
                    rowGaps[i] = 0f;
                }
            }
            
            // Animate gaps using Lerp (vFavorites: rowGaps[i] -> rowHeight when i == insertDraggedItemAtIndex)
            bool needsRepaint = false;
            var keysToUpdate = rowGaps.Keys.ToList();
            
            foreach (var idx in keysToUpdate)
            {
                float currentGap = rowGaps[idx];
                // Gap should be rowHeight at insertDraggedItemAtIndex, 0 everywhere else
                float targetGap = (hasDragged && idx == insertDraggedItemAtIndex) ? rowHeight : 0f;
                
                // Lerp towards target
                float newGap = Mathf.Lerp(currentGap, targetGap, lerpSpeed * deltaTime);
                
                // Apply gap using marginTop on the corresponding element by visual index
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

        private int CalculateDropIndex(VisualElement listContainer, Vector2 mousePos, int draggingIdx)
        {
            int dropIndex = draggingIdx;
            var sortedItems = new List<(int index, VisualElement element, float y)>();
            Vector2 localMousePos = listContainer.WorldToLocal(mousePos);
            
            // Collect all items with their indices and Y positions
            // Account for gaps (vFavorites approach)
            float accumulatedGap = 0f;
            foreach (var child in listContainer.Children())
            {
                if (child == draggingElement) continue;
                
                if (child.userData is int idx)
                {
                    Rect layout = child.layout;
                    // Add accumulated gap offset
                    float visualY = layout.y + accumulatedGap;
                    if (rowGaps.ContainsKey(idx))
                    {
                        accumulatedGap += rowGaps[idx];
                    }
                    sortedItems.Add((idx, child, visualY));
                }
            }
            
            // Sort by Y position
            sortedItems.Sort((a, b) => a.y.CompareTo(b.y));
            
            // Find where the mouse is
            for (int i = 0; i < sortedItems.Count; i++)
            {
                var item = sortedItems[i];
                Rect layout = item.element.layout;
                float yMin = layout.y;
                float yMax = layout.y + layout.height;

                if (localMousePos.y >= yMin && localMousePos.y <= yMax)
                {
                    // Mouse is over this item - check if it's in upper or lower half
                    float relativeY = localMousePos.y - yMin;
                    if (relativeY < layout.height * 0.5f)
                    {
                        // Insert before this item
                        dropIndex = item.index;
                    }
                    else
                    {
                        // Insert after this item
                        dropIndex = item.index + 1;
                    }
                    break;
                }
                else if (localMousePos.y < yMin)
                {
                    // Mouse is above this item - insert before it
                    dropIndex = item.index;
                    break;
                }
            }
            
            // If we didn't find a position, drop at the end
            if (sortedItems.Count > 0)
            {
                var last = sortedItems[sortedItems.Count - 1].element.layout;
                float lastMaxY = last.y + last.height;
                if (localMousePos.y > lastMaxY)
                {
                    dropIndex = sortedItems[sortedItems.Count - 1].index + 1;
                }
            }
            
            // Clamp to valid range (accounting for the fact that draggingIdx will be removed)
            int maxIndex = allProfiles.Count - 1;
            dropIndex = Mathf.Clamp(dropIndex, 0, maxIndex);
            
            return dropIndex;
        }

        private void UpdateFolderDropTargets(VisualElement listContainer, VisualElement dropTarget)
        {
            // Clear previous targets
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

        private int CalculateDropIndexWithFolders(VisualElement listContainer, Vector2 mousePosition, int draggingIdx, out VisualElement folderTarget)
        {
            folderTarget = null;

            var sortedItems = new List<(int visualIndex, VisualElement element, float y)>();
            int visualIndex = 0;

            // Collect all visible items (folders and uncategorized profiles) with their positions
            foreach (var child in listContainer.Children())
            {
                if (child == draggingElement) continue;
                if (child.ClassListContains("yucp-folder-content")) continue;

                if (child.ClassListContains("yucp-folder-header"))
                {
                    Rect worldBound = child.worldBound;
                    // Detect folder target hover
                    if (worldBound.Contains(mousePosition))
                    {
                        folderTarget = child;
                        return -2;
                    }
                    sortedItems.Add((visualIndex++, child, worldBound.y));
                }
                else if (child.ClassListContains("yucp-profile-item") && child.userData is int profileIdx)
                {
                    var profile = allProfiles[profileIdx];
                    if (!string.IsNullOrEmpty(profile.folderName) && projectFolders.Contains(profile.folderName))
                    {
                        continue;
                    }
                    Rect worldBound = child.worldBound;
                    sortedItems.Add((visualIndex++, child, worldBound.y));
                }
            }

            // Sort by Y position
            sortedItems.Sort((a, b) => a.y.CompareTo(b.y));

            // Find where the mouse is
            for (int i = 0; i < sortedItems.Count; i++)
            {
                var item = sortedItems[i];
                Rect worldBound = item.element.worldBound;

                if (worldBound.Contains(mousePosition))
                {
                    float relativeY = mousePosition.y - worldBound.y;
                    return relativeY < worldBound.height * 0.5f ? item.visualIndex : item.visualIndex + 1;
                }
                else if (mousePosition.y < worldBound.y)
                {
                    return item.visualIndex;
                }
            }

            // If we didn't find a position, drop at the end
            if (sortedItems.Count > 0)
            {
                return sortedItems[sortedItems.Count - 1].visualIndex + 1;
            }

            return 0;
        }

        private void ReorderProfiles(int sourceProfileIndex, int targetVisualIndex)
        {
            if (sourceProfileIndex < 0 || sourceProfileIndex >= allProfiles.Count ||
                targetVisualIndex < 0)
            {
                return;
            }
            
            // Get the profile to move
            var profileToMove = allProfiles[sourceProfileIndex];
            string profileGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profileToMove));
            
            // Skip if profile is in a folder (folders handle their own profiles)
            if (!string.IsNullOrEmpty(profileToMove.folderName) && projectFolders.Contains(profileToMove.folderName))
            {
                return;
            }
            
            // Find the profile in unified order
            int unifiedSourceIndex = -1;
            for (int i = 0; i < unifiedOrder.Count; i++)
            {
                if (!unifiedOrder[i].isFolder && unifiedOrder[i].identifier == profileGuid)
                {
                    unifiedSourceIndex = i;
                    break;
                }
            }
            
            if (unifiedSourceIndex < 0)
            {
                // Profile not in unified order, add it
                unifiedOrder.Add(new UnifiedOrderItem { isFolder = false, identifier = profileGuid });
                unifiedSourceIndex = unifiedOrder.Count - 1;
            }
            
            // Calculate target position in unified order based on visual position
            // We need to map the visual targetIndex to a unified order position
            int unifiedTargetIndex = CalculateUnifiedOrderPositionFromVisualIndex(targetVisualIndex, unifiedSourceIndex);
            
            if (unifiedTargetIndex >= 0 && unifiedTargetIndex != unifiedSourceIndex)
            {
                // Remove from source position
                var item = unifiedOrder[unifiedSourceIndex];
                unifiedOrder.RemoveAt(unifiedSourceIndex);
                
                // Adjust target index if source was before target
                int adjustedTargetIndex = unifiedSourceIndex < unifiedTargetIndex ? unifiedTargetIndex - 1 : unifiedTargetIndex;
                
                // Insert at target position
                unifiedOrder.Insert(adjustedTargetIndex, item);
                
                // Save the new order
                SaveCustomOrder();
            }
            
            // Update selection indices
            // Note: Selection indices update is based on old profile indices, skip for now
            // TODO: Consider if this needs updating for visual indices
            // UpdateSelectionIndicesAfterReorder(sourceProfileIndex, targetVisualIndex);
            
            // Refresh the UI
            UpdateProfileList();
        }

        private int CalculateUnifiedOrderPositionFromVisualIndex(int visualTargetIndex, int excludeUnifiedIndex)
        {
            // Build a map of visual positions to unified order indices
            var visualToUnified = new List<int>();
            var profileGuidToIndex = allProfiles
                .Select((p, idx) => new { Profile = p, Index = idx })
                .ToDictionary(x => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(x.Profile)), x => x.Index);
            
            for (int i = 0; i < unifiedOrder.Count; i++)
            {
                if (i == excludeUnifiedIndex)
                    continue;
                    
                if (unifiedOrder[i].isFolder)
                {
                    // Folders take one visual position
                    visualToUnified.Add(i);
                }
                else
                {
                    // Profiles take one visual position (only if not in a folder)
                    if (profileGuidToIndex.TryGetValue(unifiedOrder[i].identifier, out int profileIndex))
                    {
                        var profile = allProfiles[profileIndex];
                        if (string.IsNullOrEmpty(profile.folderName) || !projectFolders.Contains(profile.folderName))
                        {
                            visualToUnified.Add(i);
                        }
                    }
                }
            }
            
            // Find the unified order index for the target visual position
            if (visualTargetIndex >= 0 && visualTargetIndex < visualToUnified.Count)
            {
                return visualToUnified[visualTargetIndex];
            }
            
            // Default to end
            return unifiedOrder.Count;
        }

        private void ReorderFolder(string folderName, int targetVisualIndex)
        {
            // Find the folder in unified order
            int unifiedSourceIndex = -1;
            for (int i = 0; i < unifiedOrder.Count; i++)
            {
                if (unifiedOrder[i].isFolder && unifiedOrder[i].identifier == folderName)
                {
                    unifiedSourceIndex = i;
                    break;
                }
            }
            
            if (unifiedSourceIndex < 0)
            {
                // Folder not in unified order, add it
                unifiedOrder.Add(new UnifiedOrderItem { isFolder = true, identifier = folderName });
                unifiedSourceIndex = unifiedOrder.Count - 1;
            }
            
            // Calculate target position in unified order
            int unifiedTargetIndex = CalculateUnifiedOrderPositionFromVisualIndex(targetVisualIndex, unifiedSourceIndex);
            
            if (unifiedTargetIndex >= 0 && unifiedTargetIndex != unifiedSourceIndex)
            {
                // Remove from source position
                var item = unifiedOrder[unifiedSourceIndex];
                unifiedOrder.RemoveAt(unifiedSourceIndex);
                
                // Adjust target index if source was before target
                int adjustedTargetIndex = unifiedSourceIndex < unifiedTargetIndex ? unifiedTargetIndex - 1 : unifiedTargetIndex;
                
                // Insert at target position
                unifiedOrder.Insert(adjustedTargetIndex, item);
                
                // Save the new order
                SaveCustomOrder();
                
                // Refresh the UI
                UpdateProfileList();
            }
        }

        private int CalculateDropIndexForFolder(VisualElement listContainer, Vector2 mousePos, string draggingFolderName)
        {
            var sortedItems = new List<(int visualIndex, VisualElement element, float y, bool isFolder, string identifier)>();
            int visualIndex = 0;
            
            // Collect all visible items (folders and uncategorized profiles) with their positions
            foreach (var child in listContainer.Children())
            {
                if (child == draggingElement) continue;
                
                if (child.ClassListContains("yucp-folder-header"))
                {
                    if (child.userData is string folderName && folderName != draggingFolderName)
                    {
                        Rect worldBound = child.worldBound;
                        sortedItems.Add((visualIndex++, child, worldBound.y, true, folderName));
                    }
                }
                else if (child.userData is int profileIdx)
                {
                    // Only count uncategorized profiles (those not in folders)
                    var profile = allProfiles[profileIdx];
                    if (string.IsNullOrEmpty(profile.folderName) || !projectFolders.Contains(profile.folderName))
                    {
                        Rect worldBound = child.worldBound;
                        sortedItems.Add((visualIndex++, child, worldBound.y, false, profileIdx.ToString()));
                    }
                }
            }
            
            // Sort by Y position
            sortedItems.Sort((a, b) => a.y.CompareTo(b.y));
            
            // Find where the mouse is
            for (int i = 0; i < sortedItems.Count; i++)
            {
                var item = sortedItems[i];
                Rect worldBound = item.element.worldBound;
                
                if (worldBound.Contains(mousePos))
                {
                    float relativeY = mousePos.y - worldBound.y;
                    if (relativeY < worldBound.height * 0.5f)
                    {
                        return item.visualIndex;
                    }
                    else
                    {
                        return item.visualIndex + 1;
                    }
                }
                else if (mousePos.y < worldBound.y)
                {
                    return item.visualIndex;
                }
            }
            
            // If we didn't find a position, drop at the end
            if (sortedItems.Count > 0)
            {
                return sortedItems[sortedItems.Count - 1].visualIndex + 1;
            }
            
            return 0;
        }

        private void UpdateSelectionIndicesAfterReorder(int oldIndex, int newIndex)
        {
            var newIndices = new HashSet<int>();
            
            foreach (int idx in selectedProfileIndices)
            {
                if (idx == oldIndex)
                {
                    // The moved item
                    newIndices.Add(newIndex);
                }
                else if (oldIndex < newIndex)
                {
                    // Source was before target - items between move down
                    if (idx > oldIndex && idx <= newIndex)
                    {
                        newIndices.Add(idx - 1);
                    }
                    else
                    {
                        newIndices.Add(idx);
                    }
                }
                else
                {
                    // Source was after target - items between move up
                    if (idx >= newIndex && idx < oldIndex)
                    {
                        newIndices.Add(idx + 1);
                    }
                    else
                    {
                        newIndices.Add(idx);
                    }
                }
            }
            
            selectedProfileIndices = newIndices;
            
            // Update selectedProfile if it was moved
            if (selectedProfile != null)
            {
                int currentIndex = allProfiles.IndexOf(selectedProfile);
                if (currentIndex >= 0)
                {
                    selectedProfileIndices.Clear();
                    selectedProfileIndices.Add(currentIndex);
                    lastClickedProfileIndex = currentIndex;
                }
            }
        }

    }
}
