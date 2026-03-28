using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.DevTools.Editor.PackageExporter;
using YUCP.DevTools.Editor.PackageSigning.Core;

namespace YUCP.DevTools.Editor.PackageSigning.UI
{
    internal sealed class PackageRegistrySection
    {
        private const double CreatorPackageCacheLifetimeSeconds = 60d;

        private static readonly Color Surface = new Color(0.118f, 0.118f, 0.118f);
        private static readonly Color SurfaceRaise = new Color(0.138f, 0.138f, 0.138f);
        private static readonly Color Border = new Color(0.157f, 0.157f, 0.157f);
        private static readonly Color TextPri = new Color(0.961f, 0.961f, 0.961f);
        private static readonly Color TextSec = new Color(0.549f, 0.549f, 0.549f);
        private static readonly Color TextMute = new Color(0.302f, 0.302f, 0.302f);
        private static readonly Color Error = new Color(0.910f, 0.294f, 0.294f);

        private readonly ExportProfile _profile;
        private readonly Func<string> _getServerUrl;
        private readonly Action _refreshUi;
        private readonly Action _onProfileChanged;
        private readonly List<PackageRegistryService.CreatorPackageSummary> _creatorPackageCache =
            new List<PackageRegistryService.CreatorPackageSummary>();

        private bool _creatorPackageCacheLoading;
        private string _creatorPackageCacheError;
        private double _creatorPackageCacheLoadedAt;

        public PackageRegistrySection(
            ExportProfile profile,
            Func<string> getServerUrl,
            Action refreshUi,
            Action onProfileChanged)
        {
            _profile = profile;
            _getServerUrl = getServerUrl;
            _refreshUi = refreshUi;
            _onProfileChanged = onProfileChanged;
        }

        public VisualElement CreateCard()
        {
            if (_profile == null)
            {
                return null;
            }

            if (!_creatorPackageCacheLoading && !HasFreshCreatorPackageCache())
            {
                EnsureCreatorPackagesLoaded();
            }

            var card = MakeRoundedBox(Surface, 12, 1, Border);
            var body = MakePad(20, 20, 20, 20);

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.justifyContent = Justify.SpaceBetween;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 12;

            headerRow.Add(MakeLabel("Package Identity & Registry", 14, TextPri, bold: true));
            body.Add(headerRow);
            
            body.Add(MakeLabel(
                "Your package identity maintains continuity across projects and updates. Keeping the same identity ensures your package history and license validation stay intact.",
                12,
                TextSec,
                mb: 20,
                wrap: true));

            var statusBox = MakeRoundedBox(SurfaceRaise, 8, 1, Border);
            statusBox.style.marginBottom = 16;
            
            statusBox.Add(BuildStatusPanel());
            statusBox.Add(BuildActionRow());

            body.Add(statusBox);

            body.Add(MakeLabel(
                "You can manage all registered packages and their certificates directly from the YUCP Dashboard.",
                11,
                TextMute,
                mt: 4,
                wrap: true));

            card.Add(body);
            return card;
        }

        private string GetPackageRegistryServerUrl()
        {
            if (!string.IsNullOrWhiteSpace(_profile?.signingServerUrl))
            {
                return _profile.signingServerUrl.Trim();
            }

            string configuredServerUrl = _getServerUrl?.Invoke();
            return string.IsNullOrWhiteSpace(configuredServerUrl) ? null : configuredServerUrl.Trim();
        }

        private bool HasFreshCreatorPackageCache()
        {
            return _creatorPackageCacheLoadedAt > 0 &&
                EditorApplication.timeSinceStartup - _creatorPackageCacheLoadedAt < CreatorPackageCacheLifetimeSeconds;
        }

        private void EnsureCreatorPackagesLoaded(bool forceRefresh = false, Action onComplete = null)
        {
            string serverUrl = GetPackageRegistryServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                _creatorPackageCacheError = "Configure the signing server URL before loading packages.";
                onComplete?.Invoke();
                return;
            }

            if (_creatorPackageCacheLoading)
            {
                return;
            }

            if (!forceRefresh && HasFreshCreatorPackageCache())
            {
                onComplete?.Invoke();
                return;
            }

            _creatorPackageCacheLoading = true;
            _creatorPackageCacheError = null;

            PackageRegistryService.GetCreatorPackages(
                serverUrl,
                packages =>
                {
                    _creatorPackageCacheLoading = false;
                    _creatorPackageCacheError = null;
                    _creatorPackageCacheLoadedAt = EditorApplication.timeSinceStartup;
                    _creatorPackageCache.Clear();
                    if (packages != null)
                    {
                        _creatorPackageCache.AddRange(packages);
                    }

                    _refreshUi?.Invoke();
                    onComplete?.Invoke();
                },
                error =>
                {
                    _creatorPackageCacheLoading = false;
                    _creatorPackageCacheError = error;
                    _refreshUi?.Invoke();
                    onComplete?.Invoke();
                });
        }

