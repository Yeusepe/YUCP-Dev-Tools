using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.PackageExporter
{
    public partial class YUCPPackageExporterWindow
    {
        private void OnKeyDown(KeyDownEvent evt)
        {
            // Don't handle shortcuts if export is in progress
            if (isExporting)
                return;

            // Handle Escape key (always, regardless of focus)
            if (evt.keyCode == KeyCode.Escape)
            {
                HandleEscapeKey();
                evt.StopPropagation();
                return;
            }

            // Check if user is typing in a text field
            bool isTypingInTextField = IsTypingInTextField();
            
            // Get modifier keys
            bool modifierPressed = IsModifierPressed(evt);
            bool shiftPressed = evt.shiftKey;
            bool altPressed = evt.altKey;

            // Standard editing shortcuts (work even when typing in some cases)
            if (modifierPressed && evt.keyCode == KeyCode.Z && !shiftPressed)
            {
                // Undo (Ctrl+Z / Cmd+Z)
                Undo.PerformUndo();
                evt.StopPropagation();
                return;
            }

            if ((modifierPressed && evt.keyCode == KeyCode.Y) || 
                (modifierPressed && shiftPressed && evt.keyCode == KeyCode.Z))
            {
                // Redo (Ctrl+Y / Cmd+Shift+Z)
                Undo.PerformRedo();
                evt.StopPropagation();
                return;
            }

            if (modifierPressed && evt.keyCode == KeyCode.A && !shiftPressed && !altPressed)
            {
                // Select All (Ctrl+A / Cmd+A)
                HandleSelectAll();
                evt.StopPropagation();
                return;
            }

            // Don't handle other shortcuts when typing in text fields
            if (isTypingInTextField)
                return;

            // Profile management shortcuts
            if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
            {
                // Delete (Delete / Backspace)
                HandleDelete();
                evt.StopPropagation();
                return;
            }

            if (modifierPressed && evt.keyCode == KeyCode.D && !shiftPressed)
            {
                // Duplicate (Ctrl+D / Cmd+D)
                HandleDuplicate();
                evt.StopPropagation();
                return;
            }

            if (modifierPressed && shiftPressed && evt.keyCode == KeyCode.D)
            {
                // Clone (Ctrl+Shift+D / Cmd+Shift+D)
                HandleDuplicate();
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.F2 || (evt.keyCode == KeyCode.Return && !modifierPressed))
            {
                // Rename (F2 / Enter)
                HandleRename();
                evt.StopPropagation();
                return;
            }

            // Export shortcuts
            if (modifierPressed && evt.keyCode == KeyCode.E && !shiftPressed)
            {
                // Export Selected (Ctrl+E / Cmd+E)
                HandleExportSelected();
                evt.StopPropagation();
                return;
            }

            if (modifierPressed && shiftPressed && evt.keyCode == KeyCode.E)
            {
                // Export All (Ctrl+Shift+E / Cmd+Shift+E)
                HandleExportAll();
                evt.StopPropagation();
                return;
            }

            if (modifierPressed && evt.keyCode == KeyCode.Return)
            {
                // Quick Export Current (Ctrl+Enter / Cmd+Enter)
                HandleQuickExport();
                evt.StopPropagation();
                return;
            }

            // Navigation shortcuts
            if (modifierPressed && evt.keyCode == KeyCode.F && !shiftPressed && !altPressed)
            {
                // Focus Search (Ctrl+F / Cmd+F)
                HandleFocusSearch();
                evt.StopPropagation();
                return;
            }

            // Arrow key navigation
            if (evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow)
            {
                HandleArrowKeyNavigation(evt.keyCode == KeyCode.UpArrow, shiftPressed, modifierPressed);
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.Home || evt.keyCode == KeyCode.End)
            {
                HandleHomeEndNavigation(evt.keyCode == KeyCode.Home, shiftPressed);
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.PageUp || evt.keyCode == KeyCode.PageDown)
            {
                HandlePageUpDownNavigation(evt.keyCode == KeyCode.PageUp);
                evt.StopPropagation();
                return;
            }

            // Folder operations
            if (modifierPressed && shiftPressed && evt.keyCode == KeyCode.N)
            {
                // Create Folder (Ctrl+Shift+N / Cmd+Shift+N)
                HandleCreateFolder();
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.RightArrow || evt.keyCode == KeyCode.LeftArrow)
            {
                // Expand/Collapse Folder (Right/Left Arrow when on folder)
                HandleFolderExpandCollapse(evt.keyCode == KeyCode.RightArrow);
                evt.StopPropagation();
                return;
            }

            // Utility shortcuts
            if (modifierPressed && altPressed && evt.keyCode == KeyCode.P)
            {
                // Select in Project (Ctrl+Alt+P / Cmd+Alt+P)
                HandleSelectInProject();
                evt.StopPropagation();
                return;
            }

            if (modifierPressed && altPressed && evt.keyCode == KeyCode.E)
            {
                // Show in Explorer (Ctrl+Alt+E / Cmd+Alt+E)
                HandleShowInExplorer();
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.F5)
            {
                // Refresh (F5)
                HandleRefresh();
                evt.StopPropagation();
                return;
            }
        }

        private bool IsTypingInTextField()
        {
            var focusedElement = rootVisualElement.focusController?.focusedElement;
            return focusedElement is TextField || 
                   focusedElement is TextInputBaseField<string> ||
                   focusedElement is IMGUIContainer;
        }

        private bool IsModifierPressed(KeyDownEvent evt)
        {
            #if UNITY_EDITOR_WIN
                return evt.ctrlKey;
            #else
                return evt.commandKey;
            #endif
        }

        private void HandleEscapeKey()
        {
            // Close any open popovers/dialogs
            ClosePopover();
            
            // Close export options overlay
            if (_exportOptionsOverlay != null)
            {
                ToggleExportOptions(false);
            }
            
            // Cancel rename if in progress
            if (folderBeingRenamed != null)
            {
                folderBeingRenamed = null;
                UpdateProfileList();
            }
            
            // Clear selection
            selectedProfileIndices.Clear();
            selectedProfile = null;
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
        }

        private void HandleSelectAll()
        {
            // Get all visible profiles (respecting filters)
            var visibleIndices = GetVisibleProfileIndices();
            
            if (visibleIndices.Count == 0)
                return;
            
            selectedProfileIndices.Clear();
            foreach (var index in visibleIndices)
            {
                selectedProfileIndices.Add(index);
            }
            
            // Select the first one as the primary selection
            if (visibleIndices.Count > 0)
            {
                selectedProfile = allProfiles[visibleIndices[0]];
                lastClickedProfileIndex = visibleIndices[0];
            }
            
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
        }

        private List<int> GetVisibleProfileIndices()
        {
            var visibleIndices = new List<int>();
            
            // Apply filters to get visible profiles
            var filteredProfiles = allProfiles.AsEnumerable();
            
            // Apply search filter
            if (!string.IsNullOrWhiteSpace(_currentSearchText))
            {
                filteredProfiles = filteredProfiles.Where(p => 
                    GetProfileDisplayName(p).ToLowerInvariant().Contains(_currentSearchText.ToLowerInvariant()));
            }
            
            // Apply tag filter
            if (selectedFilterTags.Count > 0)
            {
                filteredProfiles = filteredProfiles.Where(p => 
                {
                    var profileTags = p.GetAllTags();
                    return selectedFilterTags.Any(tag => profileTags.Contains(tag));
                });
            }
            
            // Apply folder filter
            if (!string.IsNullOrEmpty(selectedFilterFolder))
            {
                filteredProfiles = filteredProfiles.Where(p => p.folderName == selectedFilterFolder);
            }
            
            // Get indices of filtered profiles
            foreach (var profile in filteredProfiles)
            {
                int index = allProfiles.IndexOf(profile);
                if (index >= 0)
                {
                    visibleIndices.Add(index);
                }
            }
            
            return visibleIndices;
        }

        private void HandleDelete()
        {
            if (selectedProfileIndices.Count == 0)
                return;
            
            var profilesToDelete = selectedProfileIndices.OrderByDescending(i => i)
                .Select(i => allProfiles[i])
                .ToList();
            
            if (profilesToDelete.Count == 0)
                return;
            
            string message = profilesToDelete.Count == 1
                ? $"Are you sure you want to delete '{GetProfileDisplayName(profilesToDelete[0])}'?"
                : $"Are you sure you want to delete {profilesToDelete.Count} profiles?";
            
            if (!EditorUtility.DisplayDialog("Delete Profile(s)", message, "Delete", "Cancel"))
                return;
            
            foreach (var profile in profilesToDelete)
            {
                DeleteProfile(profile);
            }
        }

        private void HandleDuplicate()
        {
            if (selectedProfileIndices.Count == 0)
                return;
            
            var profilesToDuplicate = selectedProfileIndices.OrderBy(i => i)
                .Select(i => allProfiles[i])
                .ToList();
            
            foreach (var profile in profilesToDuplicate)
            {
                CloneProfile(profile);
            }
        }

        private void HandleRename()
        {
            if (selectedProfileIndices.Count != 1 || selectedProfile == null)
                return;
            
            StartRenameProfile(selectedProfile);
        }

        private void HandleExportSelected()
        {
            if (selectedProfileIndices.Count == 0)
                return;
            
            ExportSelectedProfiles();
        }

        private void HandleExportAll()
        {
            if (allProfiles.Count == 0)
                return;
            
            ExportAllProfiles();
        }

        private void HandleQuickExport()
        {
            if (selectedProfileIndices.Count != 1 || selectedProfile == null)
                return;
            
            ExportProfile(selectedProfile);
        }

        private void HandleFocusSearch()
        {
            // Focus the appropriate search field based on current view
            var searchField = _isOverlayOpen ? _overlaySearchField : _mainSearchField;
            
            if (searchField != null)
            {
                // Find the TextField within the TokenizedSearchField
                var textField = searchField.Q<TextField>();
                if (textField != null)
                {
                    textField.Focus();
                    textField.SelectAll();
                }
            }
        }

        private void HandleArrowKeyNavigation(bool isUp, bool extendSelection, bool moveProfile)
        {
            if (allProfiles.Count == 0)
                return;
            
            var visibleIndices = GetVisibleProfileIndices();
            if (visibleIndices.Count == 0)
                return;
            
            if (moveProfile)
            {
                // Move profile up/down in order
                if (selectedProfileIndices.Count == 1)
                {
                    int currentIndex = selectedProfileIndices.First();
                    int visibleIndex = visibleIndices.IndexOf(currentIndex);
                    
                    if (isUp && visibleIndex > 0)
                    {
                        int targetVisibleIndex = visibleIndex - 1;
                        int targetIndex = visibleIndices[targetVisibleIndex];
                        ReorderProfiles(currentIndex, targetVisibleIndex);
                    }
                    else if (!isUp && visibleIndex < visibleIndices.Count - 1)
                    {
                        int targetVisibleIndex = visibleIndex + 1;
                        int targetIndex = visibleIndices[targetVisibleIndex];
                        ReorderProfiles(currentIndex, targetVisibleIndex);
                    }
                }
            }
            else
            {
                // Navigate selection
                int currentIndex = -1;
                if (selectedProfileIndices.Count > 0)
                {
                    // Use the last clicked index or first selected
                    currentIndex = lastClickedProfileIndex >= 0 ? lastClickedProfileIndex : selectedProfileIndices.First();
                }
                
                int currentVisibleIndex = visibleIndices.IndexOf(currentIndex);
                
                if (currentVisibleIndex < 0)
                {
                    // No current selection, select first/last
                    currentVisibleIndex = isUp ? visibleIndices.Count - 1 : 0;
                }
                else
                {
                    // Move to next/previous
                    if (isUp && currentVisibleIndex > 0)
                    {
                        currentVisibleIndex--;
                    }
                    else if (!isUp && currentVisibleIndex < visibleIndices.Count - 1)
                    {
                        currentVisibleIndex++;
                    }
                    else
                    {
                        return; // Already at boundary
                    }
                }
                
                int targetIndex = visibleIndices[currentVisibleIndex];
                
                if (extendSelection)
                {
                    // Extend selection range
                    if (selectedProfileIndices.Count == 0)
                    {
                        selectedProfileIndices.Add(targetIndex);
                    }
                    else
                    {
                        int startIndex = Math.Min(selectedProfileIndices.Min(), targetIndex);
                        int endIndex = Math.Max(selectedProfileIndices.Max(), targetIndex);
                        
                        selectedProfileIndices.Clear();
                        for (int i = startIndex; i <= endIndex; i++)
                        {
                            if (visibleIndices.Contains(i))
                            {
                                selectedProfileIndices.Add(i);
                            }
                        }
                    }
                }
                else
                {
                    // Single selection
                    selectedProfileIndices.Clear();
                    selectedProfileIndices.Add(targetIndex);
                }
                
                selectedProfile = allProfiles[targetIndex];
                lastClickedProfileIndex = targetIndex;
                
                UpdateProfileList();
                UpdateProfileDetails();
                UpdateBottomBar();
                
                // Scroll into view
                ScrollProfileIntoView(targetIndex);
            }
        }

        private void HandleHomeEndNavigation(bool isHome, bool extendSelection)
        {
            var visibleIndices = GetVisibleProfileIndices();
            if (visibleIndices.Count == 0)
                return;
            
            int targetIndex = isHome ? visibleIndices[0] : visibleIndices[visibleIndices.Count - 1];
            
            if (extendSelection && selectedProfileIndices.Count > 0)
            {
                int startIndex = isHome ? Math.Min(selectedProfileIndices.Min(), targetIndex) : selectedProfileIndices.Min();
                int endIndex = isHome ? selectedProfileIndices.Max() : Math.Max(selectedProfileIndices.Max(), targetIndex);
                
                selectedProfileIndices.Clear();
                for (int i = startIndex; i <= endIndex; i++)
                {
                    if (visibleIndices.Contains(i))
                    {
                        selectedProfileIndices.Add(i);
                    }
                }
            }
            else
            {
                selectedProfileIndices.Clear();
                selectedProfileIndices.Add(targetIndex);
            }
            
            selectedProfile = allProfiles[targetIndex];
            lastClickedProfileIndex = targetIndex;
            
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
            
            ScrollProfileIntoView(targetIndex);
        }

        private void HandlePageUpDownNavigation(bool isPageUp)
        {
            var visibleIndices = GetVisibleProfileIndices();
            if (visibleIndices.Count == 0)
                return;
            
            int currentIndex = -1;
            if (selectedProfileIndices.Count > 0)
            {
                currentIndex = lastClickedProfileIndex >= 0 ? lastClickedProfileIndex : selectedProfileIndices.First();
            }
            
            int currentVisibleIndex = visibleIndices.IndexOf(currentIndex);
            if (currentVisibleIndex < 0)
            {
                currentVisibleIndex = isPageUp ? visibleIndices.Count - 1 : 0;
            }
            
            // Calculate page size based on visible items in scroll view
            int pageSize = CalculatePageSize();
            
            int targetVisibleIndex;
            if (isPageUp)
            {
                targetVisibleIndex = Math.Max(0, currentVisibleIndex - pageSize);
            }
            else
            {
                targetVisibleIndex = Math.Min(visibleIndices.Count - 1, currentVisibleIndex + pageSize);
            }
            
            int targetIndex = visibleIndices[targetVisibleIndex];
            
            selectedProfileIndices.Clear();
            selectedProfileIndices.Add(targetIndex);
            selectedProfile = allProfiles[targetIndex];
            lastClickedProfileIndex = targetIndex;
            
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
            
            ScrollProfileIntoView(targetIndex);
        }

        private int CalculatePageSize()
        {
            // Estimate page size based on scroll view height and item height
            if (_profileListScrollView == null)
                return 10; // Default
            
            float scrollViewHeight = _profileListScrollView.resolvedStyle.height;
            float itemHeight = 60f; // Approximate item height
            
            if (scrollViewHeight <= 0 || itemHeight <= 0)
                return 10;
            
            return Math.Max(1, (int)(scrollViewHeight / itemHeight));
        }

        private void ScrollProfileIntoView(int profileIndex)
        {
            // Find the profile item element and scroll it into view
            if (_profileListContainer == null)
                return;
            
            var profileItem = _profileListContainer.Children()
                .FirstOrDefault(child => 
                {
                    if (child.userData is int idx && idx == profileIndex)
                        return true;
                    
                    // Also check nested items (in folders)
                    var nested = child.Query(className: "yucp-profile-item").ToList();
                    return nested.Exists(item => item.userData is int idx2 && idx2 == profileIndex);
                });
            
            if (profileItem != null && _profileListScrollView != null)
            {
                _profileListScrollView.ScrollTo(profileItem);
            }
        }

        private void HandleCreateFolder()
        {
            if (selectedProfileIndices.Count >= 2)
            {
                // Group selected profiles into a new folder
                GroupSelectedProfilesIntoFolder();
            }
            else
            {                
                GroupSelectedProfilesIntoFolder();
            }
        }

        private void HandleFolderExpandCollapse(bool expand)
        {
            // Check if selected profile is in a folder, or if we can determine folder from selection
            if (selectedProfile == null || string.IsNullOrEmpty(selectedProfile.folderName))
                return;
            
            string folderName = selectedProfile.folderName;
            
            if (expand)
            {
                collapsedFolders.Remove(folderName);
            }
            else
            {
                collapsedFolders.Add(folderName);
            }
            
            // Save collapsed state (method already exists in Profiles.Folders.cs)
            SaveCollapsedFolders();
            
            UpdateProfileList();
        }

        private void HandleSelectInProject()
        {
            if (selectedProfile == null)
                return;
            
            Selection.activeObject = selectedProfile;
            EditorGUIUtility.PingObject(selectedProfile);
        }

        private void HandleShowInExplorer()
        {
            if (selectedProfile == null)
                return;
            
            if (!string.IsNullOrEmpty(selectedProfile.profileSaveLocation) && 
                System.IO.Directory.Exists(selectedProfile.profileSaveLocation))
            {
                EditorUtility.RevealInFinder(selectedProfile.profileSaveLocation);
            }
        }

        private void HandleRefresh()
        {
            LoadProfiles();
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
        }
    }
}
