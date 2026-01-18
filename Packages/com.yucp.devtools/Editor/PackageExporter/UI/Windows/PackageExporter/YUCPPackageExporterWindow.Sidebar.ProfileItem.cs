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
        private VisualElement CreateProfileItem(ExportProfile profile, int index)
        {
            var item = new VisualElement();
            item.AddToClassList("yucp-profile-item");
            item.userData = index; // Store index for drag-and-drop
            
            bool isSelected = selectedProfileIndices.Contains(index);
            if (isSelected)
            {
                item.AddToClassList("yucp-profile-item-selected");
            }
            
            // Icon container
            var iconContainer = new VisualElement();
            iconContainer.AddToClassList("yucp-profile-item-icon-container");
            
            var iconImage = new Image();
            Texture2D displayIcon = profile.icon;
            if (displayIcon == null)
            {
                displayIcon = GetPlaceholderTexture();
            }
            iconImage.image = displayIcon;
            iconImage.AddToClassList("yucp-profile-item-icon");
            iconContainer.Add(iconImage);
            
            item.Add(iconContainer);
            
            // Content column
            var contentColumn = new VisualElement();
            contentColumn.AddToClassList("yucp-profile-item-content");
            contentColumn.style.flexGrow = 1;
            
            // Name + Tags Row
            var nameRow = new VisualElement();
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.alignItems = Align.Center;
            nameRow.style.flexWrap = Wrap.Wrap;
            nameRow.style.marginBottom = 4; // Move spacing here from label
            
            // Profile name
            var nameLabel = new Label(GetProfileDisplayName(profile));
            nameLabel.AddToClassList("yucp-profile-item-name");
            nameLabel.style.marginRight = 8; // Space before tags
            nameLabel.style.marginBottom = 0; // Remove default margin-bottom from USS
            nameLabel.style.marginTop = 0;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft; // Ensure vertical centering of text
            nameRow.Add(nameLabel);
            
            // Tags (beside name)
            var allTags = profile.GetAllTags();
            if (allTags.Count > 0)
            {
                var tagsContainer = new VisualElement();
                tagsContainer.style.flexDirection = FlexDirection.Row;
                tagsContainer.style.alignItems = Align.Center;
                tagsContainer.style.flexWrap = Wrap.NoWrap; // Try to keep on same line if possible
                tagsContainer.name = "tags-container";
                
                // Show up to 2 tags
                int maxVisibleTags = 2;
                var tagsToShow = allTags.Take(maxVisibleTags).ToList();
                
                foreach (var tag in tagsToShow)
                {
                    var baseColor = GetTagColor(tag);
                    
                    var chip = new VisualElement();
                    chip.AddToClassList("yucp-tag-chip");
                    // Make chips slightly smaller/compact for this inline view
                    chip.style.height = 18;
                    chip.style.paddingLeft = 6;
                    chip.style.paddingRight = 6;
                    chip.style.justifyContent = Justify.Center; // Cross-axis (horizontal for row)
                    chip.style.alignItems = Align.Center;       // Main-axis (vertical for row)
                    
                    chip.style.marginRight = 4;
                    chip.style.marginTop = 0;
                    chip.style.marginBottom = 0;
                    chip.AddToClassList("yucp-clickable-tag"); // CSS class for cursor: link
                    
                    // Apply colors
                    chip.style.backgroundColor = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.15f));
                    chip.style.borderLeftColor = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.3f));
                    chip.style.borderRightColor = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.3f));
                    chip.style.borderTopColor = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.3f));
                    chip.style.borderBottomColor = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.3f));

                    // Add click handler to add tag to search
                    chip.RegisterCallback<MouseDownEvent>(evt => 
                    {
                        if (evt.button == 0) // Left click
                        {
                            _mainSearchField?.AddTag(tag);
                            _overlaySearchField?.AddTag(tag);
                            evt.StopPropagation();
                        }
                        else if (evt.button == 1) // Right Click
                        {
                            ShowTagContextMenu(tag, chip);
                            evt.StopPropagation();
                        }
                    });
                    
                    var label = new Label(tag);
                    label.AddToClassList("yucp-tag-chip-label");
                    label.userData = tag; // Store full text for responsive switching
                    // Override label font size for compact view
                    label.style.fontSize = 10;
                    label.style.marginRight = 0; // No remove button so no margin needed
                    label.style.unityTextAlign = TextAnchor.MiddleCenter; // Center text inside label
                    label.style.color = new StyleColor(new Color(baseColor.r, baseColor.g, baseColor.b, 0.95f));
                    chip.Add(label);
                    
                    tagsContainer.Add(chip);
                }
                
                if (allTags.Count > maxVisibleTags)
                {
                    var moreChip = new VisualElement();
                    moreChip.AddToClassList("yucp-tag-chip");
                    moreChip.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);
                    moreChip.style.height = 18;
                    moreChip.style.paddingLeft = 6;
                    moreChip.style.paddingRight = 6;
                    moreChip.style.justifyContent = Justify.Center;
                    moreChip.style.marginTop = 0;
                    moreChip.style.marginBottom = 0;
                    
                    var moreLabel = new Label($"+{allTags.Count - maxVisibleTags}");
                    moreLabel.AddToClassList("yucp-tag-chip-label");
                    moreLabel.style.fontSize = 9;
                    moreLabel.style.marginRight = 0;
                    moreLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    moreChip.Add(moreLabel);
                    
                    tagsContainer.Add(moreChip);
                }
                
                nameRow.Add(tagsContainer);
            }

            contentColumn.Add(nameRow);

            // Responsive Logic
            item.RegisterCallback<GeometryChangedEvent>(evt => 
            {
                if (evt.newRect.width == 0) return;
                
                bool isCompact = evt.newRect.width < 260; // Threshold
                
                var tags = item.Query(className: "yucp-tag-chip").ToList();
                foreach(var tagChip in tags)
                {
                    var lbl = tagChip.Q<Label>();
                    // Only apply to actual tags (that have userData string), not the "+N" chip
                    if(lbl != null && lbl.userData is string fullText)
                    {
                        if (isCompact)
                        {
                            // Compact Mode: Initial only, fixed square size
                            lbl.text = !string.IsNullOrEmpty(fullText) ? fullText.Substring(0, 1).ToUpper() : "";
                            tagChip.style.width = 18;
                            tagChip.style.height = 18; // Explicit height
                            tagChip.style.paddingLeft = 0;
                            tagChip.style.paddingRight = 0;
                            tagChip.style.paddingTop = 0;
                            tagChip.style.paddingBottom = 0;
                            tagChip.style.justifyContent = Justify.Center;
                            tagChip.style.alignItems = Align.Center;
                            
                            lbl.style.marginRight = 0;
                            lbl.style.marginLeft = 0;
                            lbl.style.marginTop = 0;
                            lbl.style.marginBottom = 0;
                            lbl.style.paddingTop = 0;
                            lbl.style.paddingBottom = 0;
                            lbl.style.width = 18; // Match container
                            lbl.style.height = 18; // Match container
                            lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                        }
                        else
                        {
                            // Normal Mode: Full text, auto width
                            lbl.text = fullText;
                            
                            // Reset Chip Styles
                            tagChip.style.width = StyleKeyword.Auto;
                            tagChip.style.height = 18; // Keep fixed height
                            tagChip.style.paddingLeft = 6;
                            tagChip.style.paddingRight = 6;
                            tagChip.style.paddingTop = 0;
                            tagChip.style.paddingBottom = 0;
                            tagChip.style.justifyContent = Justify.Center;
                            tagChip.style.alignItems = Align.Center;

                            // Reset Label Styles
                            lbl.style.width = StyleKeyword.Auto;
                            lbl.style.height = StyleKeyword.Auto;
                            lbl.style.marginRight = 0;
                            lbl.style.marginLeft = 0;
                            lbl.style.marginTop = 0;
                            lbl.style.marginBottom = 0;
                            lbl.style.paddingTop = 0;
                            lbl.style.paddingBottom = 0;
                            lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                        }
                    }
                }
            });
            
            // Profile info container
            var infoContainer = new VisualElement();
            infoContainer.style.flexDirection = FlexDirection.Row;
            infoContainer.style.alignItems = Align.Center;

            var infoText = $"v{profile.version}";
            if (profile.foldersToExport.Count > 0)
            {
                infoText += $" • {profile.foldersToExport.Count} folder(s)";
            }
            
            var infoLabel = new Label(infoText);
            infoLabel.AddToClassList("yucp-profile-item-info");
            infoContainer.Add(infoLabel);

            // Folder association
            if (!string.IsNullOrEmpty(profile.folderName))
            {
                item.AddToClassList("yucp-profile-item-in-folder");

                var separatorLabel = new Label(" • ");
                separatorLabel.AddToClassList("yucp-profile-item-info");
                infoContainer.Add(separatorLabel);
                
                var folderIcon = new Image();
                folderIcon.image = EditorGUIUtility.IconContent("Folder Icon").image;
                folderIcon.style.width = 12;
                folderIcon.style.height = 12;
                folderIcon.style.marginRight = 2;
                folderIcon.style.marginLeft = 0;
                folderIcon.style.alignSelf = Align.Center;
                // In USS usually info text is #888 or similar in dark mode.
                folderIcon.tintColor = new Color(0.6f, 0.6f, 0.6f, 1f); 
                infoContainer.Add(folderIcon);

                var folderNameLabel = new Label(profile.folderName);
                folderNameLabel.AddToClassList("yucp-profile-item-info");
                infoContainer.Add(folderNameLabel);
            }
            else
            {
                item.AddToClassList("yucp-profile-item-root");
            }

            contentColumn.Add(infoContainer);
            
            // Tags moved to title line
            
            item.Add(contentColumn);
            
            // Drag-and-drop handlers - entire item is draggable
            item.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0) // Left click
                {
                    // Initialize drag state
                    draggingIndex = index;
                    draggingElement = item;
                    dragOffset = evt.localMousePosition;
                    dragStartPosition = evt.mousePosition;
                    hasDragged = false;
                    
                    // Clean state - no gaps, no transforms
                    var listContainer = item.parent;
                    if (listContainer != null)
                    {
                        // Clear any existing gaps
                        rowGaps.Clear();
                    }
                    
                    item.CaptureMouse();
                    evt.StopPropagation();
                }
                else if (evt.button == 1) // Right click
                {
                    ShowProfileContextMenu(profile, index, evt);
                    evt.StopPropagation();
                }
            });
            
            
            // Drag handlers on the item
            item.RegisterCallback<MouseMoveEvent>(evt =>
            {
                if (draggingIndex == index && item.HasMouseCapture())
                {
                    // Check if we've moved enough to consider it a drag
                    float dragDistance = Vector2.Distance(evt.mousePosition, dragStartPosition);
                    if (dragDistance > DRAG_THRESHOLD && !hasDragged)
                    {
                        // Start the drag - vFavorites approach: remove from layout, position absolutely
                        hasDragged = true;
                        item.AddToClassList("yucp-profile-item-dragging");
                        
                        var listContainer = item.parent;
                        if (listContainer != null)
                        {
                            // Store original position in world space
                            Rect originalRect = item.worldBound;
                            draggedItemY = originalRect.y;
                            
                            // Remove from hierarchy and create gap (vFavorites approach)
                            float itemHeight = item.resolvedStyle.height;
                            if (itemHeight <= 0) itemHeight = 60f;
                            float itemMargin = item.resolvedStyle.marginBottom;
                            if (itemMargin <= 0) itemMargin = 2f;
                            
                            // Store original index (vFavorites initFromPage approach)
                            draggingItemFromPageAtIndex = index;
                            
                            // Calculate draggedItemHoldOffset - no longer needed with element-based calculation
                            // But keeping for potential future use
                            // draggedItemHoldOffset = 0f;
                            
                            // Determine visual index of the dragged item (folders + root profiles)
                            draggingVisualIndex = 0;
                            int visualIndex = 0;
                            foreach (var child in listContainer.Children())
                            {
                                if (child.ClassListContains("yucp-folder-content")) continue;
                                
                                bool isFolderHeader = child.ClassListContains("yucp-folder-header");
                                bool isRootProfile = false;
                                
                                if (child.ClassListContains("yucp-profile-item") && child.userData is int profileIdx)
                                {
                                    var childProfile = allProfiles[profileIdx];
                                    isRootProfile = string.IsNullOrEmpty(childProfile.folderName) || !projectFolders.Contains(childProfile.folderName);
                                }
                                
                                if (!isFolderHeader && !isRootProfile)
                                {
                                    continue;
                                }
                                
                                if (child == item)
                                {
                                    draggingVisualIndex = visualIndex;
                                    break;
                                }
                                
                                visualIndex++;
                            }
                            
                            // Remove from layout (vFavorites approach)
                            item.RemoveFromHierarchy();
                            
                            // Set gap at original visual position
                            float gapSize = itemHeight + itemMargin;
                            rowGaps[draggingVisualIndex] = gapSize;
                            
                            // Initialize insert index to the original visual index
                            insertDraggedItemAtIndex = draggingVisualIndex;
                            
                            // Reset all other items' transforms before starting drag
                            foreach (var child in listContainer.Children())
                            {
                                if (motionHandles.ContainsKey(child))
                                {
                                    var handle = motionHandles[child];
                                    handle.Animate(new MotionTargets
                                    {
                                        HasY = true,
                                        Y = 0f
                                    }, new Transition(0f, EasingType.Linear)); // Instant reset
                                }
                            }
                            
                            // Create container for absolutely positioned dragged item
                            draggedItemContainer = new VisualElement();
                            draggedItemContainer.AddToClassList("yucp-profile-item-dragging-container");
                            draggedItemContainer.style.position = Position.Absolute;
                            draggedItemContainer.style.left = originalRect.x;
                            draggedItemContainer.style.top = originalRect.y;
                            draggedItemContainer.style.width = originalRect.width;
                            draggedItemContainer.style.height = originalRect.height;
                            draggedItemContainer.pickingMode = PickingMode.Ignore;
                            
                            // Add item to container
                            draggedItemContainer.Add(item);
                            
                            // Add container to root (overlay everything)
                            rootVisualElement.Add(draggedItemContainer);
                            
                            // Apply scale for visual feedback
                            item.style.scale = new Scale(new Vector2(0.95f, 0.95f));
                        }
                    }
                    
                    if (hasDragged)
                    {
                        // Move the dragged item absolutely (vFavorites approach)
                        if (draggedItemContainer != null)
                        {
                            // Calculate target position from mouse (screen space)
                            Vector2 targetPos = evt.mousePosition - dragOffset;
                            draggedItemY = targetPos.y;
                            
                            // Update container position (absolute positioning)
                            draggedItemContainer.style.top = draggedItemY;
                            draggedItemContainer.style.left = targetPos.x;
                            
                            Vector2 mousePos = evt.mousePosition;
                            
                            // Get list container
                            var listContainer = _profileListContainer;
                            if (listContainer == null)
                            {
                                // Try to find it from root
                                listContainer = rootVisualElement.Q<VisualElement>(className: "yucp-profile-list-container");
                            }
                            
                            if (listContainer != null)
                            {
                                // Find folder target or visual drop index (folders + root profiles)
                                VisualElement folderTarget = null;
                                int newDropIndex = CalculateDropIndexWithFolders(listContainer, mousePos, index, out folderTarget);
                                
                                if (folderTarget != null)
                                {
                                    if (insertDraggedItemAtIndex != -1)
                                    {
                                        insertDraggedItemAtIndex = -1;
                                    }
                                }
                                else if (newDropIndex >= 0 && newDropIndex != insertDraggedItemAtIndex)
                                {
                                    insertDraggedItemAtIndex = newDropIndex;
                                }
                                
                                if (newDropIndex != potentialDropIndex)
                                {
                                    potentialDropIndex = newDropIndex;
                                }
                                
                                // Update visual feedback for drop targets
                                if (folderTarget != null)
                                {
                                    UpdateFolderDropTargets(listContainer, folderTarget);
                                    if (potentialDropIndex >= 0) ClearDropTargets(listContainer);
                                    ResetStackState();
                                }
                                else
                                {
                                    UpdateFolderDropTargets(listContainer, null);
                                    UpdateDropTargets(listContainer, potentialDropIndex, item);
                                    
                                    // Stack detection: find profile item under cursor
                                    VisualElement hitElement = listContainer.panel.Pick(mousePos);
                                    VisualElement targetItem = null;
                                    int targetIdx = -1;
                                    
                                    var current = hitElement;
                                    while (current != null && current != listContainer)
                                    {
                                        if (current.ClassListContains("yucp-profile-item") && current.userData is int idx && idx != index)
                                        {
                                            targetItem = current;
                                            targetIdx = idx;
                                            break;
                                        }
                                        current = current.parent;
                                    }
                                    
                                    if (targetItem != null && targetIdx >= 0)
                                    {
                                        if (stackTargetElement == targetItem)
                                        {
                                            float dist = Vector2.Distance(mousePos, stackHoverPosition);
                                            if (dist <= STACK_POSITION_TOLERANCE)
                                            {
                                                double elapsed = EditorApplication.timeSinceStartup - stackHoverStartTime;
                                                if (elapsed >= STACK_HOVER_THRESHOLD && !isStackReady)
                                                {
                                                    isStackReady = true;
                                                    targetItem.AddToClassList("yucp-profile-item-stack-target");
                                                }
                                            }
                                            else
                                            {
                                                stackHoverStartTime = EditorApplication.timeSinceStartup;
                                                stackHoverPosition = mousePos;
                                                isStackReady = false;
                                                targetItem.RemoveFromClassList("yucp-profile-item-stack-target");
                                            }
                                        }
                                        else
                                        {
                                            ResetStackState();
                                            stackTargetElement = targetItem;
                                            stackTargetIndex = targetIdx;
                                            stackHoverStartTime = EditorApplication.timeSinceStartup;
                                            stackHoverPosition = mousePos;
                                        }
                                    }
                                    else
                                    {
                                        ResetStackState();
                                    }
                                }
                            }
                        }
                    }
                    
                    evt.StopPropagation();
                }
            });
            
            item.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (draggingIndex == index && item.HasMouseCapture())
                {
                    item.ReleaseMouse();
                    
                    if (hasDragged)
                    {
                        // We were dragging - handle drop (vFavorites approach)
                        item.RemoveFromClassList("yucp-profile-item-dragging");
                        
                        // Get list container
                        var listContainer = _profileListContainer;
                        if (listContainer == null)
                        {
                            listContainer = rootVisualElement.Q<VisualElement>(className: "yucp-profile-list-container");
                        }
                        
                        if (listContainer != null)
                        {
                            // Use insertDraggedItemAtIndex (vFavorites AcceptDragging approach)
                            int targetIndex = insertDraggedItemAtIndex >= 0 ? insertDraggedItemAtIndex : index;
                            
                            // Check for folder drops
                            VisualElement hitElement = listContainer.panel.Pick(evt.mousePosition);
                             // Traverse up to find folder header
                             VisualElement folderHeader = null;
                             var current = hitElement;
                             while(current != null && current != listContainer) {
                                 if (current.ClassListContains("yucp-folder-header")) {
                                     folderHeader = current;
                                     break;
                                 }
                                 current = current.parent;
                             }

                            if (folderHeader != null && folderHeader.userData is string fName && fName != profile.folderName)
                            {
                                 // Dropped onto a different folder
                                 MoveProfileToFolder(profile, fName);
                            }
                            else if (isStackReady && stackTargetIndex >= 0 && stackTargetIndex < allProfiles.Count)
                            {
                                // Stack-to-folder: create a new folder with both profiles
                                var targetProfile = allProfiles[stackTargetIndex];
                                CreateFolderWithProfiles(profile, targetProfile);
                            }
                            else
                            {
                                // Perform reorder on drop (vFavorites AcceptDragging: insert at insertDraggedItemAtIndex)
                                // insertDraggedItemAtIndex is the visual index (0-based position in visible list excluding dragged item)
                                // In vFavorites: data.curPage.items.AddAt(draggedItem, insertDraggedItemAtIndex)
                                // The items list already has the dragged item removed, so insertDraggedItemAtIndex is the position in that list
                                
                                if (insertDraggedItemAtIndex >= 0 && insertDraggedItemAtIndex != draggingVisualIndex)
                                {
                                    ReorderProfiles(draggingItemFromPageAtIndex, insertDraggedItemAtIndex);
                                }
                            }
                            
                            // Adjust rowGaps (vFavorites: data.curPage.rowGaps[insertDraggedItemAtIndex] -= rowHeight; data.curPage.rowGaps.AddAt(0, insertDraggedItemAtIndex))
                            if (rowGaps.ContainsKey(targetIndex))
                            {
                                float rowHeight = draggingElement != null ? draggingElement.resolvedStyle.height : 60f;
                                if (rowHeight <= 0) rowHeight = 60f;
                                rowGaps[targetIndex] = Mathf.Max(0f, rowGaps[targetIndex] - rowHeight);
                            }
                            
                            // Reset stack state
                            ResetStackState();
                            
                            // Clear all visual feedback
                            ClearDropTargets(listContainer);
                        }
                        
                        // Clean up dragged item container (vFavorites approach)
                        if (draggedItemContainer != null)
                        {
                            // Remove item from container
                            item.RemoveFromHierarchy();
                            draggedItemContainer.RemoveFromHierarchy();
                            draggedItemContainer = null;
                            
                            // Reset scale
                            item.style.scale = new Scale(Vector2.one);
                            
                            // Clear gaps and reset all transforms
                            rowGaps.Clear();
                            draggingVisualIndex = -1;
                            draggingItemFromPageAtIndex = -1;
                            insertDraggedItemAtIndex = -1;
                            // draggedItemHoldOffset = 0f;
                            
                            // Reset all item transforms before refreshing
                            if (listContainer != null)
                            {
                                foreach (var child in listContainer.Children())
                                {
                                    if (motionHandles.ContainsKey(child))
                                    {
                                        var handle = motionHandles[child];
                                        handle.Animate(new MotionTargets
                                        {
                                            HasY = true,
                                            Y = 0f
                                        }, new Transition(0.2f, EasingType.EaseOut));
                                    }
                                }
                            }
                            
                            // Refresh list to show item in new position
                            UpdateProfileList();
                        }
                    }
                    else
                    {
                        // It was just a click, not a drag - handle selection
                        // Create a MouseDownEvent-like event for HandleProfileSelection
                        // We'll handle selection directly here since we know it's a click
                        if (evt.ctrlKey || evt.commandKey)
                        {
                            // Toggle selection
                            if (selectedProfileIndices.Contains(index))
                            {
                                selectedProfileIndices.Remove(index);
                                if (selectedProfile == profile)
                                {
                                    selectedProfile = null;
                                }
                            }
                            else
                            {
                                selectedProfileIndices.Add(index);
                                selectedProfile = profile;
                            }
                            lastClickedProfileIndex = index;
                        }
                        else if (evt.shiftKey)
                        {
                            // Range selection
                            if (lastClickedProfileIndex >= 0 && lastClickedProfileIndex < allProfiles.Count)
                            {
                                int start = Math.Min(lastClickedProfileIndex, index);
                                int end = Math.Max(lastClickedProfileIndex, index);
                                selectedProfileIndices.Clear();
                                for (int i = start; i <= end; i++)
                                {
                                    selectedProfileIndices.Add(i);
                                }
                                selectedProfile = profile;
                            }
                            else
                            {
                                selectedProfileIndices.Clear();
                                selectedProfileIndices.Add(index);
                                selectedProfile = profile;
                            }
                            lastClickedProfileIndex = index;
                        }
                        else
                        {
                            // Single selection
                            selectedProfileIndices.Clear();
                            selectedProfileIndices.Add(index);
                            selectedProfile = profile;
                            lastClickedProfileIndex = index;
                        }
                        
                        UpdateProfileList();
                        UpdateProfileDetails();
                        UpdateBottomBar();
                    }
                    
                    // Clean up drag state
                    if (draggedItemContainer != null)
                    {
                        draggedItemContainer.RemoveFromHierarchy();
                        draggedItemContainer = null;
                    }
                    
                            // Clear gaps and drag state (vFavorites CancelDragging approach)
                            rowGaps.Clear();
                            draggingItemFromPageAtIndex = -1;
                            insertDraggedItemAtIndex = -1;
                            // draggedItemHoldOffset = 0f;
                            draggingVisualIndex = -1;
                    
                    draggingIndex = -1;
                    draggingElement = null;
                    hasDragged = false;
                    potentialDropIndex = -1;
                    evt.StopPropagation();
                }
            });
            
            // Click handler with multi-selection support (only if not dragging)
            // We'll handle selection in MouseDownEvent, but only if we're not starting a drag
            // The drag logic will prevent selection when dragging starts
            
            return item;
        }



    }
}
