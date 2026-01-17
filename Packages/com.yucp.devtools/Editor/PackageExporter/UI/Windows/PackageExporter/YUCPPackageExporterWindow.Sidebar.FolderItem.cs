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
        private VisualElement CreateFolderHeader(string folderName, int count)
        {
            bool isCollapsed = collapsedFolders.Contains(folderName);
            
            var header = new VisualElement();
            header.AddToClassList("yucp-folder-header");
            header.AddToClassList("yucp-profile-item"); // Reuse profile item styling
            header.userData = folderName; // Identifier for drag-drop targets
            
            // Check if renaming
            if (folderName == folderBeingRenamed)
            {
                // Icon container with folder icon
                var iconContainer = new VisualElement();
                iconContainer.AddToClassList("yucp-profile-item-icon-container");
                iconContainer.AddToClassList("yucp-folder-icon-container");
                
                var folderIcon = new Image();
                folderIcon.image = EditorGUIUtility.IconContent("Folder Icon").image;
                folderIcon.AddToClassList("yucp-folder-icon");
                iconContainer.Add(folderIcon);
                header.Add(iconContainer);
                
                // Render text field for renaming
                var textField = new TextField();
                textField.value = folderName;
                textField.style.flexGrow = 1;
                textField.style.marginRight = 8;
                textField.AddToClassList("yucp-folder-rename-field");
                
                textField.RegisterCallback<KeyDownEvent>(evt => 
                {
                    if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    {
                        EndRenameFolder(folderName, textField.value);
                        evt.StopPropagation();
                    }
                    else if (evt.keyCode == KeyCode.Escape)
                    {
                        CancelRenameFolder();
                        evt.StopPropagation();
                    }
                });
                
                textField.RegisterCallback<FocusOutEvent>(evt => 
                {
                    EndRenameFolder(folderName, textField.value);
                });
                
                header.Add(textField);
                
                // Focus and select all
                header.schedule.Execute(() => 
                {
                    textField.Focus();
                    textField.SelectAll();
                });
                
                return header;
            }
            
            // Drag-and-drop support for folders
            header.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0) // Left click
                {
                    // Initialize drag state for folder
                    draggingIndex = -1; // Use -1 to indicate folder drag
                    draggingElement = header;
                    dragOffset = evt.localMousePosition;
                    dragStartPosition = evt.mousePosition;
                    hasDragged = false;
                    
                    header.CaptureMouse();
                    evt.StopPropagation();
                }
                else if (evt.button == 1) // Right click
                {
                    ShowFolderContextMenu(folderName);
                    evt.StopPropagation();
                }
            });
            
            // Drag handlers for folder - using vFavorites approach (same as profile items)
            header.RegisterCallback<MouseMoveEvent>(evt =>
            {
                if (draggingIndex == -1 && draggingElement == header && header.HasMouseCapture())
                {
                    float dragDistance = Vector2.Distance(evt.mousePosition, dragStartPosition);
                    if (dragDistance > DRAG_THRESHOLD && !hasDragged)
                    {
                        // Start the drag - vFavorites approach: remove from layout, position absolutely
                        hasDragged = true;
                        header.AddToClassList("yucp-profile-item-dragging");
                        
                        var listContainer = header.parent;
                        if (listContainer != null)
                        {
                            // Store original position in world space
                            Rect originalRect = header.worldBound;
                            draggedItemY = originalRect.y;
                            
                            // Remove from hierarchy and create gap (vFavorites approach)
                            float itemHeight = header.resolvedStyle.height;
                            if (itemHeight <= 0) itemHeight = 60f;
                            float itemMargin = header.resolvedStyle.marginBottom;
                            if (itemMargin <= 0) itemMargin = 2f;
                            
                            // Store folder name for reordering
                            draggingItemFromPageAtIndex = -1; // -1 indicates folder
                            
                            // draggedItemHoldOffset = 0f;
                            
                            // Remove from layout (vFavorites approach)
                            header.RemoveFromHierarchy();
                            
                            // Determine visual index of the dragged folder
                            // Get all items (profiles and folders) in visual order
                            var allItems = new List<VisualElement>();
                            foreach (var child in listContainer.Children())
                            {
                                if (child.ClassListContains("yucp-folder-content")) continue;
                                if (child.ClassListContains("yucp-profile-item") || child.ClassListContains("yucp-folder-header"))
                                {
                                    allItems.Add(child);
                                }
                            }
                            allItems = allItems.OrderBy(el => el.worldBound.y).ToList();
                            
                            draggingVisualIndex = allItems.IndexOf(header);
                            if (draggingVisualIndex < 0) draggingVisualIndex = 0;
                            
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
                            
                            // Create container for absolutely positioned dragged folder
                            draggedItemContainer = new VisualElement();
                            draggedItemContainer.AddToClassList("yucp-profile-item-dragging-container");
                            draggedItemContainer.style.position = Position.Absolute;
                            draggedItemContainer.style.left = originalRect.x;
                            draggedItemContainer.style.top = originalRect.y;
                            draggedItemContainer.style.width = originalRect.width;
                            draggedItemContainer.style.height = originalRect.height;
                            draggedItemContainer.pickingMode = PickingMode.Ignore;
                            
                            header.style.position = Position.Absolute;
                            header.style.left = 0;
                            header.style.top = 0;
                            header.style.width = Length.Percent(100);
                            
                            draggedItemContainer.Add(header);
                            rootVisualElement.Add(draggedItemContainer);
                            
                            // Visual feedback
                            header.style.scale = new Scale(new Vector2(0.95f, 0.95f));
                        }
                    }
                    
                    if (hasDragged)
                    {
                        Vector2 mousePos = evt.mousePosition;
                        
                        // Update dragged folder position to follow mouse
                        if (draggedItemContainer != null)
                        {
                            draggedItemContainer.style.left = mousePos.x - dragOffset.x;
                            draggedItemContainer.style.top = mousePos.y - dragOffset.y;
                        }
                        
                        var listContainer = _profileListContainer;
                        if (listContainer != null)
                        {
                            // Calculate insertDraggedItemAtIndex for folders (same logic as profiles)
                            var allItems = new List<VisualElement>();
                            foreach (var child in listContainer.Children())
                            {
                                if (child.ClassListContains("yucp-folder-content")) continue;
                                if (child.ClassListContains("yucp-profile-item") || child.ClassListContains("yucp-folder-header"))
                                {
                                    if (child != header) // Exclude dragged folder
                                    {
                                        allItems.Add(child);
                                    }
                                }
                            }
                            allItems = allItems.OrderBy(el => el.worldBound.y).ToList();
                            
                            int newInsertIndex = allItems.Count; // Default to end
                            
                            if (allItems.Count > 0)
                            {
                                // Check if mouse is above the first item's top edge
                                var firstItem = allItems[0];
                                Rect firstWorld = firstItem.worldBound;
                                
                                if (mousePos.y < firstWorld.y)
                                {
                                    // Mouse is above first item - insert at position 0
                                    newInsertIndex = 0;
                                }
                                else
                                {
                                    // Find insertion point by comparing with each item's center
                                    for (int i = 0; i < allItems.Count; i++)
                                    {
                                        var child = allItems[i];
                                        Rect childWorld = child.worldBound;
                                        float childCenterY = childWorld.y + childWorld.height / 2f;
                                        
                                        // If mouse is above center of this child, insert before it
                                        if (mousePos.y < childCenterY)
                                        {
                                            newInsertIndex = i;
                                            break;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // No items, insert at 0
                                newInsertIndex = 0;
                            }
                            
                            // Clamp to valid range
                            newInsertIndex = Mathf.Clamp(newInsertIndex, 0, allItems.Count);
                            
                            if (newInsertIndex != insertDraggedItemAtIndex)
                            {
                                insertDraggedItemAtIndex = newInsertIndex;
                            }
                            
                            // Calculate drop index for folder reordering
                            int newDropIndex = CalculateDropIndexForFolder(listContainer, mousePos, folderName);
                            
                            if (newDropIndex != potentialDropIndex)
                            {
                                potentialDropIndex = newDropIndex;
                            }
                            
                            UpdateDropTargets(listContainer, potentialDropIndex, header);
                        }
                        
                        evt.StopPropagation();
                    }
                }
            });
            
            header.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (draggingIndex == -1 && draggingElement == header && header.HasMouseCapture())
                {
                    header.ReleaseMouse();
                    
                    if (hasDragged)
                    {
                        header.RemoveFromClassList("yucp-profile-item-dragging");
                        header.style.scale = new Scale(Vector2.one);
                        
                        var listContainer = _profileListContainer;
                        if (listContainer != null)
                        {
                            int targetIndex = potentialDropIndex >= 0 ? potentialDropIndex : -1;
                            
                            if (targetIndex >= 0)
                            {
                                ReorderFolder(folderName, targetIndex);
                            }
                            
                            ClearDropTargets(listContainer);
                        }
                        
                        // Clean up drag state (vFavorites approach)
                        draggingItemFromPageAtIndex = -1;
                        insertDraggedItemAtIndex = -1;
                        // draggedItemHoldOffset = 0f;
                        draggingVisualIndex = -1;
                        
                        // Remove dragged item container
                        if (draggedItemContainer != null)
                        {
                            draggedItemContainer.RemoveFromHierarchy();
                            draggedItemContainer = null;
                        }
                        
                        // Clear gaps and reset transforms
                        rowGaps.Clear();
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
                            child.style.marginTop = 0f;
                        }
                        
                        UpdateProfileList();
                    }
                    else
                    {
                        // It was just a click - toggle collapse
                        if (isCollapsed)
                            collapsedFolders.Remove(folderName);
                        else
                            collapsedFolders.Add(folderName);
                            
                        SaveCollapsedFolders();
                        UpdateProfileList();
                    }
                    
                    draggingIndex = -1;
                    draggingElement = null;
                    hasDragged = false;
                    potentialDropIndex = -1;
                    evt.StopPropagation();
                }
            });
            
            // Icon container with folder icon (like profile item icon)
            var mainIconContainer = new VisualElement();
            mainIconContainer.AddToClassList("yucp-profile-item-icon-container");
            mainIconContainer.AddToClassList("yucp-folder-icon-container");
            
            var mainFolderIcon = new Image();
            mainFolderIcon.image = EditorGUIUtility.IconContent(isCollapsed ? "Folder Icon" : "FolderOpened Icon").image;
            mainFolderIcon.AddToClassList("yucp-folder-icon");
            mainIconContainer.Add(mainFolderIcon);
            header.Add(mainIconContainer);
            
            // Content column (like profile item content)
            var contentColumn = new VisualElement();
            contentColumn.AddToClassList("yucp-profile-item-content");
            contentColumn.style.flexGrow = 1;
            
            // Folder name
            var nameLabel = new Label(folderName);
            nameLabel.AddToClassList("yucp-profile-item-name");
            contentColumn.Add(nameLabel);
            
            // Folder info
            var infoText = $"{count} profile{(count != 1 ? "s" : "")}";
            var infoLabel = new Label(infoText);
            infoLabel.AddToClassList("yucp-profile-item-info");
            contentColumn.Add(infoLabel);
            
            header.Add(contentColumn);
            
            // Chevron indicator
            var chevron = new Image();
            chevron.AddToClassList("yucp-folder-chevron");
            var chevronIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                isCollapsed 
                ? "Packages/com.yucp.components/Resources/Icons/Nucleo/chevron-right-small.png"
                : "Packages/com.yucp.components/Resources/Icons/Nucleo/chevron-down-small.png");
            if (chevronIcon != null)
            {
                chevron.image = chevronIcon;
            }
            else
            {
                // Fallback to built-in
                chevron.image = EditorGUIUtility.IconContent(isCollapsed ? "d_forward" : "d_dropdown").image;
            }
            header.Add(chevron);
            
            return header;
        }

    }
}
