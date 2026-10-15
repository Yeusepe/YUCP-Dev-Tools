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
                bool focusEverReceived = false;
                
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

                textField.RegisterCallback<FocusInEvent>(_ =>
                {
                    focusEverReceived = true;
                });
                
                textField.RegisterCallback<FocusOutEvent>(evt => 
                {
                    // When invoking rename from a context menu, Unity often fires a focus-out immediately
                    // as the menu closes. Debounce that initial focus churn and re-focus the field.
                    double sinceStart = folderRenameStartTime >= 0
                        ? (EditorApplication.timeSinceStartup - folderRenameStartTime)
                        : double.MaxValue;
                    
                    if (sinceStart < 0.25d)
                    {
                        // Try to reclaim focus on the next tick once the context menu fully closes.
                        header.schedule.Execute(() =>
                        {
                            if (folderBeingRenamed == folderName && textField.panel != null)
                            {
                                textField.Focus();
                                textField.SelectAll();
                            }
                        }).StartingIn(1);
                        return;
                    }
                    
                    // Only commit if the field actually had focus at least once. This avoids "instant commit"
                    // if focus was never successfully applied.
                    if (focusEverReceived && folderBeingRenamed == folderName)
                    {
                        EndRenameFolder(folderName, textField.value);
                    }
                });
                
                header.Add(textField);
                
                // Focus and select all
                header.schedule.Execute(() =>
                {
                    if (folderBeingRenamed == folderName && textField.panel != null)
                    {
                        textField.Focus();
                        textField.SelectAll();
                    }
                }).StartingIn(1);
                
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
                if (draggingIndex != -1 || draggingElement != header || !header.HasMouseCapture()) return;

                if (!hasDragged && Vector2.Distance(evt.mousePosition, dragStartPosition) > DRAG_THRESHOLD)
                {
                    // Start the drag - vFavorites approach: lift the header out of the list and float it at the cursor
                    var listContainer = header.parent;
                    if (listContainer != null)
                    {
                        hasDragged = true;
                        header.AddToClassList("yucp-profile-item-dragging");
                        _draggingListContainer = listContainer; // Use this container for entire drag (overlay vs main)

                        Rect originalRect = header.worldBound;
                        float itemHeight = header.resolvedStyle.height;
                        if (itemHeight <= 0) itemHeight = 60f;
                        float itemMargin = header.resolvedStyle.marginBottom;
                        if (itemMargin <= 0) itemMargin = 2f;

                        // Keep the header's slot open where it was (gap indices follow GetListRows)
                        draggingVisualIndex = GetListRows(listContainer).IndexOf(header);
                        header.RemoveFromHierarchy();
                        rowGaps[draggingVisualIndex] = itemHeight + itemMargin;
                        insertDraggedItemAtIndex = draggingVisualIndex;

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

                    var listContainer = _draggingListContainer;
                    if (listContainer != null)
                    {
                        var dropRow = FindDropRow(listContainer, mousePos, false, out bool after, out _);
                        insertDraggedItemAtIndex = GetGapIndex(GetListRows(listContainer), dropRow, after);
                        UpdateDropTargets(listContainer, insertDraggedItemAtIndex);
                    }
                }

                evt.StopPropagation();
            });

            header.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (draggingIndex != -1 || draggingElement != header || !header.HasMouseCapture()) return;
                header.ReleaseMouse();

                if (hasDragged)
                {
                    header.RemoveFromClassList("yucp-profile-item-dragging");
                    header.style.scale = new Scale(Vector2.one);

                    var listContainer = _draggingListContainer;
                    if (listContainer != null)
                    {
                        var dropRow = FindDropRow(listContainer, evt.mousePosition, false, out bool after, out _);
                        ClearDropTargets(listContainer);
                        MoveFolderNextTo(folderName, dropRow, after);
                    }

                    // Remove dragged item container
                    if (draggedItemContainer != null)
                    {
                        draggedItemContainer.RemoveFromHierarchy();
                        draggedItemContainer = null;
                    }
                }
                else
                {
                    // It was just a click - toggle collapse
                    if (isCollapsed)
                        collapsedFolders.Remove(folderName);
                    else
                        collapsedFolders.Add(folderName);

                    SaveCollapsedFolders();
                }

                // Clean up drag state (vFavorites approach)
                rowGaps.Clear();
                insertDraggedItemAtIndex = -1;
                draggingVisualIndex = -1;
                draggingIndex = -1;
                draggingElement = null;
                hasDragged = false;
                _draggingListContainer = null;

                UpdateProfileList();
                evt.StopPropagation();
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
                "Packages/com.yucp.components/Resources/Icons/Nucleo/chevron-down-small.png");
            if (chevronIcon != null)
            {
                chevron.image = chevronIcon;
            }
            else
            {
                // Fallback to built-in
                chevron.image = EditorGUIUtility.IconContent("d_dropdown").image;
            }
            chevron.style.rotate = new Rotate(Angle.Degrees(isCollapsed ? -90f : 0f));
            header.Add(chevron);
            
            return header;
        }

    }
}
