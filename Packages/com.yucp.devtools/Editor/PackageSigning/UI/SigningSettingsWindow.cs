using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.DevTools.Editor.PackageSigning.Core;
using YUCP.DevTools.Editor.PackageSigning.Data;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.DevTools.Editor.PackageSigning.UI
{
    /// <summary>
    /// Signing-settings window. Normally opened via project settings redirect.
    /// Cards: (1) Account/OAuth  (2) Developer Key  (3) Server Config  (4) Root Public Key
    /// </summary>
    public class SigningSettingsWindow : EditorWindow
    {
        private SigningSettings _settings;
        private string          _devPublicKeyDisplay = "";
        private ScrollView      _scrollView;
        private bool            _isSigningIn;
        private bool            _isRequestingCert;

        [MenuItem("Tools/YUCP/Others/Development/Package Signing Settings", false, 100)]
        public static void ShowWindow()
        {
            SettingsService.OpenProjectSettings("Project/YUCP Package Exporter");
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);

            var devtoolsStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.devtools/Editor/Styles/YucpDesignSystem.uss");
            if (devtoolsStyle != null)
                root.styleSheets.Add(devtoolsStyle);

            root.style.backgroundColor = new Color(0.082f, 0.082f, 0.082f);

            _scrollView = new ScrollView();
            _scrollView.AddToClassList("yucp-scrollview");
            root.Add(_scrollView);

            LoadSettings();
            RefreshDevKey();
            BuildUI();
        }

        private void OnEnable()
        {
            // Guard against Unity calling OnEnable before CreateGUI
            if (_scrollView == null) return;
            LoadSettings();
            RefreshDevKey();
            BuildUI();
        }

        // ──────────────────────────────────────────────────────────────
        //  Main builder
        // ──────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            if (_scrollView == null) return;
            _scrollView.Clear();

            // Header bar
            var header = new VisualElement();
            header.style.flexDirection   = FlexDirection.Row;
            header.style.justifyContent  = Justify.SpaceBetween;
            header.style.alignItems      = Align.Center;
            header.style.marginBottom    = 20;
            header.style.paddingBottom   = 12;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f);

            var title = new Label("Package Signing Configuration");
            title.AddToClassList("yucp-section-title");
            title.style.marginBottom = 0;
            header.Add(title);
            _scrollView.Add(header);

            // Card 1 – Account / OAuth
            _scrollView.Add(CreateAccountCard());

            // Card 2 – Developer Key
            _scrollView.Add(CreateDevKeyCard());

            // Card 3 – Server Configuration
            if (_settings != null)
                _scrollView.Add(CreateServerConfigCard());

            // Card 4 – YUCP Root Public Key
            if (_settings != null)
                _scrollView.Add(CreateRootKeyCard());
        }

        // ──────────────────────────────────────────────────────────────
        //  Card 1: Account
        // ──────────────────────────────────────────────────────────────

        private VisualElement CreateAccountCard()
        {
            var card    = YUCPUIToolkitHelper.CreateCard("YUCP Account", "Creator identity for package signing");
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            if (!YucpOAuthService.IsSignedIn())
                BuildSignInHero(content);
            else
            {
                YucpOAuthService.TryBeginBackgroundRefresh(GetServerUrl(), BuildUI);
                BuildSignedInSection(content);
            }

            return card;
        }

        // --- Not signed in: gorgeous hero inside card content ---

        private void BuildSignInHero(VisualElement content)
        {
            // Teal accent strip
            var strip = new VisualElement();
            strip.style.height          = 3;
            strip.style.marginLeft      = -12;   // bleed to card edges
            strip.style.marginRight     = -12;
            strip.style.marginTop       = -12;
            strip.style.marginBottom    = 0;
            strip.style.backgroundColor = new Color(0.21f, 0.75f, 0.69f);
            content.Add(strip);

            // Inner padded container
            var inner = new VisualElement();
            inner.style.paddingTop    = 24;
            inner.style.paddingBottom = 0;
            content.Add(inner);

            // Category label
            var category = new Label("CREATOR IDENTITY");
            category.style.fontSize                = 10;
            category.style.unityFontStyleAndWeight = FontStyle.Bold;
            category.style.letterSpacing           = 1.5f;
            category.style.color                   = new Color(0.21f, 0.75f, 0.69f);
            category.style.marginBottom            = 14;
            inner.Add(category);

            // Heading
            var heading = new Label("Sign in with your YUCP creator account");
            heading.style.fontSize                = 18;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.color                   = Color.white;
            heading.style.whiteSpace              = WhiteSpace.Normal;
            heading.style.marginBottom            = 10;
            inner.Add(heading);

            // Body
            var body = new Label(
                "Issues a 90-day signing certificate bound to your YUCP identity.\n" +
                "Packages are verified by the YUCP ecosystem.");
            body.style.fontSize     = 13;
            body.style.color        = new Color(0.502f, 0.502f, 0.502f);
            body.style.whiteSpace   = WhiteSpace.Normal;
            body.style.marginBottom = 24;
            inner.Add(body);

            if (_isSigningIn)
            {
                // Loading state
                var loadingRow = new VisualElement();
                loadingRow.style.flexDirection         = FlexDirection.Row;
                loadingRow.style.alignItems            = Align.Center;
                loadingRow.style.justifyContent        = Justify.Center;
                loadingRow.style.height                = 64;
                loadingRow.style.backgroundColor       = new Color(0.145f, 0.145f, 0.145f);
                loadingRow.style.borderTopLeftRadius   = 16;
                loadingRow.style.borderTopRightRadius  = 16;
                loadingRow.style.borderBottomLeftRadius= 16;
                loadingRow.style.borderBottomRightRadius=16;

                var dots = new Label("●  ●  ●");
                dots.style.fontSize    = 13;
                dots.style.color       = new Color(0.21f, 0.75f, 0.69f);
                dots.style.marginRight = 14;
                loadingRow.Add(dots);

                var signingLbl = new Label("Signing in…");
                signingLbl.style.fontSize = 14;
                signingLbl.style.color    = new Color(0.502f, 0.502f, 0.502f);
                loadingRow.Add(signingLbl);

                inner.Add(loadingRow);
                YUCPUIToolkitHelper.AddSpacing(inner, 12);

                var cancelBtn = YUCPUIToolkitHelper.CreateButton(
                    "Cancel",
                    () => { _isSigningIn = false; BuildUI(); },
                    YUCPUIToolkitHelper.ButtonVariant.Ghost);
                cancelBtn.style.alignSelf = Align.Center;
                inner.Add(cancelBtn);
            }
            else
            {
                // Sign-in button (VisualElement for full visual control)
                var signInBtn = new VisualElement();
                signInBtn.style.backgroundColor         = new Color(0.961f, 0.961f, 0.961f);
                signInBtn.style.height                  = 64;
                signInBtn.style.borderTopLeftRadius     = 16;
                signInBtn.style.borderTopRightRadius    = 16;
                signInBtn.style.borderBottomLeftRadius  = 16;
                signInBtn.style.borderBottomRightRadius = 16;
                signInBtn.style.paddingLeft             = 24;
                signInBtn.style.paddingRight            = 28;
                signInBtn.style.flexDirection           = FlexDirection.Row;
                signInBtn.style.alignItems              = Align.Center;
                signInBtn.style.justifyContent          = Justify.FlexStart;
                signInBtn.style.width                   = Length.Percent(100);

                // Avatar circle
                var avatar = new VisualElement();
                avatar.style.width                   = 46;
                avatar.style.height                  = 46;
                avatar.style.borderTopLeftRadius     = 23;
                avatar.style.borderTopRightRadius    = 23;
                avatar.style.borderBottomLeftRadius  = 23;
                avatar.style.borderBottomRightRadius = 23;
                avatar.style.backgroundColor         = new Color(0.21f, 0.75f, 0.69f);
                avatar.style.alignItems              = Align.Center;
                avatar.style.justifyContent          = Justify.Center;
                avatar.style.marginRight             = 18;
                avatar.style.flexShrink              = 0;

                var lockLbl = new Label("[lock]");
                lockLbl.style.fontSize       = 20;
                lockLbl.style.color          = Color.white;
                lockLbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                avatar.Add(lockLbl);
                signInBtn.Add(avatar);

                // Text column
                var textCol = new VisualElement();
                textCol.style.flexDirection  = FlexDirection.Column;
                textCol.style.justifyContent = Justify.Center;

                var mainLbl = new Label("Sign in as Creator");
                mainLbl.style.fontSize                = 16;
                mainLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                mainLbl.style.color                   = new Color(0.102f, 0.102f, 0.102f);
                textCol.Add(mainLbl);

                var subLbl = new Label("Continue with YUCP");
                subLbl.style.fontSize = 11;
                subLbl.style.color    = new Color(0.40f, 0.40f, 0.40f);
                textCol.Add(subLbl);

                signInBtn.Add(textCol);

                // Hover effect
                signInBtn.RegisterCallback<MouseEnterEvent>(_ => signInBtn.style.opacity = 0.9f);
                signInBtn.RegisterCallback<MouseLeaveEvent>(_ => signInBtn.style.opacity = 1.0f);
                signInBtn.RegisterCallback<ClickEvent>(_ => OnSignInClicked());

                inner.Add(signInBtn);
            }

            // Security hint row
            YUCPUIToolkitHelper.AddSpacing(inner, 14);
            var hintRow = new VisualElement();
            hintRow.style.flexDirection  = FlexDirection.Row;
            hintRow.style.alignItems     = Align.Center;
            hintRow.style.justifyContent = Justify.Center;

            var hintTxt = new Label("Secure OAuth 2.0 + PKCE · Opens your browser");
            hintTxt.style.fontSize       = 10;
            hintTxt.style.color          = new Color(0.29f, 0.29f, 0.29f);
            hintTxt.style.unityTextAlign = TextAnchor.MiddleCenter;
            hintRow.Add(hintTxt);
            inner.Add(hintRow);
        }

        // --- Signed in: account row + cert section ---

        private void BuildSignedInSection(VisualElement content)
        {
            var section = new VisualElement();
            section.style.paddingTop    = 4;
            section.style.paddingBottom = 0;

            // Account row
            var accountRow = new VisualElement();
            accountRow.style.flexDirection    = FlexDirection.Row;
            accountRow.style.alignItems       = Align.Center;
            accountRow.style.justifyContent   = Justify.SpaceBetween;
            accountRow.style.paddingBottom    = 16;
            accountRow.style.borderBottomWidth= 1;
            accountRow.style.borderBottomColor= new Color(0.165f, 0.165f, 0.165f);
            accountRow.style.marginBottom     = 16;

            var left = new VisualElement();
            left.style.flexDirection = FlexDirection.Row;
            left.style.alignItems    = Align.Center;

            // Avatar circle with initial letter
            string displayName = YucpOAuthService.GetDisplayName() ?? "Creator";
            string initial     = displayName.Length > 0 ? displayName.Substring(0, 1).ToUpper() : "C";

            var avatarCircle = new VisualElement();
            avatarCircle.style.width                   = 44;
            avatarCircle.style.height                  = 44;
            avatarCircle.style.borderTopLeftRadius     = 22;
            avatarCircle.style.borderTopRightRadius    = 22;
            avatarCircle.style.borderBottomLeftRadius  = 22;
            avatarCircle.style.borderBottomRightRadius = 22;
            avatarCircle.style.backgroundColor         = new Color(0.21f, 0.75f, 0.69f, 0.15f);
            avatarCircle.style.borderTopWidth          = 2;
            avatarCircle.style.borderBottomWidth       = 2;
            avatarCircle.style.borderLeftWidth         = 2;
            avatarCircle.style.borderRightWidth        = 2;
            avatarCircle.style.borderTopColor          = new Color(0.21f, 0.75f, 0.69f);
            avatarCircle.style.borderBottomColor       = new Color(0.21f, 0.75f, 0.69f);
            avatarCircle.style.borderLeftColor         = new Color(0.21f, 0.75f, 0.69f);
            avatarCircle.style.borderRightColor        = new Color(0.21f, 0.75f, 0.69f);
            avatarCircle.style.alignItems              = Align.Center;
            avatarCircle.style.justifyContent          = Justify.Center;
            avatarCircle.style.marginRight             = 14;

            var initialLbl = new Label(initial);
            initialLbl.style.fontSize                = 18;
            initialLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            initialLbl.style.color                   = new Color(0.21f, 0.75f, 0.69f);
            avatarCircle.Add(initialLbl);
            left.Add(avatarCircle);

            // Info column
            var infoCol = new VisualElement();
            infoCol.style.flexDirection = FlexDirection.Column;

            var nameLbl = new Label(displayName);
            nameLbl.style.fontSize                = 14;
            nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLbl.style.color                   = Color.white;
            infoCol.Add(nameLbl);

            var roleLbl = new Label("YUCP Creator · Signed in");
            roleLbl.style.fontSize = 11;
            roleLbl.style.color    = new Color(0.21f, 0.75f, 0.69f);
            infoCol.Add(roleLbl);

            left.Add(infoCol);
            accountRow.Add(left);

            accountRow.Add(YUCPUIToolkitHelper.CreateButton(
                "Sign Out", OnSignOutClicked, YUCPUIToolkitHelper.ButtonVariant.Ghost));

            section.Add(accountRow);

            // Certificate area
            bool hasCert = _settings != null && _settings.HasValidCertificate();
            if (hasCert)
            {
                section.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Certificate is active and ready for signing.",
                    YUCPUIToolkitHelper.MessageType.Success));

                YUCPUIToolkitHelper.AddSpacing(section, 12);

                var details = new VisualElement();
                details.style.flexDirection = FlexDirection.Column;

                if (!string.IsNullOrEmpty(_settings.publisherName))
                    AddDetailRow(details, "Publisher", _settings.publisherName);

                if (!string.IsNullOrEmpty(_settings.publisherId))
                    AddDetailRow(details, "Publisher ID", _settings.publisherId);

                if (!string.IsNullOrEmpty(_settings.certificateExpiresAt) &&
                    DateTime.TryParse(_settings.certificateExpiresAt, out DateTime exp))
                    AddDetailRow(details, "Expires", exp.ToString("yyyy-MM-dd HH:mm") + " UTC");

                section.Add(details);
                YUCPUIToolkitHelper.AddSpacing(section, 12);

                var renewBtn = YUCPUIToolkitHelper.CreateButton(
                    "Renew Certificate",
                    OnRequestCertClicked,
                    YUCPUIToolkitHelper.ButtonVariant.Secondary);
                section.Add(renewBtn);

                YUCPUIToolkitHelper.AddSpacing(section, 8);

                section.Add(YUCPUIToolkitHelper.CreateButton(
                    "Remove Certificate", OnRemoveCert, YUCPUIToolkitHelper.ButtonVariant.Ghost));
            }
            else
            {
                section.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "No certificate yet. Request one to start signing.",
                    YUCPUIToolkitHelper.MessageType.Warning));

                YUCPUIToolkitHelper.AddSpacing(section, 16);

                var reqBtn = YUCPUIToolkitHelper.CreateButton(
                    "✦  Request Signing Certificate",
                    OnRequestCertClicked,
                    YUCPUIToolkitHelper.ButtonVariant.Primary);
                reqBtn.style.width = Length.Percent(100);
                reqBtn.SetEnabled(!_isRequestingCert);
                section.Add(reqBtn);

                if (_isRequestingCert)
                {
                    YUCPUIToolkitHelper.AddSpacing(section, 8);
                    var progLbl = new Label("Requesting…");
                    progLbl.style.fontSize       = 11;
                    progLbl.style.color          = new Color(0.502f, 0.502f, 0.502f);
                    progLbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                    section.Add(progLbl);
                }

                YUCPUIToolkitHelper.AddSpacing(section, 8);

                var infoTxt = new Label(
                    "Your Ed25519 dev key will be certified by the YUCP Authority. Certs expire after 90 days.");
                infoTxt.style.fontSize   = 11;
                infoTxt.style.color      = new Color(0.502f, 0.502f, 0.502f);
                infoTxt.style.whiteSpace = WhiteSpace.Normal;
                section.Add(infoTxt);

                // Divider
                YUCPUIToolkitHelper.AddSpacing(section, 14);
                var divider = new VisualElement();
                divider.style.height          = 1;
                divider.style.backgroundColor = new Color(0.165f, 0.165f, 0.165f);
                divider.style.marginBottom    = 10;
                section.Add(divider);

                // Import from file fallback
                var importRow = new VisualElement();
                importRow.style.flexDirection  = FlexDirection.Row;
                importRow.style.alignItems     = Align.Center;
                importRow.style.justifyContent = Justify.Center;

                var importLbl = new Label("Already have a .yucp_cert file?");
                importLbl.style.fontSize  = 11;
                importLbl.style.color     = new Color(0.45f, 0.45f, 0.45f);
                importLbl.style.marginRight = 6;
                importRow.Add(importLbl);

                importRow.Add(YUCPUIToolkitHelper.CreateButton(
                    "Import from file", ImportCertificateFromFile, YUCPUIToolkitHelper.ButtonVariant.Ghost));
                section.Add(importRow);
            }

            content.Add(section);
        }

        // ──────────────────────────────────────────────────────────────
        //  Card 2: Developer Key
        // ──────────────────────────────────────────────────────────────

        private VisualElement CreateDevKeyCard()
        {
            var card    = YUCPUIToolkitHelper.CreateCard("Developer Key", "Your Ed25519 public key");
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            content.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "Share this public key with admins to receive a signing certificate.",
                YUCPUIToolkitHelper.MessageType.Info));

            YUCPUIToolkitHelper.AddSpacing(content, 12);

            // Key field + copy button row
            var keyRow = new VisualElement();
            keyRow.style.flexDirection = FlexDirection.Row;
            keyRow.style.alignItems    = Align.FlexStart;

            var keyField = new TextField();
            keyField.value      = _devPublicKeyDisplay;
            keyField.isReadOnly = true;
            keyField.AddToClassList("yucp-input");
            keyField.style.flexGrow  = 1;
            keyField.style.fontSize  = 11;
            keyRow.Add(keyField);

            var copyBtn = YUCPUIToolkitHelper.CreateButton(
                "Copy",
                () =>
                {
                    EditorGUIUtility.systemCopyBuffer = _devPublicKeyDisplay;
                    Debug.Log("[YUCP] Dev public key copied to clipboard.");
                },
                YUCPUIToolkitHelper.ButtonVariant.Secondary);
            copyBtn.style.marginLeft = 8;
            keyRow.Add(copyBtn);

            content.Add(keyRow);
            YUCPUIToolkitHelper.AddSpacing(content, 8);

            // Action buttons
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.flexWrap      = Wrap.Wrap;

            btnRow.Add(YUCPUIToolkitHelper.CreateButton(
                "Refresh", RefreshDevKey, YUCPUIToolkitHelper.ButtonVariant.Ghost));

            btnRow.Add(YUCPUIToolkitHelper.CreateButton(
                "Generate New Key",
                () =>
                {
                    if (!EditorUtility.DisplayDialog(
                        "Generate New Key",
                        "This will replace your current developer key. You will need a new certificate.\n\nContinue?",
                        "Yes", "Cancel")) return;
                    RegenerateDevKey();
                    RefreshDevKey();
                    BuildUI();
                },
                YUCPUIToolkitHelper.ButtonVariant.Ghost));

            content.Add(btnRow);
            return card;
        }

        // ──────────────────────────────────────────────────────────────
        //  Card 3: Server Configuration
        // ──────────────────────────────────────────────────────────────

        private VisualElement CreateServerConfigCard()
        {
            var card    = YUCPUIToolkitHelper.CreateCard("Server Configuration", "Signing authority server settings");
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            var serverField = new TextField("Default Server URL");
            serverField.value = _settings.serverUrl;
            serverField.AddToClassList("yucp-input");
            serverField.RegisterValueChangedCallback(evt =>
            {
                _settings.serverUrl = evt.newValue;
                EditorUtility.SetDirty(_settings);
            });
            content.Add(serverField);

            YUCPUIToolkitHelper.AddSpacing(content, 20);

            // ── Certificate Providers ──────────────────────────────────────────────
            var providersLabel = new Label("Certificate Providers");
            providersLabel.AddToClassList("yucp-section-subtitle");
            content.Add(providersLabel);

            var providersHelp = new Label("Define named signing authorities. The first provider is used by default. Individual export profiles can override the server URL.");
            providersHelp.AddToClassList("yucp-label-secondary");
            providersHelp.style.whiteSpace = WhiteSpace.Normal;
            providersHelp.style.marginBottom = 10;
            content.Add(providersHelp);

            var providersContainer = new VisualElement();
            providersContainer.name = "providers-container";
            content.Add(providersContainer);

            void RebuildProviders()
            {
                providersContainer.Clear();
                var providers = _settings.certificateProviders;
                for (int i = 0; i < providers.Count; i++)
                {
                    int idx = i;
                    var provider = providers[idx];

                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Column;
                    row.style.borderTopWidth = 1;
                    row.style.borderTopColor = new Color(0.2f, 0.2f, 0.2f);
                    row.style.paddingTop = 10;
                    row.style.paddingBottom = 10;
                    row.style.marginBottom = 4;

                    var rowHeader = new VisualElement();
                    rowHeader.style.flexDirection = FlexDirection.Row;
                    rowHeader.style.justifyContent = Justify.SpaceBetween;
                    rowHeader.style.alignItems = Align.Center;
                    rowHeader.style.marginBottom = 6;
                    row.Add(rowHeader);

                    var indexLabel = new Label($"Provider {idx + 1}{(idx == 0 ? " (default)" : "")}");
                    indexLabel.AddToClassList("yucp-label");
                    rowHeader.Add(indexLabel);

                    var removeBtn = new Button(() =>
                    {
                        _settings.certificateProviders.RemoveAt(idx);
                        EditorUtility.SetDirty(_settings);
                        RebuildProviders();
                    }) { text = "Remove" };
                    removeBtn.AddToClassList("yucp-button-secondary");
                    removeBtn.style.fontSize = 11;
                    rowHeader.Add(removeBtn);

                    var nameField = new TextField("Name") { value = provider.name };
                    nameField.AddToClassList("yucp-input");
                    nameField.RegisterValueChangedCallback(evt =>
                    {
                        _settings.certificateProviders[idx].name = evt.newValue;
                        EditorUtility.SetDirty(_settings);
                    });
                    row.Add(nameField);

                    YUCPUIToolkitHelper.AddSpacing(row, 4);

                    var urlField = new TextField("Server URL") { value = provider.serverUrl };
                    urlField.AddToClassList("yucp-input");
                    urlField.RegisterValueChangedCallback(evt =>
                    {
                        _settings.certificateProviders[idx].serverUrl = evt.newValue;
                        EditorUtility.SetDirty(_settings);
                    });
                    row.Add(urlField);

                    YUCPUIToolkitHelper.AddSpacing(row, 4);

                    var keyField = new TextField("Root Public Key (base64)") { value = provider.rootPublicKeyBase64 };
                    keyField.AddToClassList("yucp-input");
                    keyField.RegisterValueChangedCallback(evt =>
                    {
                        _settings.certificateProviders[idx].rootPublicKeyBase64 = evt.newValue;
                        EditorUtility.SetDirty(_settings);
                    });
                    row.Add(keyField);

                    providersContainer.Add(row);
                }
            }

            RebuildProviders();

            YUCPUIToolkitHelper.AddSpacing(content, 8);

            content.Add(YUCPUIToolkitHelper.CreateButton(
                "+ Add Provider",
                () =>
                {
                    _settings.certificateProviders.Add(new CertificateProvider
                    {
                        name = "New Provider",
                        serverUrl = "https://api.creators.yucp.club",
                        rootPublicKeyBase64 = ""
                    });
                    EditorUtility.SetDirty(_settings);
                    RebuildProviders();
                },
                YUCPUIToolkitHelper.ButtonVariant.Secondary));

            YUCPUIToolkitHelper.AddSpacing(content, 16);

            content.Add(YUCPUIToolkitHelper.CreateButton(
                "Save Settings",
                () =>
                {
                    EditorUtility.SetDirty(_settings);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[YUCP] Signing settings saved.");
                },
                YUCPUIToolkitHelper.ButtonVariant.Primary));

            return card;
        }

        // ──────────────────────────────────────────────────────────────
        //  Card 4: YUCP Root Public Key
        // ──────────────────────────────────────────────────────────────

        private VisualElement CreateRootKeyCard()
        {
            var card    = YUCPUIToolkitHelper.CreateCard("YUCP Root Public Key", "Authority verification key");
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            content.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "The root public key for verifying certificates. Set this from server configuration.",
                YUCPUIToolkitHelper.MessageType.Info));

            YUCPUIToolkitHelper.AddSpacing(content, 12);

            var rootField = new TextField();
            rootField.multiline = true;
            rootField.value     = _settings.yucpRootPublicKeyBase64 ?? "";
            rootField.AddToClassList("yucp-input");
            rootField.AddToClassList("yucp-input-multiline");
            rootField.style.minHeight = 80;
            rootField.style.fontSize  = 11;
            rootField.RegisterValueChangedCallback(evt =>
            {
                _settings.yucpRootPublicKeyBase64 = evt.newValue;
                EditorUtility.SetDirty(_settings);
            });
            content.Add(rootField);

            return card;
        }

        // ──────────────────────────────────────────────────────────────
        //  Event handlers
        // ──────────────────────────────────────────────────────────────

        private void OnSignInClicked()
        {
            if (_isSigningIn) return;
            _isSigningIn = true;
            BuildUI();

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
                    BuildUI();
                },
                onError: err => EditorApplication.delayCall += () =>
                {
                    _isSigningIn = false;
                    BuildUI();
                    EditorUtility.DisplayDialog("Sign-in Failed", err, "OK");
                });
        }

        private void OnSignOutClicked()
        {
            YucpOAuthService.SignOut();
            BuildUI();
        }

        private async void OnRequestCertClicked()
        {
            if (_isRequestingCert) return;
            _isRequestingCert = true;
            BuildUI();

            string serverUrl = GetServerUrl();
            string accessToken = await YucpOAuthService.GetValidAccessTokenAsync(serverUrl);
            if (string.IsNullOrEmpty(accessToken))
            {
                _isRequestingCert = false;
                BuildUI();
                EditorUtility.DisplayDialog("Certificate Request Failed", "Please sign in before requesting a certificate.", "OK");
                return;
            }

            string devPublicKey  = DevKeyManager.GetPublicKeyBase64();
            string publisherName = YucpOAuthService.GetDisplayName() ?? "YUCP Creator";
            var    service       = new PackageSigningService(serverUrl);

            try
            {
                var (success, error, certJson) = await service.RequestCertificateAsync(accessToken, devPublicKey, publisherName);
                if (!success)
                {
                    _isRequestingCert = false;
                    BuildUI();
                    EditorUtility.DisplayDialog("Certificate Request Failed", error ?? "Unknown error.", "OK");
                    return;
                }

                var result = CertificateManager.ImportAndVerifyFromJson(certJson);
                if (result.valid)
                {
                    EditorUtility.DisplayDialog(
                        "Certificate Issued!",
                        $"Your signing certificate has been issued.\n\n" +
                        $"Publisher: {result.publisherName}\n" +
                        $"Expires: {result.expiresAt:MMM dd, yyyy}",
                        "OK");
                    _isRequestingCert = false;
                    LoadSettings();
                    BuildUI();
                    return;
                }

                _isRequestingCert = false;
                BuildUI();
                EditorUtility.DisplayDialog(
                    "Certificate Invalid",
                    $"Certificate was issued but failed verification:\n\n{result.error}",
                    "OK");
            }
            catch (Exception ex)
            {
                _isRequestingCert = false;
                BuildUI();
                EditorUtility.DisplayDialog("Certificate Request Failed", ex.Message, "OK");
            }
        }

        private void OnRemoveCert()
        {
            if (!EditorUtility.DisplayDialog(
                "Remove Certificate?",
                "Packages will not be signed until you request or import a new certificate.\n\nRemove it?",
                "Remove", "Cancel")) return;

            if (_settings == null) return;
            _settings.ClearCertificate();
            EditorUtility.SetDirty(_settings);
            AssetDatabase.SaveAssets();
            BuildUI();
        }

        private void ImportCertificateFromFile()
        {
            string path = EditorUtility.OpenFilePanel("Import YUCP Certificate", "", "yucp_cert");
            if (string.IsNullOrEmpty(path)) return;

            var result = CertificateManager.ImportAndVerify(path);
            if (result.valid)
            {
                EditorUtility.DisplayDialog(
                    "Certificate Imported",
                    $"Certificate imported successfully!\n\n" +
                    $"Publisher: {result.publisherName}\n" +
                    $"Expires: {result.expiresAt:yyyy-MM-dd HH:mm:ss UTC}",
                    "OK");
                LoadSettings();
                BuildUI();
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Certificate Import Failed",
                    $"Failed to import certificate:\n\n{result.error}",
                    "OK");
            }
        }

        // ──────────────────────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────────────────────

        private void LoadSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:SigningSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _settings = AssetDatabase.LoadAssetAtPath<SigningSettings>(path);
            }
            else
            {
                _settings = ScriptableObject.CreateInstance<SigningSettings>();
                string settingsPath = "Assets/YUCP/SigningSettings.asset";
                string dir          = Path.GetDirectoryName(settingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                AssetDatabase.CreateAsset(_settings, settingsPath);
                AssetDatabase.SaveAssets();
            }
        }

        private void RefreshDevKey()
        {
            try   { _devPublicKeyDisplay = DevKeyManager.GetPublicKeyBase64() ?? ""; }
            catch { _devPublicKeyDisplay = "Error loading dev key. Generate a new key."; }
        }

        private void RegenerateDevKey()
        {
            try
            {
                string keyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".unitysign", "devkey.json");
                if (File.Exists(keyPath)) File.Delete(keyPath);

                // Clear the static cache via reflection
                var field = typeof(DevKeyManager).GetField(
                    "_cachedKeyPair", BindingFlags.NonPublic | BindingFlags.Static);
                field?.SetValue(null, null);

                DevKeyManager.GetOrCreateDevKey();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP] Failed to regenerate dev key: {ex.Message}");
            }
        }

        private void AddDetailRow(VisualElement container, string label, string value)
        {
            if (string.IsNullOrEmpty(value)) return;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 6;

            var lbl = new Label(label + ":");
            lbl.style.width = 140;
            lbl.AddToClassList("yucp-label-secondary");
            row.Add(lbl);

            var val = new Label(value);
            val.style.flexGrow = 1;
            val.AddToClassList("yucp-label");
            row.Add(val);

            container.Add(row);
        }

        private string GetServerUrl() =>
            _settings?.serverUrl ?? "https://api.creators.yucp.club";
    }
}
