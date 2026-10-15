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

                    // Add click handler to add tag to search (Alt+click excludes it instead)
                    chip.tooltip = "Click to filter by this tag, Alt+Click to exclude it, Right-click for color";
                    chip.RegisterCallback<MouseDownEvent>(evt =>
                    {
                        if (evt.button == 0) // Left click
                        {
                            string filterTag = evt.altKey ? "-" + tag : tag;
                            _mainSearchField?.AddTag(filterTag);
                            _overlaySearchField?.AddTag(filterTag);
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
                    rowGaps.Clear();

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
                if (draggingIndex != index || !item.HasMouseCapture()) return;

                // Check if we've moved enough to consider it a drag
                if (!hasDragged && Vector2.Distance(evt.mousePosition, dragStartPosition) > DRAG_THRESHOLD)
                {
                    // Start the drag - vFavorites approach: lift the row out of the list and float it at the cursor
                    var listContainer = GetListRoot(item);
                    if (listContainer != null)
                    {
                        hasDragged = true;
                        item.AddToClassList("yucp-profile-item-dragging");
                        _draggingListContainer = listContainer; // Use this container for entire drag (overlay vs main)

                        Rect originalRect = item.worldBound;
                        float itemHeight = item.resolvedStyle.height;
                        if (itemHeight <= 0) itemHeight = 60f;
                        float itemMargin = item.resolvedStyle.marginBottom;
                        if (itemMargin <= 0) itemMargin = 2f;

                        // Keep the row's slot open where it was (gap indices follow GetListRows)
                        draggingVisualIndex = GetListRows(listContainer).IndexOf(item);
                        item.RemoveFromHierarchy();
                        rowGaps[draggingVisualIndex] = itemHeight + itemMargin;
                        insertDraggedItemAtIndex = draggingVisualIndex;

                        // Create container for absolutely positioned dragged item
                        draggedItemContainer = new VisualElement();
                        draggedItemContainer.AddToClassList("yucp-profile-item-dragging-container");
                        draggedItemContainer.style.position = Position.Absolute;
                        draggedItemContainer.style.left = originalRect.x;
                        draggedItemContainer.style.top = originalRect.y;
                        draggedItemContainer.style.width = originalRect.width;
                        draggedItemContainer.style.height = originalRect.height;
                        draggedItemContainer.pickingMode = PickingMode.Ignore;
                        draggedItemContainer.Add(item);

                        // Add container to root (overlay everything)
                        rootVisualElement.Add(draggedItemContainer);

                        // Apply scale for visual feedback
                        item.style.scale = new Scale(new Vector2(0.95f, 0.95f));
                    }
                }

                if (hasDragged && draggedItemContainer != null)
                {
                    // Move the dragged item absolutely (vFavorites approach)
                    Vector2 targetPos = evt.mousePosition - dragOffset;
                    draggedItemContainer.style.top = targetPos.y;
                    draggedItemContainer.style.left = targetPos.x;

                    Vector2 mousePos = evt.mousePosition;
                    var listContainer = _draggingListContainer;
                    var rows = GetListRows(listContainer);

                    // Drop slot (or folder to drop into) under the cursor
                    var dropRow = FindDropRow(listContainer, mousePos, true, out bool after, out var folderTarget);
                    insertDraggedItemAtIndex = folderTarget != null ? -1 : GetGapIndex(rows, dropRow, after);
                    UpdateFolderDropTargets(listContainer, folderTarget);
                    UpdateDropTargets(listContainer, insertDraggedItemAtIndex);

                    // Stack detection: resting on another profile readies "group both into a new folder".
                    // Bounds, not panel.Pick: the floating dragged row is what sits under the cursor.
                    var targetItem = folderTarget == null
                        ? rows.FirstOrDefault(r => r.userData is int && r.worldBound.Contains(mousePos))
                        : null;

                    if (targetItem == null)
                    {
                        ResetStackState();
                    }
                    else if (stackTargetElement != targetItem || Vector2.Distance(mousePos, stackHoverPosition) > STACK_POSITION_TOLERANCE)
                    {
                        ResetStackState();
                        stackTargetElement = targetItem;
                        stackTargetIndex = (int)targetItem.userData;
                        stackHoverPosition = mousePos;
                        double started = stackHoverStartTime = EditorApplication.timeSinceStartup;

                        // Timer instead of waiting for the next mouse move, so holding still is enough
                        item.schedule.Execute(() =>
                        {
                            if (!hasDragged || stackTargetElement != targetItem || stackHoverStartTime != started) return;
                            isStackReady = true;
                            targetItem.AddToClassList("yucp-profile-item-stack-target");
                        }).StartingIn((long)(STACK_HOVER_THRESHOLD * 1000));
                    }
                }

                evt.StopPropagation();
            });

            item.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (draggingIndex != index || !item.HasMouseCapture()) return;
                item.ReleaseMouse();

                bool wasDragged = hasDragged;
                if (wasDragged)
                {
                    // We were dragging - handle drop (vFavorites approach)
                    item.RemoveFromClassList("yucp-profile-item-dragging");
                    item.style.scale = new Scale(Vector2.one);

                    var listContainer = _draggingListContainer;
                    if (listContainer != null)
                    {
                        var dropRow = FindDropRow(listContainer, evt.mousePosition, true, out bool after, out var folderHeader);
                        ClearDropTargets(listContainer);
                        UpdateFolderDropTargets(listContainer, null);

                        if (folderHeader != null)
                        {
                            // Dropped onto a folder
                            if (folderHeader.userData is string folderName && folderName != profile.folderName)
                                MoveProfileToFolder(profile, folderName);
                        }
                        else if (isStackReady && stackTargetIndex >= 0 && stackTargetIndex < allProfiles.Count)
                        {
                            // Stack-to-folder: create a new folder with both profiles
                            CreateFolderWithProfiles(profile, allProfiles[stackTargetIndex]);
                        }
                        else
                        {
                            // Manual order only exists in the Custom view
                            currentSortOption = ProfileSortOption.Custom;
                            MoveProfileNextTo(profile, dropRow, after);
                        }
                    }

                    ResetStackState();
                }
                else
                {
                    // It was just a click, not a drag - handle selection
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
                        // Range selection in on-screen order. The anchor stays put so repeated shift-clicks pivot on it.
                        if (lastClickedProfileIndex < 0) lastClickedProfileIndex = index;
                        SelectDisplayedRange(lastClickedProfileIndex, index);
                        selectedProfile = profile;
                    }
                    else
                    {
                        // Single selection
                        selectedProfileIndices.Clear();
                        selectedProfileIndices.Add(index);
                        selectedProfile = profile;
                        lastClickedProfileIndex = index;
                    }

                    UpdateProfileDetails();
                    UpdateBottomBar();
                }

                // Clean up drag state (vFavorites CancelDragging approach)
                if (draggedItemContainer != null)
                {
                    draggedItemContainer.RemoveFromHierarchy();
                    draggedItemContainer = null;
                }
                rowGaps.Clear();
                insertDraggedItemAtIndex = -1;
                draggingVisualIndex = -1;
                draggingIndex = -1;
                draggingElement = null;
                hasDragged = false;
                _draggingListContainer = null;

                // Re-render: restores the lifted row and shows the new selection/position
                UpdateProfileList();
                evt.StopPropagation();
            });
            return item;
        }



    }
}
