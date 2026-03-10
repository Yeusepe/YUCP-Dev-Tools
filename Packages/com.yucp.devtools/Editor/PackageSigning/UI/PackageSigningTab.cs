using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.DevTools.Editor.PackageSigning.Core;
using YUCP.DevTools.Editor.PackageSigning.Data;
using YUCP.DevTools.Editor.PackageExporter;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.DevTools.Editor.PackageSigning.UI
{
    /// <summary>
    /// Inline Package Signing section rendered inside the Package Exporter window.
    /// Three states:
    ///   1. Not signed in  — gorgeous sign-in hero with YUCP-branded button
    ///   2. Signed in, no cert — account chip + "Request Certificate" action
    ///   3. Signed in, cert active — cert status + package info
    /// </summary>
    public class PackageSigningTab
    {
        private VisualElement _root;
        private SigningSettings _settings;
        private readonly ExportProfile _profile;
        private bool _isSigningIn;
        private bool _isRequestingCert;

        // YUCP brand colors
        private static readonly Color Teal       = new Color(0.21f, 0.75f, 0.69f);   // #36BFB1
        private static readonly Color TealDim    = new Color(0.21f, 0.75f, 0.69f, 0.12f);
        private static readonly Color BgCard     = new Color(0.102f, 0.102f, 0.102f); // #1a1a1a
        private static readonly Color Border     = new Color(0.165f, 0.165f, 0.165f); // #2a2a2a
        private static readonly Color TextPri    = new Color(0.95f, 0.95f, 0.95f);
        private static readonly Color TextMuted  = new Color(0.50f, 0.50f, 0.50f);
        private static readonly Color BtnWhite   = new Color(0.957f, 0.957f, 0.957f);

        public PackageSigningTab(ExportProfile profile = null)
        {
            _profile = profile;
        }

        public VisualElement CreateUI()
        {
            _root = new VisualElement();
            _root.name = "PackageSigningTab";
            _root.AddToClassList("yucp-section");
            LoadSettings();
            _root.Add(CreateMainCard());
            return _root;
        }

        public void RefreshUI()
        {
            if (_root == null) return;
            _root.Clear();
            LoadSettings();
            _root.Add(CreateMainCard());
        }

        public bool CanSign()
        {
            LoadSettings();
            return YucpOAuthService.IsSignedIn() && _settings != null && _settings.HasValidCertificate();
        }

        // ─── Main card dispatcher ──────────────────────────────────────────────────

        private VisualElement CreateMainCard()
        {
            if (!YucpOAuthService.IsSignedIn())
                return CreateSignInHeroCard();

            bool hasCert = _settings != null && _settings.HasValidCertificate();
            return hasCert ? CreateSignedActiveCard() : CreateNoCertCard();
        }

        // ─── STATE 1: Not signed in ────────────────────────────────────────────────

        private VisualElement CreateSignInHeroCard()
        {
            // Outer container — hand-built card for full visual control
            var card = new VisualElement();
            card.style.backgroundColor  = BgCard;
            card.style.borderTopLeftRadius    = 10;
            card.style.borderTopRightRadius   = 10;
            card.style.borderBottomLeftRadius = 10;
            card.style.borderBottomRightRadius = 10;
            card.style.borderTopColor    = Border;
            card.style.borderBottomColor = Border;
            card.style.borderLeftColor   = Teal;   // teal left-border accent
            card.style.borderRightColor  = Border;
            card.style.borderTopWidth    = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth   = 3;
            card.style.borderRightWidth  = 1;
            card.style.overflow = Overflow.Hidden;
            card.style.marginBottom = 8;

            // Teal accent strip at top
            var strip = new VisualElement();
            strip.style.height = 3;
            strip.style.backgroundColor = Teal;
            card.Add(strip);

            // Content padding
            var content = new VisualElement();
            content.style.paddingTop    = 20;
            content.style.paddingBottom = 22;
            content.style.paddingLeft   = 20;
            content.style.paddingRight  = 20;
            card.Add(content);

            // Category label
            var category = new Label("CREATOR SIGNING");
            category.style.fontSize    = 10;
            category.style.letterSpacing = 1.5f;
            category.style.unityFontStyleAndWeight = FontStyle.Bold;
            category.style.color       = Teal;
            category.style.marginBottom = 10;
            content.Add(category);

            // Heading
            var heading = new Label("Sign in to start issuing signed packages");
            heading.style.fontSize     = 16;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.color        = TextPri;
            heading.style.whiteSpace   = WhiteSpace.Normal;
            heading.style.marginBottom = 8;
            content.Add(heading);

            // Body text
            var body = new Label("Packages signed with your YUCP identity are verified across the ecosystem.");
            body.style.fontSize    = 12;
            body.style.color       = TextMuted;
            body.style.whiteSpace  = WhiteSpace.Normal;
            body.style.marginBottom = 18;
            content.Add(body);

            if (_isSigningIn)
            {
                content.Add(CreateSigningInLoadingRow());
            }
            else
            {
                content.Add(CreateSignInButton());
                var hint = new Label("Secure \u00b7 Opens your browser \u00b7 OAuth 2.0 + PKCE");
                hint.style.fontSize   = 10;
                hint.style.color      = new Color(0.29f, 0.29f, 0.29f);
                hint.style.marginTop  = 10;
                hint.style.unityTextAlign = TextAnchor.MiddleCenter;
                content.Add(hint);
            }

            return card;
        }

        private VisualElement CreateSignInButton()
        {
            // SignInAsCreator design: white bg, inner elements (icon + text) — button fits content
            var btn = new VisualElement();
            btn.style.backgroundColor       = Color.white;
            btn.style.borderTopLeftRadius    = 20;
            btn.style.borderTopRightRadius   = 20;
            btn.style.borderBottomLeftRadius = 20;
            btn.style.borderBottomRightRadius = 20;
            btn.style.borderTopWidth    = 0;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth   = 0;
            btn.style.borderRightWidth  = 0;
            btn.style.flexDirection     = FlexDirection.Row;
            btn.style.alignItems        = Align.Center;
            btn.style.justifyContent    = Justify.Center;
            btn.style.paddingLeft       = 24;
            btn.style.paddingRight      = 24;
            btn.style.paddingTop        = 16;
            btn.style.paddingBottom     = 16;
            btn.style.alignSelf         = Align.Center; // prevent button from stretching to parent width

            // Inner elements — use source dimensions for correct aspect ratio (Unity import can distort)
            const string innerPath = "Packages/com.yucp.devtools/Editor/PackageSigning/Resources/SignInAsCreatorInnerElements.png";
            var innerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(innerPath);
            if (innerTexture != null)
            {
                int w = innerTexture.width, h = innerTexture.height;
                var importer = AssetImporter.GetAtPath(innerPath) as TextureImporter;
                if (importer != null)
                {
                    importer.GetSourceTextureWidthAndHeight(out int srcW, out int srcH);
                    if (srcW > 0 && srcH > 0) { w = srcW; h = srcH; }
                }

                var wrapper = new VisualElement();
                wrapper.style.width  = w * 0.7f;
                wrapper.style.height = h * 0.7f;
                wrapper.style.flexShrink = 0;
                wrapper.style.flexGrow  = 0;
                wrapper.style.overflow = Overflow.Hidden;

                var inner = new Image();
                inner.image = innerTexture;
                inner.scaleMode = ScaleMode.ScaleToFit;
                inner.style.width = Length.Percent(100);
                inner.style.height = Length.Percent(100);
                inner.style.position = Position.Absolute;
                inner.style.left = 0;
                inner.style.top = 0;
                inner.style.right = 0;
                inner.style.bottom = 0;
                wrapper.Add(inner);
                btn.Add(wrapper);
            }

            // Hover effect
            btn.RegisterCallback<MouseEnterEvent>(_ => btn.style.opacity = 0.88f);
            btn.RegisterCallback<MouseLeaveEvent>(_ => btn.style.opacity = 1.0f);

            // Click handler
            btn.RegisterCallback<ClickEvent>(evt =>
            {
                if (_isSigningIn) return;
                _isSigningIn = true;
                RefreshUI();
                string serverUrl = GetServerUrl();
                // Fire-and-forget sign-in task; suppress CS4014 warning intentionally.
#pragma warning disable CS4014
                YucpOAuthService.SignInAsync(
                    serverUrl,
#pragma warning restore CS4014
                    onSuccess: () => EditorApplication.delayCall += () =>
                    {
                        _isSigningIn = false;
                        LoadSettings();
                        RefreshUI();
                    },
                    onError: err => EditorApplication.delayCall += () =>
                    {
                        _isSigningIn = false;
                        RefreshUI();
                        EditorUtility.DisplayDialog("Sign-in Failed", err, "OK");
                    }
                );
            });

            return btn;
        }

        private VisualElement CreateSigningInLoadingRow()
        {
            var col = new VisualElement();

            var row = new VisualElement();
            row.style.height          = 83;
            row.style.backgroundColor = new Color(0.145f, 0.145f, 0.145f);
            row.style.borderTopLeftRadius    = 20;
            row.style.borderTopRightRadius   = 20;
            row.style.borderBottomLeftRadius = 20;
            row.style.borderBottomRightRadius = 20;
            row.style.borderTopWidth    = 1;
            row.style.borderBottomWidth = 1;
            row.style.borderLeftWidth   = 1;
            row.style.borderRightWidth  = 1;
            row.style.borderTopColor    = new Color(0.21f, 0.75f, 0.69f, 0.25f);
            row.style.borderBottomColor = new Color(0.21f, 0.75f, 0.69f, 0.25f);
            row.style.borderLeftColor   = new Color(0.21f, 0.75f, 0.69f, 0.25f);
            row.style.borderRightColor  = new Color(0.21f, 0.75f, 0.69f, 0.25f);
            row.style.flexDirection   = FlexDirection.Row;
            row.style.alignItems      = Align.Center;
            row.style.justifyContent  = Justify.Center;

            var dots = new Label("\u25cf  \u25cf  \u25cf");
            dots.style.fontSize  = 10;
            dots.style.color     = Teal;
            dots.style.marginRight = 12;
            row.Add(dots);

            var text = new Label("Waiting for browser sign-in\u2026");
            text.style.fontSize = 13;
            text.style.color    = TextMuted;
            row.Add(text);

            col.Add(row);

            var cancel = YUCPUIToolkitHelper.CreateButton("Cancel", () =>
            {
                _isSigningIn = false;
                RefreshUI();
            }, YUCPUIToolkitHelper.ButtonVariant.Ghost);
            cancel.style.marginTop  = 8;
            cancel.style.alignSelf  = Align.Center;
            col.Add(cancel);

            return col;
        }

        // ─── STATE 2: Signed in, no certificate ───────────────────────────────────

        private VisualElement CreateNoCertCard()
        {
            string title = _profile != null && !string.IsNullOrEmpty(_profile.packageName)
                ? $"{_profile.packageName} \u2013 Signing"
                : "Package Signing";

            var card    = YUCPUIToolkitHelper.CreateCard(title, null);
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            AddAccountChip(content);

            var warning = YUCPUIToolkitHelper.CreateHelpBox(
                "No signing certificate found. Request one to enable package signing.",
                YUCPUIToolkitHelper.MessageType.Warning);
            content.Add(warning);

            YUCPUIToolkitHelper.AddSpacing(content, 16);

            var requestBtn = YUCPUIToolkitHelper.CreateButton(
                _isRequestingCert ? "Requesting\u2026" : "\u2726  Request Signing Certificate",
                OnRequestCertClicked,
                YUCPUIToolkitHelper.ButtonVariant.Primary);
            requestBtn.style.width = Length.Percent(100);
            requestBtn.SetEnabled(!_isRequestingCert);
            content.Add(requestBtn);

            YUCPUIToolkitHelper.AddSpacing(content, 8);

            var info = new Label("Your Ed25519 dev key will be certified by the YUCP Authority. Certificates expire after 90 days.");
            info.style.fontSize   = 11;
            info.style.color      = TextMuted;
            info.style.whiteSpace = WhiteSpace.Normal;
            content.Add(info);

            YUCPUIToolkitHelper.AddSpacing(content, 16);

            // Divider
            var divider = new VisualElement();
            divider.style.height          = 1;
            divider.style.backgroundColor = Border;
            divider.style.marginBottom    = 12;
            content.Add(divider);

            // Dev key display
            AddDevKeyRow(content);

            YUCPUIToolkitHelper.AddSpacing(content, 8);

            // Import from file fallback
            var importRow = new VisualElement();
            importRow.style.flexDirection = FlexDirection.Row;
            importRow.style.alignItems    = Align.Center;
            importRow.style.justifyContent = Justify.Center;

            var importLbl = new Label("Already have a .yucp_cert file?");
            importLbl.style.fontSize  = 11;
            importLbl.style.color     = TextMuted;
            importLbl.style.marginRight = 6;
            importRow.Add(importLbl);

            var importBtn = YUCPUIToolkitHelper.CreateButton(
                "Import from file",
                () =>
                {
                    string path = EditorUtility.OpenFilePanel("Import YUCP Certificate", "", "yucp_cert");
                    if (string.IsNullOrEmpty(path)) return;
                    var result = CertificateManager.ImportAndVerify(path);
                    if (result.valid)
                    {
                        LoadSettings();
                        RefreshUI();
                        EditorUtility.DisplayDialog("Certificate Imported",
                            $"Certificate imported!\n\nPublisher: {result.publisherName}\nExpires: {result.expiresAt:MMM dd, yyyy}", "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Import Failed", result.error, "OK");
                    }
                },
                YUCPUIToolkitHelper.ButtonVariant.Ghost);
            importBtn.style.fontSize = 11;
            importRow.Add(importBtn);
            content.Add(importRow);

            return card;
        }

        // ─── STATE 3: Signed in, certificate active ───────────────────────────────

        private VisualElement CreateSignedActiveCard()
        {
            string title = _profile != null && !string.IsNullOrEmpty(_profile.packageName)
                ? $"{_profile.packageName} \u2013 Signing"
                : "Package Signing";

            var card    = YUCPUIToolkitHelper.CreateCard(title, null);
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            AddAccountChip(content);

            // Green signed banner
            var statusRow = new VisualElement();
            statusRow.style.flexDirection     = FlexDirection.Row;
            statusRow.style.alignItems        = Align.Center;
            statusRow.style.marginBottom      = 14;
            statusRow.style.paddingLeft       = 12;
            statusRow.style.paddingRight      = 12;
            statusRow.style.paddingTop        = 10;
            statusRow.style.paddingBottom     = 10;
            statusRow.style.backgroundColor   = TealDim;
            statusRow.style.borderTopLeftRadius    = 6;
            statusRow.style.borderTopRightRadius   = 6;
            statusRow.style.borderBottomLeftRadius = 6;
            statusRow.style.borderBottomRightRadius = 6;

            var check = new Label("\u2713");
            check.style.fontSize  = 16;
            check.style.color     = Teal;
            check.style.marginRight = 10;
            statusRow.Add(check);

            var statusText = new Label("This package will be signed");
            statusText.style.fontSize = 13;
            statusText.style.unityFontStyleAndWeight = FontStyle.Bold;
            statusText.style.color = TextPri;
            statusRow.Add(statusText);
            content.Add(statusRow);

            // Publisher + expiry info panel
            var certPanel = new VisualElement();
            certPanel.style.backgroundColor    = new Color(0.12f, 0.12f, 0.12f);
            certPanel.style.borderTopLeftRadius    = 8;
            certPanel.style.borderTopRightRadius   = 8;
            certPanel.style.borderBottomLeftRadius = 8;
            certPanel.style.borderBottomRightRadius = 8;
            certPanel.style.borderTopWidth    = 1;
            certPanel.style.borderBottomWidth = 1;
            certPanel.style.borderLeftWidth   = 1;
            certPanel.style.borderRightWidth  = 1;
            certPanel.style.borderTopColor    = Border;
            certPanel.style.borderBottomColor = Border;
            certPanel.style.borderLeftColor   = Border;
            certPanel.style.borderRightColor  = Border;
            certPanel.style.paddingTop        = 10;
            certPanel.style.paddingBottom     = 10;
            certPanel.style.paddingLeft       = 12;
            certPanel.style.paddingRight      = 12;
            certPanel.style.marginBottom      = 4;

            if (!string.IsNullOrEmpty(_settings?.publisherName))
                certPanel.Add(CreateInfoRow("Publisher", _settings.publisherName));

            if (!string.IsNullOrEmpty(_settings?.publisherId))
                certPanel.Add(CreateInfoRow("Publisher ID", _settings.publisherId));

            if (!string.IsNullOrEmpty(_settings?.certificateExpiresAt) &&
                DateTime.TryParse(_settings.certificateExpiresAt, out DateTime exp))
            {
                var delta = exp - DateTime.UtcNow;
                string expiryText;
                Color  expiryColor;
                if (delta.TotalDays < 0)
                { expiryText = "Expired"; expiryColor = new Color(0.89f, 0.29f, 0.29f); }
                else if (delta.TotalDays < 30)
                { expiryText = $"Expires in {Math.Ceiling(delta.TotalDays)} days"; expiryColor = new Color(0.89f, 0.65f, 0.29f); }
                else
                { expiryText = $"Valid until {exp:MMM dd, yyyy}"; expiryColor = Teal; }

                var expRow = CreateInfoRow("Certificate", expiryText);
                expRow.Q<Label>(className: "yucp-info-value").style.color = expiryColor;
                certPanel.Add(expRow);
            }

            content.Add(certPanel);

            // Current package status
            if (_profile != null)
            {
                YUCPUIToolkitHelper.AddSpacing(content, 16);
                content.Add(CreateCurrentPackageStatus());
            }

            YUCPUIToolkitHelper.AddSpacing(content, 12);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.Add(YUCPUIToolkitHelper.CreateButton(
                "Manage Certificate",
                () => SigningSettingsWindow.ShowWindow(),
                YUCPUIToolkitHelper.ButtonVariant.Ghost));
            content.Add(actions);

            return card;
        }

        // ─── Shared UI helpers ─────────────────────────────────────────────────────

        private void AddAccountChip(VisualElement content)
        {
            var row = new VisualElement();
            row.style.flexDirection  = FlexDirection.Row;
            row.style.alignItems     = Align.Center;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginBottom   = 14;
            row.style.paddingBottom  = 14;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = Border;

            var left = new VisualElement();
            left.style.flexDirection = FlexDirection.Row;
            left.style.alignItems    = Align.Center;

            // Avatar circle with initial letter
            string displayName = YucpOAuthService.GetDisplayName() ?? "Creator";
            string initial = displayName.Length > 0 ? displayName.Substring(0, 1).ToUpper() : "C";

            var avatar = new VisualElement();
            avatar.style.width  = 30;
            avatar.style.height = 30;
            avatar.style.borderTopLeftRadius     = 15;
            avatar.style.borderTopRightRadius    = 15;
            avatar.style.borderBottomLeftRadius  = 15;
            avatar.style.borderBottomRightRadius = 15;
            avatar.style.backgroundColor = new Color(0.21f, 0.75f, 0.69f, 0.15f);
            avatar.style.borderTopWidth    = 1;
            avatar.style.borderBottomWidth = 1;
            avatar.style.borderLeftWidth   = 1;
            avatar.style.borderRightWidth  = 1;
            avatar.style.borderTopColor    = Teal;
            avatar.style.borderBottomColor = Teal;
            avatar.style.borderLeftColor   = Teal;
            avatar.style.borderRightColor  = Teal;
            avatar.style.alignItems    = Align.Center;
            avatar.style.justifyContent = Justify.Center;
            avatar.style.marginRight   = 8;
            avatar.style.flexShrink    = 0;

            var initLbl = new Label(initial);
            initLbl.style.fontSize = 13;
            initLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            initLbl.style.color = Teal;
            initLbl.style.unityTextAlign = TextAnchor.MiddleCenter;
            avatar.Add(initLbl);
            left.Add(avatar);

            var nameLabel = new Label(displayName);
            nameLabel.style.fontSize = 12;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.color = TextPri;
            left.Add(nameLabel);
            row.Add(left);

            var signOut = YUCPUIToolkitHelper.CreateButton("Sign Out", OnSignOutClicked, YUCPUIToolkitHelper.ButtonVariant.Ghost);
            signOut.style.fontSize    = 11;
            signOut.style.paddingTop  = 2;
            signOut.style.paddingBottom = 2;
            row.Add(signOut);

            content.Add(row);
        }

        private void AddDevKeyRow(VisualElement content)
        {
            string devKey = "";
            try { devKey = DevKeyManager.GetPublicKeyBase64(); } catch { }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;

            var keyLabel = new Label("Dev key:");
            keyLabel.style.fontSize  = 11;
            keyLabel.style.color     = TextMuted;
            keyLabel.style.marginRight = 6;
            keyLabel.style.flexShrink = 0;
            row.Add(keyLabel);

            string truncated = devKey.Length > 8 ? devKey.Substring(0, 8) + "\u2026" : devKey;
            var keyValue = new Label(truncated);
            keyValue.style.fontSize  = 11;
            keyValue.style.color     = TextPri;
            keyValue.style.flexGrow  = 1;
            row.Add(keyValue);

            var copyBtn = YUCPUIToolkitHelper.CreateButton("Copy", () =>
            {
                EditorGUIUtility.systemCopyBuffer = devKey;
            }, YUCPUIToolkitHelper.ButtonVariant.Ghost);
            copyBtn.style.fontSize = 11;
            row.Add(copyBtn);

            content.Add(row);
        }

        private VisualElement CreateInfoRow(string label, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 6;

            var lbl = new Label(label + ":");
            lbl.style.fontSize  = 11;
            lbl.style.color     = TextMuted;
            lbl.style.width     = 90;
            lbl.style.flexShrink = 0;
            row.Add(lbl);

            var val = new Label(value);
            val.style.fontSize = 12;
            val.AddToClassList("yucp-info-value");
            val.style.flexGrow = 1;
            val.style.whiteSpace = WhiteSpace.Normal;
            val.style.color = TextPri;
            row.Add(val);

            return row;
        }

        // ─── Current package status section ───────────────────────────────────────

        private VisualElement CreateCurrentPackageStatus()
        {
            var section = new VisualElement();
            section.style.paddingLeft   = 12;
            section.style.paddingRight  = 12;
            section.style.paddingTop    = 12;
            section.style.paddingBottom = 12;
            section.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.3f);
            section.style.borderTopLeftRadius    = 6;
            section.style.borderTopRightRadius   = 6;
            section.style.borderBottomLeftRadius = 6;
            section.style.borderBottomRightRadius = 6;

            var title = new Label("Current Package");
            title.style.fontSize = 12;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            section.Add(title);

            var pkgInfo = new VisualElement();
            pkgInfo.style.flexDirection = FlexDirection.Column;
            pkgInfo.style.marginBottom  = 8;

            if (!string.IsNullOrEmpty(_profile.packageName))
            {
                var n = new Label(_profile.packageName);
                n.style.fontSize = 13;
                n.style.unityFontStyleAndWeight = FontStyle.Bold;
                pkgInfo.Add(n);
            }

            if (!string.IsNullOrEmpty(_profile.version))
            {
                var v = new Label($"Version {_profile.version}");
                v.style.fontSize   = 11;
                v.style.color      = TextMuted;
                v.style.marginTop  = 4;
                pkgInfo.Add(v);
            }
            section.Add(pkgInfo);

            YUCPUIToolkitHelper.AddSpacing(section, 12);

            // Product IDs
            var idsTitle = new Label("Product IDs");
            idsTitle.style.fontSize = 11;
            idsTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            idsTitle.style.marginBottom = 6;
            section.Add(idsTitle);

            section.Add(CreateProductIdField("Gumroad:", _profile.gumroadProductId ?? "",
                val => { if (_profile != null) { Undo.RecordObject(_profile, "Change Gumroad ID"); _profile.gumroadProductId = val; EditorUtility.SetDirty(_profile); } }));

            section.Add(CreateProductIdField("Jinxxy:", _profile.jinxxyProductId ?? "",
                val => { if (_profile != null) { Undo.RecordObject(_profile, "Change Jinxxy ID"); _profile.jinxxyProductId = val; EditorUtility.SetDirty(_profile); } }));

            // Package server status
            if (!string.IsNullOrEmpty(_profile.packageId))
            {
                var loadingLbl = new Label("Checking package status\u2026");
                loadingLbl.style.fontSize = 11;
                loadingLbl.style.color    = TextMuted;
                section.Add(loadingLbl);

                PackageInfoService.GetPublisherPackages(
                    packages =>
                    {
                        EditorApplication.delayCall += () =>
                        {
                            if (_root == null) return;
                            loadingLbl.RemoveFromHierarchy();

                            var pkg = packages?.FirstOrDefault(p =>
                                !string.IsNullOrEmpty(p.packageId) && p.packageId == _profile.packageId);

                            if (pkg == null)
                            {
                                var notFound = new Label("Package not found in registry");
                                notFound.style.fontSize = 11;
                                notFound.style.color    = TextMuted;
                                section.Add(notFound);
                                return;
                            }

                            section.Add(CreateStatusBadge(pkg.status));

                            if (!string.IsNullOrEmpty(pkg.createdAt) &&
                                DateTime.TryParse(pkg.createdAt, out DateTime created))
                            {
                                var dateLabel = new Label($"Signed on {created:MMM dd, yyyy}");
                                dateLabel.style.fontSize = 11;
                                dateLabel.style.color    = TextMuted;
                                section.Add(dateLabel);
                            }

                            if (pkg.status == "active")
                            {
                                YUCPUIToolkitHelper.AddSpacing(section, 8);
                                var revokeBtn = YUCPUIToolkitHelper.CreateButton(
                                    "Revoke Package",
                                    () =>
                                    {
                                        if (EditorUtility.DisplayDialog("Revoke Package",
                                            $"Mark this package as revoked? It will fail verification.",
                                            "Revoke", "Cancel"))
                                        {
                                            PackageInfoService.RevokePackage(pkg.packageId, "Revoked by publisher",
                                                () => EditorApplication.delayCall += RefreshUI,
                                                err => EditorUtility.DisplayDialog("Revoke Failed", err, "OK"));
                                        }
                                    }, YUCPUIToolkitHelper.ButtonVariant.Ghost);
                                revokeBtn.style.width = Length.Percent(100);
                                section.Add(revokeBtn);
                            }
                            else if (pkg.status == "revoked" && !string.IsNullOrEmpty(pkg.reason))
                            {
                                var revokedLbl = new Label($"Revoked: {pkg.reason}");
                                revokedLbl.style.fontSize   = 11;
                                revokedLbl.style.color      = new Color(0.89f, 0.50f, 0.29f);
                                revokedLbl.style.marginTop  = 8;
                                section.Add(revokedLbl);
                            }
                        };
                    },
                    err =>
                    {
                        EditorApplication.delayCall += () =>
                        {
                            if (_root == null) return;
                            loadingLbl.RemoveFromHierarchy();
                            var errLbl = new Label($"Status check failed: {err}");
                            errLbl.style.fontSize = 11;
                            errLbl.style.color    = new Color(0.89f, 0.40f, 0.40f);
                            section.Add(errLbl);
                        };
                    });
            }
            else
            {
                var notSigned = new Label("This package has not been signed yet. It will be signed when exported.");
                notSigned.style.fontSize  = 11;
                notSigned.style.color     = TextMuted;
                notSigned.style.marginTop = 8;
                section.Add(notSigned);
            }

            return section;
        }

        private VisualElement CreateProductIdField(string labelText, string value, Action<string> onChange)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 6;

            var lbl = new Label(labelText);
            lbl.style.fontSize   = 10;
            lbl.style.color      = TextMuted;
            lbl.style.width      = 80;
            lbl.style.flexShrink = 0;
            row.Add(lbl);

            var field = new TextField { value = value };
            field.AddToClassList("yucp-input");
            field.style.fontSize = 11;
            field.style.flexGrow = 1;
            field.RegisterValueChangedCallback(evt => onChange?.Invoke(evt.newValue));
            row.Add(field);

            return row;
        }

        private VisualElement CreateStatusBadge(string status)
        {
            var badge = new VisualElement();
            badge.style.paddingLeft   = 8;
            badge.style.paddingRight  = 8;
            badge.style.paddingTop    = 4;
            badge.style.paddingBottom = 4;
            badge.style.borderTopLeftRadius    = 4;
            badge.style.borderTopRightRadius   = 4;
            badge.style.borderBottomLeftRadius = 4;
            badge.style.borderBottomRightRadius = 4;
            badge.style.alignSelf = Align.FlexStart;
            badge.style.marginBottom = 6;

            var label = new Label(status.ToUpper());
            label.style.fontSize = 10;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;

            switch (status)
            {
                case "active":
                    badge.style.backgroundColor = new Color(0.21f, 0.75f, 0.69f, 0.2f);
                    label.style.color = Teal;
                    break;
                case "revoked":
                    badge.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f, 0.3f);
                    label.style.color = new Color(0.9f, 0.4f, 0.4f);
                    break;
                default:
                    badge.style.backgroundColor = new Color(0.5f, 0.5f, 0.2f, 0.3f);
                    label.style.color = new Color(0.9f, 0.8f, 0.4f);
                    break;
            }

            badge.Add(label);
            return badge;
        }

        // ─── Action handlers ───────────────────────────────────────────────────────

        private void OnSignOutClicked()
        {
            YucpOAuthService.SignOut();
            RefreshUI();
        }

        private void OnRequestCertClicked()
        {
            if (_isRequestingCert) return;

            string accessToken = YucpOAuthService.GetAccessToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                EditorUtility.DisplayDialog("Not Signed In", "Please sign in before requesting a certificate.", "OK");
                return;
            }

            string devPublicKey   = DevKeyManager.GetPublicKeyBase64();
            string publisherName  = YucpOAuthService.GetDisplayName() ?? "YUCP Creator";
            string serverUrl      = GetServerUrl();
            var    service        = new PackageSigningService(serverUrl);

            UnityEngine.Debug.Log($"[YUCP Cert] devPublicKey={(string.IsNullOrEmpty(devPublicKey) ? "(empty!)" : devPublicKey.Substring(0, Math.Min(20, devPublicKey.Length)) + "…")}" +
                                  $" publisherName={publisherName}" +
                                  $" serverUrl={serverUrl}");

            _isRequestingCert = true;
            RefreshUI();

            _ = service.RequestCertificateAsync(accessToken, devPublicKey, publisherName)
                .ContinueWith(task =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        _isRequestingCert = false;
                        var (success, error, certJson) = task.Result;

                        if (!success)
                        {
                            RefreshUI();
                            EditorUtility.DisplayDialog("Certificate Request Failed", error, "OK");
                            return;
                        }

                        var result = CertificateManager.ImportAndVerifyFromJson(certJson);
                        if (result.valid)
                        {
                            LoadSettings();
                            RefreshUI();
                            EditorUtility.DisplayDialog("Certificate Issued",
                                $"Signing certificate issued!\n\nPublisher: {result.publisherName}\nExpires: {result.expiresAt:MMM dd, yyyy}",
                                "OK");
                        }
                        else
                        {
                            RefreshUI();
                            EditorUtility.DisplayDialog("Certificate Error",
                                $"Server issued a certificate but verification failed:\n{result.error}", "OK");
                        }
                    };
                });
        }

        // ─── Utilities ─────────────────────────────────────────────────────────────

        private void LoadSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:SigningSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _settings = AssetDatabase.LoadAssetAtPath<SigningSettings>(path);
            }
        }

        private string GetServerUrl()
        {
            if (!string.IsNullOrEmpty(_settings?.serverUrl)) return _settings.serverUrl;
            string fromService = PackageSigningService.GetServerUrl();
            return !string.IsNullOrEmpty(fromService) ? fromService : "https://signing.yucp.club";
        }
    }
}
