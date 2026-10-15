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

            if (isExporting)
                return;


            if (evt.keyCode == KeyCode.Escape)
            {
                HandleEscapeKey();
                evt.StopPropagation();
                return;
            }



            var focusedElement = rootVisualElement.focusController?.focusedElement as VisualElement;
            bool isTextEditingContext = IsTextEditingContext(focusedElement) || IsTextEditingContext(evt.target as VisualElement);


            bool modifierPressed = IsModifierPressed(evt);
            bool shiftPressed = evt.shiftKey;
            bool altPressed = evt.altKey;



            if (isTextEditingContext)
            {
                return;
            }


            if (!isTextEditingContext && modifierPressed && evt.keyCode == KeyCode.Z && !shiftPressed)
            {

                Undo.PerformUndo();
                evt.StopPropagation();
                return;
            }

            if (!isTextEditingContext && ((modifierPressed && evt.keyCode == KeyCode.Y) ||
                (modifierPressed && shiftPressed && evt.keyCode == KeyCode.Z)))
            {

                Undo.PerformRedo();
                evt.StopPropagation();
                return;
            }

            if (!isTextEditingContext && modifierPressed && evt.keyCode == KeyCode.A && !shiftPressed && !altPressed)
            {

                HandleSelectAll();
                evt.StopPropagation();
                return;
            }


            if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
            {

                HandleDelete();
                evt.StopPropagation();
                return;
            }

            if (modifierPressed && evt.keyCode == KeyCode.D && !shiftPressed)
            {

                HandleDuplicate();
                evt.StopPropagation();
                return;
            }

            if (modifierPressed && shiftPressed && evt.keyCode == KeyCode.D)
            {

                HandleClone();
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.F2 || (evt.keyCode == KeyCode.Return && !modifierPressed))
            {

                HandleRename();
                evt.StopPropagation();
                return;
            }


            if (modifierPressed && evt.keyCode == KeyCode.E && !shiftPressed)
            {

                HandleExportSelected();
                evt.StopPropagation();
                return;
            }

            if (modifierPressed && shiftPressed && evt.keyCode == KeyCode.E)
            {

                HandleExportAll();
                evt.StopPropagation();
                return;
            }

            if (modifierPressed && evt.keyCode == KeyCode.Return)
            {

                HandleQuickExport();
                evt.StopPropagation();
                return;
            }


            if (modifierPressed && evt.keyCode == KeyCode.F && !shiftPressed && !altPressed)
            {

                HandleFocusSearch();
                evt.StopPropagation();
                return;
            }


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


            if (modifierPressed && shiftPressed && evt.keyCode == KeyCode.N)
            {

                HandleCreateFolder();
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.RightArrow || evt.keyCode == KeyCode.LeftArrow)
            {

                HandleFolderExpandCollapse(evt.keyCode == KeyCode.RightArrow);
                evt.StopPropagation();
                return;
            }


            if (modifierPressed && altPressed && evt.keyCode == KeyCode.P)
            {

                HandleSelectInProject();
                evt.StopPropagation();
                return;
            }

            if (modifierPressed && altPressed && evt.keyCode == KeyCode.E)
            {

                HandleShowInExplorer();
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.F5)
            {

                HandleRefresh();
                evt.StopPropagation();
                return;
            }
        }


        private bool IsTextEditingContext(VisualElement element)
        {

            for (var current = element; current != null; current = current.parent as VisualElement)
            {
                if (current is IMGUIContainer) return true;
                if (current is TextField) return true;

                var t = current.GetType();
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(TextInputBaseField<>))
                    return true;
            }

            return false;
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

            YUCP.DevTools.Editor.PackageExporter.UI.Components.TokenizedTagInput.CloseTagMenu();


            if (_exportOptionsOverlay != null)
            {
                ToggleExportOptions(false);
            }


            if (folderBeingRenamed != null)
            {
                folderBeingRenamed = null;
                UpdateProfileList();
            }


            selectedProfileIndices.Clear();
            selectedProfile = null;
            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
        }

        private void HandleSelectAll()
        {

            var visibleIndices = GetVisibleProfileIndices();

            if (visibleIndices.Count == 0)
                return;

            selectedProfileIndices.Clear();
            foreach (var index in visibleIndices)
            {
                selectedProfileIndices.Add(index);
            }


            if (visibleIndices.Count > 0)
            {
                selectedProfile = allProfiles[visibleIndices[0]];
                lastClickedProfileIndex = visibleIndices[0];
            }

            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();
        }

        /// <summary>Profiles currently on screen, in on-screen order (see RenderProfileListWithFolders).</summary>
        private List<int> GetVisibleProfileIndices()
        {
            return _displayedProfileIndices;
        }

        /// <summary>Selects every displayed profile between two profiles (inclusive), in on-screen order.</summary>
        private void SelectDisplayedRange(int anchorIndex, int targetIndex)
        {
            int from = _displayedProfileIndices.IndexOf(anchorIndex);
            int to = _displayedProfileIndices.IndexOf(targetIndex);

            selectedProfileIndices.Clear();
            if (from < 0 || to < 0)
            {
                selectedProfileIndices.Add(targetIndex);
                return;
            }

            for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++)
            {
                selectedProfileIndices.Add(_displayedProfileIndices[i]);
            }
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

            BulkDeleteProfilesInternal(profilesToDelete);
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
                DuplicateProfile(profile);
            }
        }

        private void HandleClone()
        {
            if (selectedProfileIndices.Count == 0)
                return;

            var profilesToClone = selectedProfileIndices.OrderBy(i => i)
                .Select(i => allProfiles[i])
                .ToList();

            foreach (var profile in profilesToClone)
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

            var searchField = _isOverlayOpen ? _overlaySearchField : _mainSearchField;

            if (searchField != null)
            {

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
            if (moveProfile)
            {
                if (selectedProfileIndices.Count == 1 && selectedProfile != null)
                {
                    MoveProfileOneRow(selectedProfile, isUp);
                    ScrollProfileIntoView(allProfiles.IndexOf(selectedProfile));
                }
                return;
            }

            var visibleIndices = GetVisibleProfileIndices();
            if (visibleIndices.Count == 0)
                return;

            // Step from the focused profile (the last one clicked or navigated to) in on-screen order
            int position = visibleIndices.IndexOf(GetFocusedProfileIndex());
            if (position < 0)
                position = isUp ? visibleIndices.Count - 1 : 0;
            else if (isUp ? position == 0 : position == visibleIndices.Count - 1)
                return;
            else
                position += isUp ? -1 : 1;

            NavigateToProfile(visibleIndices[position], extendSelection);
        }

        private void HandleHomeEndNavigation(bool isHome, bool extendSelection)
        {
            var visibleIndices = GetVisibleProfileIndices();
            if (visibleIndices.Count == 0)
                return;

            NavigateToProfile(isHome ? visibleIndices[0] : visibleIndices[visibleIndices.Count - 1], extendSelection);
        }

        private void HandlePageUpDownNavigation(bool isPageUp)
        {
            var visibleIndices = GetVisibleProfileIndices();
            if (visibleIndices.Count == 0)
                return;

            int position = visibleIndices.IndexOf(GetFocusedProfileIndex());
            if (position < 0)
            {
                position = isPageUp ? visibleIndices.Count - 1 : 0;
            }

            int pageSize = CalculatePageSize();
            position = isPageUp
                ? Math.Max(0, position - pageSize)
                : Math.Min(visibleIndices.Count - 1, position + pageSize);

            NavigateToProfile(visibleIndices[position], false);
        }

        /// <summary>The profile keyboard navigation moves from: the one last clicked or navigated to.</summary>
        private int GetFocusedProfileIndex()
        {
            if (selectedProfile != null)
                return allProfiles.IndexOf(selectedProfile);
            return selectedProfileIndices.Count > 0 ? selectedProfileIndices.First() : -1;
        }

        /// <summary>Focuses a profile; with extendSelection, selects everything between the anchor and it.</summary>
        private void NavigateToProfile(int targetIndex, bool extendSelection)
        {
            if (extendSelection)
            {
                if (lastClickedProfileIndex < 0 || !_displayedProfileIndices.Contains(lastClickedProfileIndex))
                {
                    int focused = GetFocusedProfileIndex();
                    lastClickedProfileIndex = focused >= 0 ? focused : targetIndex;
                }
                SelectDisplayedRange(lastClickedProfileIndex, targetIndex);
            }
            else
            {
                selectedProfileIndices.Clear();
                selectedProfileIndices.Add(targetIndex);
                lastClickedProfileIndex = targetIndex;
            }

            selectedProfile = allProfiles[targetIndex];

            UpdateProfileList();
            UpdateProfileDetails();
            UpdateBottomBar();

            ScrollProfileIntoView(targetIndex);
        }

        private int CalculatePageSize()
        {

            if (_profileListScrollView == null)
                return 10;

            float scrollViewHeight = _profileListScrollView.resolvedStyle.height;
            float itemHeight = 60f;

            if (scrollViewHeight <= 0 || itemHeight <= 0)
                return 10;

            return Math.Max(1, (int)(scrollViewHeight / itemHeight));
        }

        private void ScrollProfileIntoView(int profileIndex)
        {

            if (_profileListContainer == null)
                return;

            var profileItem = _profileListContainer.Children()
                .FirstOrDefault(child =>
                {
                    if (child.userData is int idx && idx == profileIndex)
                        return true;


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
            // Group a multi-selection; otherwise just make an empty folder
            if (selectedProfileIndices.Count >= 2)
                GroupSelectedProfilesIntoFolder();
            else
                CreateFolder(Array.Empty<ExportProfile>());
        }

        private void HandleFolderExpandCollapse(bool expand)
        {

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
