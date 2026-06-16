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
        private const double CreatorPackageErrorRetryCooldownSeconds = 10d;

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
        private double _creatorPackageCacheLastFailureAt;

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

        /// <summary>
        /// Standalone bordered card — used when the registry stands on its own
        /// (signed out / no certificate states).
        /// </summary>
        public VisualElement CreateCard()
        {
            if (_profile == null)
            {
                return null;
            }

            var card = MakeRoundedBox(Surface, 12, 1, Border);
            var body = MakePad(20, 20, 20, 20);
            body.Add(MakeLabel("Package Identity", 14, TextPri, bold: true, mb: 4));
            body.Add(MakeLabel(
                "A stable identity keeps your package history and license validation intact across updates.",
                11,
                TextSec,
                mb: 14,
                wrap: true));
            body.Add(CreateContent());
            card.Add(body);
            return card;
        }

        /// <summary>
        /// Just the identity status + actions, with no card chrome or header — so it
        /// can be embedded directly under the merged "Package" section of the active card.
        /// </summary>
        public VisualElement CreateContent()
        {
            if (_profile == null)
            {
                return null;
            }

            if (!_creatorPackageCacheLoading && !HasFreshCreatorPackageCache() && CanRetryCreatorPackageLoad())
            {
                EnsureCreatorPackagesLoaded();
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.justifyContent = Justify.SpaceBetween;

            var info = new VisualElement();
            info.style.flexGrow = 1;
            info.style.flexShrink = 1;
            info.style.marginRight = 10;

            var currentPackage = FindKnownPackage(_profile.packageId);
            bool isAssigned = !string.IsNullOrWhiteSpace(_profile.packageId);

            if (!isAssigned)
            {
                info.Add(MakeLabel("No identity yet", 13, TextPri, bold: true, mb: 3));
                info.Add(MakeLabel(
                    "One is created automatically the first time you export.",
                    11, TextMute, wrap: true));
            }
            else
            {
                string pkgName = currentPackage != null && !string.IsNullOrWhiteSpace(currentPackage.packageName)
                    ? currentPackage.packageName
                    : (!string.IsNullOrWhiteSpace(_profile.packageName) ? _profile.packageName : "This package");
                info.Add(MakeLabel(pkgName, 13, TextPri, bold: true, mb: 3));
                info.Add(MakeLabel(
                    $"id · {ShortId(_profile.packageId)} · keeps updates linked to the same product",
                    11, TextMute, wrap: true));
            }

            if (!string.IsNullOrWhiteSpace(_creatorPackageCacheError))
            {
                info.Add(MakeLabel(_creatorPackageCacheError, 11, Error, mt: 6, wrap: true));
            }
            else if (_creatorPackageCacheLoading)
            {
                info.Add(MakeLabel("Syncing with registry…", 11, TextMute, mt: 6));
            }

            row.Add(info);
            row.Add(MakeChangeButton(isAssigned ? "Change" : "Set up", ShowChangeMenu));
            return row;
        }

        private string GetPackageRegistryServerUrl()
        {
            string configuredServerUrl = _getServerUrl?.Invoke();
            return string.IsNullOrWhiteSpace(configuredServerUrl) ? null : configuredServerUrl.Trim();
        }

        private bool HasFreshCreatorPackageCache()
        {
            return _creatorPackageCacheLoadedAt > 0 &&
                EditorApplication.timeSinceStartup - _creatorPackageCacheLoadedAt < CreatorPackageCacheLifetimeSeconds;
        }

        private bool CanRetryCreatorPackageLoad()
        {
            return string.IsNullOrEmpty(_creatorPackageCacheError) ||
                _creatorPackageCacheLastFailureAt <= 0 ||
                EditorApplication.timeSinceStartup - _creatorPackageCacheLastFailureAt >= CreatorPackageErrorRetryCooldownSeconds;
        }

        private void EnsureCreatorPackagesLoaded(bool forceRefresh = false, Action onComplete = null)
        {
            string serverUrl = GetPackageRegistryServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                _creatorPackageCacheError = "Configure the signing server URL before loading packages.";
                _creatorPackageCacheLastFailureAt = EditorApplication.timeSinceStartup;
                onComplete?.Invoke();
                return;
            }

            if (!YucpOAuthService.IsSignedIn())
            {
                _creatorPackageCacheError = "Sign in with your creator account before loading packages.";
                _creatorPackageCacheLastFailureAt = 0;
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

            if (!forceRefresh && !CanRetryCreatorPackageLoad())
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
                    _creatorPackageCacheLastFailureAt = 0;
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
                    _creatorPackageCacheLastFailureAt = EditorApplication.timeSinceStartup;
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

        private static string ShortId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return id;
            return id.Length <= 10 ? id : id.Substring(0, 8);
        }

        // One "Change" affordance that opens a menu, replacing the old
        // Link / Generate / Unlink / Sync button cluster.
        private void ShowChangeMenu()
        {
            void Build()
            {
                bool isAssigned = !string.IsNullOrWhiteSpace(_profile.packageId);
                var menu = new GenericMenu();

                if (_creatorPackageCache.Count > 0)
                {
                    foreach (var package in _creatorPackageCache)
                    {
                        string name = string.IsNullOrWhiteSpace(package.packageName)
                            ? package.packageId
                            : package.packageName;
                        bool isCurrent = string.Equals(
                            _profile.packageId, package.packageId, StringComparison.OrdinalIgnoreCase);
                        menu.AddItem(new GUIContent($"Link to existing/{name}"), isCurrent,
                            () => AssignExistingPackageId(package));
                    }
                    menu.AddSeparator("");
                }

                menu.AddItem(new GUIContent("Generate a new identity"), false, () =>
                {
                    Undo.RecordObject(_profile, "Generate New Package ID");
                    PackageIdManager.AssignNewPackageId(_profile);
                    EditorUtility.SetDirty(_profile);
                    _onProfileChanged?.Invoke();
                    _refreshUi?.Invoke();
                });

                if (isAssigned)
                {
                    menu.AddItem(new GUIContent("Unlink this identity"), false, () =>
                    {
                        if (EditorUtility.DisplayDialog(
                            "Unlink identity",
                            "This package will be treated as new on the next export. Continue?",
                            "Unlink", "Cancel"))
                        {
                            Undo.RecordObject(_profile, "Clear Package ID");
                            PackageIdManager.UnlinkPackageId(_profile);
                            EditorUtility.SetDirty(_profile);
                            _onProfileChanged?.Invoke();
                            _refreshUi?.Invoke();
                        }
                    });
                }

                menu.AddSeparator("");
                menu.AddItem(new GUIContent(_creatorPackageCacheLoading ? "Refreshing…" : "Refresh list"),
                    false, () => EnsureCreatorPackagesLoaded(forceRefresh: true));

                menu.ShowAsContext();
            }

            if (_creatorPackageCache.Count == 0 && !_creatorPackageCacheLoading)
                EnsureCreatorPackagesLoaded(forceRefresh: true, onComplete: Build);
            else
                Build();
        }

        private VisualElement MakeChangeButton(string text, Action onClick)
        {
            var btn = new Button(() => onClick?.Invoke());
            btn.style.flexShrink = 0;
            btn.style.paddingLeft = btn.style.paddingRight = 10;
            btn.style.paddingTop = btn.style.paddingBottom = 4;
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 6;
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = 1;
            btn.style.borderTopColor = btn.style.borderBottomColor =
                btn.style.borderLeftColor = btn.style.borderRightColor = Border;
            var lbl = MakeLabel(text, 11, TextSec, mb: 0);
            btn.Add(lbl);
            btn.RegisterCallback<MouseEnterEvent>(_ =>
            {
                btn.style.borderTopColor = btn.style.borderBottomColor =
                    btn.style.borderLeftColor = btn.style.borderRightColor = TextMute;
                lbl.style.color = TextPri;
            });
            btn.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                btn.style.borderTopColor = btn.style.borderBottomColor =
                    btn.style.borderLeftColor = btn.style.borderRightColor = Border;
                lbl.style.color = TextSec;
            });
            return btn;
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
    }
}