        private PackageRegistryService.CreatorPackageSummary FindKnownPackage(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return null;
            }

            return _creatorPackageCache.FirstOrDefault(package =>
                string.Equals(package.packageId, packageId, StringComparison.OrdinalIgnoreCase));
        }

        private void AssignExistingPackageId(PackageRegistryService.CreatorPackageSummary package)
        {
            if (_profile == null || package == null)
            {
                return;
            }

            Undo.RecordObject(_profile, "Assign Existing Package ID");
            PackageIdManager.SetPackageId(_profile, package.packageId);

            if (string.IsNullOrWhiteSpace(_profile.packageName) && !string.IsNullOrWhiteSpace(package.packageName))
            {
                _profile.packageName = package.packageName;
                if (string.IsNullOrWhiteSpace(_profile.profileName))
                {
                    _profile.profileName = package.packageName;
                }
            }

            EditorUtility.SetDirty(_profile);
            _onProfileChanged?.Invoke();
            _refreshUi?.Invoke();
        }

        private void ShowExistingPackageMenu()
        {
            if (_creatorPackageCache.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "No Packages Found",
                    "No creator packages are available yet. Export and sign a package first, or refresh after signing in.",
                    "OK");
                return;
            }

            var menu = new GenericMenu();
            foreach (var package in _creatorPackageCache)
            {
                string displayName = string.IsNullOrWhiteSpace(package.packageName)
                    ? package.packageId
                    : $"{package.packageName} [{package.packageId}]";
                bool isCurrent = string.Equals(
                    _profile.packageId,
                    package.packageId,
                    StringComparison.OrdinalIgnoreCase);
                menu.AddItem(new GUIContent(displayName), isCurrent, () => AssignExistingPackageId(package));
            }

            menu.ShowAsContext();
        }

        private VisualElement BuildStatusPanel()
        {
            var panel = new VisualElement();
            panel.style.paddingLeft = 16;
            panel.style.paddingRight = 16;
            panel.style.paddingTop = 16;
            panel.style.paddingBottom = 16;

            var currentPackage = FindKnownPackage(_profile.packageId);
            bool isAssigned = !string.IsNullOrWhiteSpace(_profile.packageId);
            
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;

            var dot = new VisualElement();
            dot.style.width = 10;
            dot.style.height = 10;
            dot.style.borderTopLeftRadius = 5;
            dot.style.borderTopRightRadius = 5;
            dot.style.borderBottomLeftRadius = 5;
            dot.style.borderBottomRightRadius = 5;
            dot.style.marginTop = 4;
            dot.style.marginRight = 12;
            
            var infoCol = new VisualElement();
            infoCol.style.flexGrow = 1;

            string titleText;
            string descText;

            if (!isAssigned)
            {
                dot.style.backgroundColor = new Color(0.9f, 0.7f, 0.2f);
                titleText = "Identity Unassigned";
                descText = "This package does not have an identity yet. Exporting without an identity will assign one automatically.";
            }
            else if (currentPackage != null)
            {
                dot.style.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
                string pkgName = string.IsNullOrWhiteSpace(currentPackage.packageName) ? currentPackage.packageId : currentPackage.packageName;
                titleText = $"Linked to: {pkgName}";
                descText = $"Package ID: {_profile.packageId}";
            }
            else
            {
                dot.style.backgroundColor = new Color(0.212f, 0.749f, 0.694f); // Teal
                titleText = "Local Identity Assigned";
                descText = $"Package ID: {_profile.packageId}\nThis ID will be registered on the server during the next signed export.";
            }

            infoCol.Add(MakeLabel(titleText, 13, TextPri, bold: true, mb: 4));
            infoCol.Add(MakeLabel(descText, 11, TextSec, mb: 0, wrap: true));

            if (!string.IsNullOrWhiteSpace(_creatorPackageCacheError))
            {
                infoCol.Add(MakeLabel($"Connection Error: {_creatorPackageCacheError}", 11, Error, mt: 8, wrap: true));
            }
            else if (_creatorPackageCacheLoading)
            {
                infoCol.Add(MakeLabel("Syncing with registry...", 11, TextMute, mt: 8));
            }

            row.Add(dot);
            row.Add(infoCol);
            
            panel.Add(row);
            return panel;
        }

        private VisualElement BuildActionRow()
        {
            var actionContainer = new VisualElement();
            actionContainer.style.borderTopWidth = 1;
            actionContainer.style.borderTopColor = Border;
            actionContainer.style.backgroundColor = new Color(0f, 0f, 0f, 0.2f);
            actionContainer.style.paddingLeft = 16;
            actionContainer.style.paddingRight = 16;
            actionContainer.style.paddingTop = 12;
            actionContainer.style.paddingBottom = 12;
            
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;

            var leftActions = new VisualElement();
            leftActions.style.flexDirection = FlexDirection.Row;
            
            var rightActions = new VisualElement();
            rightActions.style.flexDirection = FlexDirection.Row;

            bool isAssigned = !string.IsNullOrWhiteSpace(_profile.packageId);

            leftActions.Add(MakeActionButton(
                "Link Existing Identity",
                () => {
                    if (_creatorPackageCache.Count > 0)
                    {
                        ShowExistingPackageMenu();
                        return;
                    }
                    EnsureCreatorPackagesLoaded(forceRefresh: true, onComplete: () => {
                        if (_creatorPackageCache.Count > 0)
                            ShowExistingPackageMenu();
                    });
                },
                !_creatorPackageCacheLoading,
                isPrimary: true
            ));

            if (!isAssigned)
            {
                leftActions.Add(MakeActionButton(
                    "Generate New",
                    () => {
                        Undo.RecordObject(_profile, "Generate New Package ID");
                        PackageIdManager.AssignNewPackageId(_profile);
                        EditorUtility.SetDirty(_profile);
                        _onProfileChanged?.Invoke();
                        _refreshUi?.Invoke();
                    }
                ));
            }

            rightActions.Add(MakeActionButton(
                _creatorPackageCacheLoading ? "Syncing..." : "Sync",
                () => EnsureCreatorPackagesLoaded(forceRefresh: true),
                !_creatorPackageCacheLoading
            ));

            if (isAssigned)
            {
                rightActions.Add(MakeActionButton(
                    "Unlink",
                    () => {
                        if (EditorUtility.DisplayDialog("Unlink Identity", "Are you sure you want to unlink this package identity? The package will be treated as new.", "Unlink", "Cancel"))
                        {
                            Undo.RecordObject(_profile, "Clear Package ID");
                            PackageIdManager.UnlinkPackageId(_profile);
                            EditorUtility.SetDirty(_profile);
                            _onProfileChanged?.Invoke();
                            _refreshUi?.Invoke();
                        }
                    },
                    isDanger: true
                ));
            }

            row.Add(leftActions);
            row.Add(rightActions);
            actionContainer.Add(row);

            return actionContainer;
        }

        private static VisualElement MakeCard()
        {
            return MakeRoundedBox(Surface, 12, 1, Border);
        }

        private static VisualElement MakeRoundedBox(Color background, int radius, int borderWidth, Color borderColor)
        {
            var box = new VisualElement();
            box.style.backgroundColor = background;
            box.style.borderTopLeftRadius = radius;
            box.style.borderTopRightRadius = radius;
            box.style.borderBottomLeftRadius = radius;
            box.style.borderBottomRightRadius = radius;
            box.style.borderTopWidth = borderWidth;
            box.style.borderRightWidth = borderWidth;
            box.style.borderBottomWidth = borderWidth;
            box.style.borderLeftWidth = borderWidth;
            box.style.borderTopColor = borderColor;
            box.style.borderRightColor = borderColor;
            box.style.borderBottomColor = borderColor;
            box.style.borderLeftColor = borderColor;
            return box;
        }

        private static VisualElement MakePad(float top, float right, float bottom, float left)
        {
            var element = new VisualElement();
            element.style.paddingTop = top;
            element.style.paddingRight = right;
            element.style.paddingBottom = bottom;
            element.style.paddingLeft = left;
            return element;
        }

        private static Label MakeLabel(
            string text,
            int size,
            Color color,
            bool bold = false,
            int mb = 0,
            int mt = 0,
            bool wrap = false)
        {
            var label = new Label(text ?? string.Empty);
            label.style.fontSize = size;
            label.style.color = color;
            label.style.marginBottom = mb;
            label.style.marginTop = mt;
            label.style.whiteSpace = wrap ? WhiteSpace.Normal : WhiteSpace.NoWrap;
            if (bold)
            {
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
            }

            return label;
        }

        private static Button MakeActionButton(string text, Action onClick, bool enabled = true, bool isPrimary = false, bool isDanger = false)
        {
            var btn = new Button(() => { if (enabled) onClick?.Invoke(); }) { text = text };
            
            btn.AddToClassList("yucp-button");

            if (isPrimary)
            {
                btn.AddToClassList("yucp-button-primary");
            }
            else if (isDanger)
            {
                btn.AddToClassList("yucp-button-danger");
            }
            else
            {
                btn.AddToClassList("yucp-button-action");
            }

            btn.style.marginRight = isDanger ? 0 : 4;
            btn.SetEnabled(enabled);

            return btn;
        }
    }
}
