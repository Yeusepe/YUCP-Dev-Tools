using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.DevTools.Editor.PackageSigning.Core;
using YUCP.DevTools.Editor.PackageSigning.Data;
using YUCP.DevTools.Editor.PackageExporter;

namespace YUCP.DevTools.Editor.PackageSigning.UI
{
    /// <summary>
    /// Package Signing section rendered inside the Package Exporter window.
    ///
    /// States:
    ///   1. Not signed in  — hero sign-in card
    ///   2. Signed in, no cert — account bar + "Get Certificate" action
    ///   3. Signed in, cert active — live status + cert health + package info
    /// </summary>
    public class PackageSigningTab
    {
        private VisualElement _root;
        private SigningSettings _settings;
        private readonly ExportProfile _profile;
        private bool _isSigningIn;
        private bool _isRequestingCert;
        private bool _isLoadingAccountState;
        private string _accountStateServerUrl;
        private double _accountStateRefreshedAt;
        private PackageSigningService.CertificateAccountState _accountState;
        private const double AccountStateRefreshIntervalSeconds = 15d;
        private const string ProtectedExportsCapabilityKey = "protected_exports";

        // ── Product catalog cache ──────────────────────────────────────────────────
        // Loaded once per sign-in from GET /v1/products. The server groups by canonical
        // productId so one entry = one logical product across all providers.
        private class CanonicalProduct
        {
            public string productId;
            public List<string> productIds = new List<string>();
            public string displayName;
            public string owner;   // null = own product; non-null = collaborator's product (owner's name)
            public List<ProviderRef> providers = new List<ProviderRef>();
            public bool configured;
            public bool live;
            public bool localConfigured;
            public string GetRef(string p) { foreach (var r in providers) if (r.provider == p) return r.providerRef; return null; }
        }
        private struct ProviderRef { public string provider; public string providerRef; }
        private List<CanonicalProduct> _canonicalProducts;
        private bool  _productsLoading;
        private string _productLoadErrorTitle;
        private string _productLoadErrorMessage;
        private bool  _productPickerExpanded;
        private VisualElement _productPickerSlot;
        private VisualElement _productListPanel;
        private string _productSearchQuery = "";
        private string _productProviderFilter = AllProvidersFilter;
        private string _productSourceFilter = AllSourcesFilter;

        private const string AllProvidersFilter = "All providers";
        private const string AllSourcesFilter = "All products";
        private const string ConfiguredSourcesFilter = "Configured";
        private const string StoreSourcesFilter = "Store";
        private const string SelectedSourcesFilter = "Selected";

        // ── Design tokens ──────────────────────────────────────────────────────────
        // Use the same lighter surface as the rest of the Package Exporter UI
        private static readonly Color Surface      = new Color(0.118f, 0.118f, 0.118f); // #1E1E1E
        private static readonly Color SurfaceRaise = new Color(0.138f, 0.138f, 0.138f); // slightly raised surface
        private static readonly Color Border       = new Color(0.157f, 0.157f, 0.157f); // #282828
        private static readonly Color BorderFaint  = new Color(0.118f, 0.118f, 0.118f); // #1E1E1E
        private static readonly Color Teal         = new Color(0.212f, 0.749f, 0.694f); // #36BFB1
        private static readonly Color TealGlow     = new Color(0.212f, 0.749f, 0.694f, 0.08f);
        private static readonly Color TealSub      = new Color(0.212f, 0.749f, 0.694f, 0.14f);
        private static readonly Color Amber        = new Color(0.910f, 0.659f, 0.294f); // #E8A84B
        private static readonly Color Red          = new Color(0.910f, 0.294f, 0.294f); // #E84B4B
        private static readonly Color TextPri      = new Color(0.961f, 0.961f, 0.961f); // #F5F5F5
        private static readonly Color TextSec      = new Color(0.549f, 0.549f, 0.549f); // #8C8C8C
        private static readonly Color TextMute     = new Color(0.302f, 0.302f, 0.302f); // #4D4D4D

        private Action _onProfileChanged;

        public PackageSigningTab(ExportProfile profile = null, Action onProfileChanged = null)
        {
            _profile = profile;
            _onProfileChanged = onProfileChanged;
        }

        public VisualElement CreateUI()
        {
            _root = new VisualElement();
            _root.name = "PackageSigningTab";
            LoadSettings();
            _root.Add(BuildCard());
            return _root;
        }

        public void RefreshUI()
        {
            if (_root == null) return;
            _root.Clear();
            LoadSettings();
            _root.Add(BuildCard());
        }

        public bool CanSign()
        {
            LoadSettings();
            if (!YucpOAuthService.IsSignedIn() || _settings == null || !_settings.HasValidCertificate())
                return false;

            return _accountState?.billing == null || _accountState.billing.allowSigning;
        }

        // ── State dispatcher ───────────────────────────────────────────────────────

        private VisualElement BuildCard()
        {
            if (!YucpOAuthService.IsSignedIn())
            {
                var wrapper = new VisualElement();
                wrapper.Add(BuildSignInHero());
                if (_profile != null)
                    wrapper.Add(BuildDetachedLicenseSection());
                return wrapper;
            }

            YucpOAuthService.TryBeginBackgroundRefresh(GetServerUrl(), RefreshUI);
            EnsureAccountStateRefresh();
            bool hasCert = _settings != null && _settings.HasValidCertificate();

            if (hasCert)
                return BuildActiveCard(); // already includes license section

            var noCertWrapper = new VisualElement();
            noCertWrapper.Add(BuildNoCertCard());
            if (_profile != null)
                noCertWrapper.Add(BuildDetachedLicenseSection());
            return noCertWrapper;
        }

        /// <summary>
        /// A standalone license-protection card shown in states 1 &amp; 2 (not signed in / no cert),
        /// so the toggle remains accessible regardless of auth state.
        /// </summary>
        private VisualElement BuildDetachedLicenseSection()
        {
            var card = MakeCard();
            card.style.marginTop = 4;

            _licenseSectionSlot = new VisualElement();
            _licenseSectionSlot.Add(BuildLicenseProtectionSection());
            card.Add(_licenseSectionSlot);

            return card;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // STATE 1 — Sign-in hero
        // ══════════════════════════════════════════════════════════════════════════

        private VisualElement BuildSignInHero()
        {
            var card = MakeCard();

            // Top banner (use image if available, fallback to teal glow)
            const string bannerPath = "Packages/com.yucp.devtools/Editor/PackageSigning/Resources/Banner.png";
            var bannerTex = AssetDatabase.LoadAssetAtPath<Texture2D>(bannerPath);
            if (bannerTex != null)
            {
                // Use an Image with ScaleAndCrop so it fills horizontally without stretching
                var bannerImg = new Image { image = bannerTex, scaleMode = ScaleMode.ScaleAndCrop };
                bannerImg.style.width = Length.Percent(100);
                bannerImg.style.height = 80;
                bannerImg.style.borderTopLeftRadius  = 10;
                bannerImg.style.borderTopRightRadius = 10;
                bannerImg.style.overflow = Overflow.Hidden;
                card.Add(bannerImg);
            }
            else
            {
                var glow = new VisualElement();
                glow.style.height = 80;
                glow.style.backgroundColor = TealGlow;
                glow.style.borderTopLeftRadius  = 10;
                glow.style.borderTopRightRadius = 10;
                card.Add(glow);
            }

            var body = MakePad(24, 28, 24, 24);
            body.style.marginTop = -4; // overlap into glow
            card.Add(body);

            // Eyebrow
            body.Add(MakeLabel("CREATOR SIGNING", 9, Teal, bold: true, letterSpacing: 2f, mb: 14));

            // Headline — use camel case for trademarked phrase
            body.Add(MakeLabel("Sign packages with\nyour Creator Identity\u2122", 17, TextPri, bold: true, mb: 6, wrap: true));

            // Subtext — short, no jargon
            body.Add(MakeLabel("Verified packages build trust with your customers.", 12, TextSec, mb: 22, wrap: true));

            if (_isSigningIn)
                body.Add(BuildSigningInState());
            else
                body.Add(BuildSignInButton());

            return card;
        }

        private VisualElement BuildSignInButton()
        {
            var col = new VisualElement();
            // center the button and avoid it stretching to full width
            col.style.flexDirection = FlexDirection.Row;
            col.style.justifyContent = Justify.Center;
            col.style.alignItems = Align.Center;

            // White pill — loads image from resources
            var btn = new Button(() =>
            {
                if (_isSigningIn) return;
                _isSigningIn = true;
                RefreshUI();
#pragma warning disable CS4014
                YucpOAuthService.SignInAsync(GetServerUrl(),
                    onSuccess: () => EditorApplication.delayCall += () => { _isSigningIn = false; LoadSettings(); RefreshUI(); },
                    onError:   e  => EditorApplication.delayCall += () => { _isSigningIn = false; RefreshUI(); EditorUtility.DisplayDialog("Sign-in failed", e, "OK"); });
#pragma warning restore CS4014
            }) { focusable = true };
            btn.style.backgroundColor       = Color.white;
            // slightly smaller pill, tighter padding for a compact look
            btn.style.borderTopLeftRadius    = 14;
            btn.style.borderTopRightRadius   = 14;
            btn.style.borderBottomLeftRadius = 14;
            btn.style.borderBottomRightRadius = 14;
            btn.style.flexDirection  = FlexDirection.Row;
            btn.style.alignItems     = Align.Center;
            btn.style.justifyContent = Justify.Center;
            btn.style.paddingLeft    = 20;
            btn.style.paddingRight   = 20;
            btn.style.paddingTop     = 10;
            btn.style.paddingBottom  = 10;
            btn.style.alignSelf      = Align.Center;
            btn.style.minHeight      = 36;
            // fixed compact width so the control is a button, not a banner
            btn.style.width = 220; // px
            btn.style.flexGrow = 0;
            btn.style.flexShrink = 0;
            btn.style.flexShrink = 0;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;

            const string imgPath = "Packages/com.yucp.devtools/Editor/PackageSigning/Resources/Bag.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(imgPath);
            if (tex != null)
            {
                // Use a fixed icon size so it remains crisp and never stretches
                const int iconSize = 20;
                var wrap = new VisualElement();
                wrap.style.width  = iconSize;
                wrap.style.height = iconSize;
                wrap.style.flexShrink = 0;
                wrap.style.overflow  = Overflow.Hidden;
                wrap.style.alignItems = Align.Center;
                wrap.style.justifyContent = Justify.Center;
                var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit };
                // Fill the wrap while keeping aspect ratio
                img.style.width  = Length.Percent(100);
                img.style.height = Length.Percent(100);
                wrap.Add(img);
                btn.Add(wrap);
                var lbl = MakeLabel("Sign in as Creator", 12, new Color(0.08f, 0.08f, 0.08f), bold: true);
                lbl.style.marginLeft = 6;
                lbl.style.alignSelf = Align.Center;
                btn.Add(lbl);
            }
            else
            {
                // concise, slightly smaller label for the compact button
                btn.Add(MakeLabel("Sign in as Creator", 12, new Color(0.08f, 0.08f, 0.08f), bold: true));
            }

            // keep button compact
            btn.style.maxWidth = 320;

            btn.RegisterCallback<MouseEnterEvent>(_ => btn.style.opacity = 0.92f);
            btn.RegisterCallback<MouseLeaveEvent>(_ => btn.style.opacity = 1.0f);
            col.Add(btn);
            return col;
        }

        private VisualElement BuildSigningInState()
        {
            var col = new VisualElement();

            // Waiting pill
            var pill = MakeRoundedBox(SurfaceRaise, 22, 1, new Color(0.212f, 0.749f, 0.694f, 0.22f));
            pill.style.flexDirection  = FlexDirection.Row;
            pill.style.alignItems     = Align.Center;
            pill.style.justifyContent = Justify.Center;
            pill.style.paddingTop     = 18;
            pill.style.paddingBottom  = 18;

            // Pulsing dot
            var dot = new VisualElement();
            dot.style.width  = 6;
            dot.style.height = 6;
            dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius =
                dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 3;
            dot.style.backgroundColor = Teal;
            dot.style.marginRight     = 10;
            dot.schedule.Execute(() => dot.style.opacity = dot.style.opacity.value > 0.6f ? 0.3f : 1.0f).Every(600);
            pill.Add(dot);

            pill.Add(MakeLabel("Waiting for browser\u2026", 12, TextSec));
            col.Add(pill);

            var cancel = MakeGhostButton("Cancel", () => { _isSigningIn = false; RefreshUI(); });
            cancel.style.marginTop = 8;
            cancel.style.alignSelf = Align.Center;
            col.Add(cancel);
            return col;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // STATE 2 — Signed in, no certificate
        // ══════════════════════════════════════════════════════════════════════════

        private VisualElement BuildNoCertCard()
        {
            var card = MakeCard();
            card.Add(BuildAccountBar());
            card.Add(MakeDivider());

            var body = MakePad(24, 28, 24, 24);
            string title = "No certificate yet";
            string subtitle = "Get one to start signing your packages.";
            string detail = null;
            string primaryText = "Get Certificate";
            Action primaryAction = OnRequestCertClicked;
            string secondaryText = null;
            Action secondaryAction = null;

            bool currentDeviceKnown = _accountState?.currentDeviceKnown == true;
            bool deviceCapReached = _accountState?.deviceCapReachedForCurrentMachine == true;
            string billingStatus = _accountState?.billing?.status ?? "";

            if (_isLoadingAccountState && _accountState == null)
            {
                subtitle = "Checking certificate and billing status for this account.";
                primaryText = null;
            }
            else if (_accountState != null)
            {
                detail = _accountState.error;

                switch (billingStatus)
                {
                    case "inactive":
                        title = "Certificate plan required";
                        subtitle = "This account needs an active certificate subscription before this machine can enroll.";
                        primaryText = "Open Certificates & Billing";
                        primaryAction = OpenAccountCertificatesPage;
                        break;
                    case "suspended":
                        title = "Signing access suspended";
                        subtitle = "Billing needs attention before this machine can enroll or restore a signing certificate.";
                        primaryText = "Open Certificates & Billing";
                        primaryAction = OpenAccountCertificatesPage;
                        break;
                    case "grace":
                        if (currentDeviceKnown)
                        {
                            title = "Restore this device";
                            subtitle = "Billing grace is active. This machine can still sign once its existing certificate is restored.";
                            primaryText = "Restore Certificate";
                            primaryAction = OnRequestCertClicked;
                            secondaryText = "Open Billing";
                            secondaryAction = OpenAccountCertificatesPage;
                        }
                        else
                        {
                            title = "Grace period active";
                            subtitle = "Existing enrolled devices can keep signing, but this machine cannot enroll until billing is fixed.";
                            primaryText = "Open Certificates & Billing";
                            primaryAction = OpenAccountCertificatesPage;
                        }
                        break;
                    case "active":
                        if (currentDeviceKnown)
                        {
                            title = "Restore certificate";
                            subtitle = "This machine already has an active signing device on your account.";
                            primaryText = "Restore Certificate";
                            primaryAction = OnRequestCertClicked;
                            secondaryText = "Manage Devices";
                            secondaryAction = OpenAccountCertificatesPage;
                        }
                        else if (deviceCapReached)
                        {
                            title = "Device limit reached";
                            subtitle = "Your current certificate plan has no free device slots for this machine.";
                            primaryText = "Manage Devices";
                            primaryAction = OpenAccountCertificatesPage;
                        }
                        break;
                    case "unmanaged":
                        subtitle = "Get one to start signing your packages on this unmanaged signing server.";
                        break;
                }
            }

            var ring = MakeRoundedBox(Color.clear, 28, 1, new Color(0.212f, 0.749f, 0.694f, 0.25f));
            ring.style.width = 56;
            ring.style.height = 56;
            ring.style.alignItems = Align.Center;
            ring.style.justifyContent = Justify.Center;
            ring.style.alignSelf = Align.Center;
            ring.style.marginBottom = 16;

            var ringInner = new VisualElement();
            ringInner.style.width = 20;
            ringInner.style.height = 20;
            ringInner.style.borderTopLeftRadius = ringInner.style.borderTopRightRadius =
                ringInner.style.borderBottomLeftRadius = ringInner.style.borderBottomRightRadius = 10;
            ringInner.style.borderTopWidth = ringInner.style.borderBottomWidth =
                ringInner.style.borderLeftWidth = ringInner.style.borderRightWidth = 2;
            ringInner.style.borderTopColor = ringInner.style.borderBottomColor =
                ringInner.style.borderLeftColor = ringInner.style.borderRightColor =
                    new Color(0.212f, 0.749f, 0.694f, 0.45f);
            ring.Add(ringInner);
            body.Add(ring);

            body.Add(MakeLabel(title, 14, TextPri, bold: true, align: TextAnchor.MiddleCenter, mb: 6));
            body.Add(MakeLabel(subtitle, 11, TextSec, align: TextAnchor.MiddleCenter, mb: 12, wrap: true));
            if (!string.IsNullOrEmpty(detail))
            {
                body.Add(MakeLabel(detail, 10, TextMute, align: TextAnchor.MiddleCenter, mb: 18, wrap: true));
            }
            else
            {
                var spacer = new VisualElement();
                spacer.style.height = 10;
                body.Add(spacer);
            }

            if (!string.IsNullOrEmpty(primaryText))
            {
                var getBtn = _isRequestingCert
                    ? BuildLoadingButton("Getting certificate...")
                    : BuildPrimaryButton(primaryText, primaryAction);
                getBtn.style.marginBottom = 10;
                body.Add(getBtn);
            }

            if (!string.IsNullOrEmpty(secondaryText))
            {
                var secondaryBtn = MakeGhostButton(secondaryText, secondaryAction);
                secondaryBtn.style.alignSelf = Align.Center;
                secondaryBtn.style.marginBottom = 14;
                body.Add(secondaryBtn);
            }

            var importRow = new VisualElement();
            importRow.style.flexDirection = FlexDirection.Row;
            importRow.style.alignItems = Align.Center;
            importRow.style.justifyContent = Justify.Center;
            var importHint = MakeLabel("Have a .yucp_cert file?", 11, TextMute, mb: 0);
            importHint.style.marginRight = 5;
            importRow.Add(importHint);
            importRow.Add(MakeGhostButton("Import ->", () =>
            {
                string path = EditorUtility.OpenFilePanel("Import Certificate", "", "yucp_cert");
                if (string.IsNullOrEmpty(path)) return;
                var r = CertificateManager.ImportAndVerify(path);
                if (r.valid) { LoadSettings(); RefreshUI(); }
                else EditorUtility.DisplayDialog("Import failed", r.error, "OK");
            }, small: true));
            body.Add(importRow);

            if (_accountState?.billing?.billingEnabled == true)
            {
                body.Add(BuildBillingInsightsSection(showPlanActions: true));
            }

            card.Add(body);
            return card;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // STATE 3 — Signed in, certificate active
        // ══════════════════════════════════════════════════════════════════════════

        private VisualElement BuildActiveCard()
        {
            var card = MakeCard();
            card.Add(BuildAccountBar());
            card.Add(MakeDivider());
            string statusText = "Ready to sign";
            Color statusColor = Teal;
            string accountDetail = _accountState?.error;
            string manageLabel = "Manage Billing & Devices";
            string billingLabel = null;
            string deviceLabel = null;

            if (_accountState?.billing != null)
            {
                switch (_accountState.billing.status ?? "")
                {
                    case "grace":
                        statusText = "Grace period";
                        statusColor = Amber;
                        break;
                    case "inactive":
                        statusText = "Plan required";
                        statusColor = Amber;
                        break;
                    case "suspended":
                        statusText = "Signing blocked";
                        statusColor = Red;
                        break;
                    case "unmanaged":
                        statusText = "Unmanaged server";
                        statusColor = TextSec;
                        manageLabel = "Open Certificates Workspace";
                        break;
                }

                if (_accountState.billing.billingEnabled)
                {
                    string planDisplayName = ResolvePlanDisplayName(_accountState.billing.planKey);
                    billingLabel = string.IsNullOrEmpty(_accountState.billing.planKey)
                        ? $"Billing: {_accountState.billing.status}"
                        : $"Plan: {planDisplayName}";
                }

                deviceLabel = _accountState.billing.deviceCap > 0
                    ? $"Devices: {_accountState.billing.activeDeviceCount}/{_accountState.billing.deviceCap}"
                    : $"Devices: {_accountState.billing.activeDeviceCount}";
            }

            // ── Status row (inline, no filled band) ───────────────────────────────
            var statusRow = new VisualElement();
            statusRow.style.flexDirection  = FlexDirection.Row;
            statusRow.style.alignItems     = Align.Center;
            statusRow.style.paddingLeft    = 18;
            statusRow.style.paddingRight   = 18;
            statusRow.style.paddingTop     = 13;
            statusRow.style.paddingBottom  = 11;

            // Pulsing live dot
            var liveDot = new VisualElement();
            liveDot.style.width  = 6;
            liveDot.style.height = 6;
            liveDot.style.borderTopLeftRadius = liveDot.style.borderTopRightRadius =
                liveDot.style.borderBottomLeftRadius = liveDot.style.borderBottomRightRadius = 3;
            liveDot.style.backgroundColor = statusColor;
            liveDot.style.marginRight     = 8;
            liveDot.style.flexShrink      = 0;
            liveDot.schedule.Execute(() => liveDot.style.opacity = liveDot.style.opacity.value > 0.6f ? 0.3f : 1.0f).Every(900);
            statusRow.Add(liveDot);

            statusRow.Add(MakeLabel(statusText, 12, statusColor, bold: true));
            card.Add(statusRow);

            // ── Certificate health ─────────────────────────────────────────────────
            var certBody = MakePad(16, 18, 16, 16);

            // Publisher row
            string pubName = _settings?.publisherName ?? YucpOAuthService.GetDisplayName() ?? "Creator";
            certBody.Add(MakeLabel(pubName, 13, TextPri, bold: true, mb: 2));

            // Expiry bar
            if (!string.IsNullOrEmpty(_settings?.certificateExpiresAt) &&
                DateTime.TryParse(_settings.certificateExpiresAt, out DateTime exp))
            {
                certBody.Add(BuildExpiryBar(exp));
            }

            if (!string.IsNullOrEmpty(accountDetail))
            {
                certBody.Add(MakeLabel(accountDetail, 11, TextSec, mb: 8, wrap: true));
            }

            if (!string.IsNullOrEmpty(billingLabel))
            {
                certBody.Add(MakeLabel(billingLabel, 11, TextSec, mb: 4, wrap: true));
            }

            if (!string.IsNullOrEmpty(deviceLabel))
            {
                certBody.Add(MakeLabel(deviceLabel, 11, TextSec, mb: 0, wrap: true));
            }

            card.Add(certBody);

            if (_accountState?.billing?.billingEnabled == true)
            {
                card.Add(MakeDivider());
                card.Add(BuildBillingInsightsSection(showPlanActions: true));
            }

            // ── Package info ───────────────────────────────────────────────────────
            if (_profile != null)
            {
                card.Add(MakeDivider());
                card.Add(BuildPackageInfo());
            }

            // ── License Protection ─────────────────────────────────────────────────
            if (_profile != null)
            {
                card.Add(MakeDivider());
                _licenseSectionSlot = new VisualElement();
                _licenseSectionSlot.Add(BuildLicenseProtectionSection());
                card.Add(_licenseSectionSlot);
            }

            // ── Footer actions ─────────────────────────────────────────────────────
            card.Add(MakeDivider());
            var footer = new VisualElement();
            footer.style.flexDirection  = FlexDirection.Row;
            footer.style.justifyContent = Justify.FlexEnd;
            footer.style.paddingLeft    = 14;
            footer.style.paddingRight   = 14;
            footer.style.paddingTop     = 10;
            footer.style.paddingBottom  = 10;
            footer.Add(MakeGhostButton(manageLabel, OpenAccountCertificatesPage));
            var settingsBtn = MakeGhostButton("Editor Settings", () => SigningSettingsWindow.ShowWindow());
            settingsBtn.style.marginLeft = 8;
            footer.Add(settingsBtn);
            card.Add(footer);

            return card;
        }

        private VisualElement BuildExpiryBar(DateTime exp)
        {
            var col = new VisualElement();
            col.style.marginTop    = 10;
            col.style.marginBottom = 4;

            var delta = exp - DateTime.UtcNow;
            int daysLeft = (int)Math.Max(0, Math.Ceiling(delta.TotalDays));
            float pct    = Mathf.Clamp01((float)(delta.TotalDays / 90.0));

            Color barColor = daysLeft < 0 ? Red : daysLeft < 14 ? Amber : Teal;
            string expiryLabel = daysLeft < 0 ? "Expired"
                : daysLeft == 0 ? "Expires today"
                : daysLeft == 1 ? "1 day left"
                : $"{daysLeft} days left";

            // Label row
            var labelRow = new VisualElement();
            labelRow.style.flexDirection  = FlexDirection.Row;
            labelRow.style.justifyContent = Justify.SpaceBetween;
            labelRow.style.marginBottom   = 5;
            labelRow.Add(MakeLabel("Certificate", 10, TextMute));
            var expLbl = MakeLabel(expiryLabel, 10, barColor, bold: true);
            labelRow.Add(expLbl);
            col.Add(labelRow);

            // Track
            var track = new VisualElement();
            track.style.height          = 3;
            track.style.backgroundColor = BorderFaint;
            track.style.borderTopLeftRadius = track.style.borderTopRightRadius =
                track.style.borderBottomLeftRadius = track.style.borderBottomRightRadius = 2;
            track.style.overflow = Overflow.Hidden;

            var fill = new VisualElement();
            fill.style.height          = 3;
            fill.style.width           = Length.Percent(pct * 100f);
            fill.style.backgroundColor = barColor;
            fill.style.borderTopLeftRadius = fill.style.borderTopRightRadius =
                fill.style.borderBottomLeftRadius = fill.style.borderBottomRightRadius = 2;
            track.Add(fill);
            col.Add(track);

            return col;
        }

        private VisualElement BuildBillingInsightsSection(bool showPlanActions)
        {
            var section = MakePad(16, 18, 16, 16);
            section.Add(MakeLabel("Billing Workspace", 10, TextMute, bold: true, mb: 6));

            string summary = _accountState?.billing?.reason;
            if (string.IsNullOrEmpty(summary))
            {
                summary = _accountState?.error;
            }
            if (string.IsNullOrEmpty(summary))
            {
                summary = "Your subscription controls device enrollment, signing availability, and support capacity for this workspace.";
            }

            section.Add(MakeLabel(summary, 11, TextSec, mb: 12, wrap: true));

            var metrics = new VisualElement();
            metrics.style.flexDirection = FlexDirection.Row;
            metrics.style.flexWrap = Wrap.Wrap;
            metrics.style.marginBottom = 12;
            metrics.Add(BuildBillingMetricTile(
                "Current plan",
                !string.IsNullOrEmpty(_accountState?.billing?.planKey) ? _accountState.billing.planKey : "No plan"));
            metrics.Add(BuildBillingMetricTile(
                "Devices",
                _accountState?.billing != null && _accountState.billing.deviceCap > 0
                    ? $"{_accountState.billing.activeDeviceCount}/{_accountState.billing.deviceCap}"
                    : $"{_accountState?.billing?.activeDeviceCount ?? 0}"));
            metrics.Add(BuildBillingMetricTile(
                "Quota",
                FormatQuota(_accountState?.billing?.signQuotaPerPeriod ?? 0)));
            metrics.Add(BuildBillingMetricTile(
                "Support",
                FormatSupportTier(_accountState?.billing?.supportTier)));
            section.Add(metrics);

            if (!string.IsNullOrEmpty(_accountState?.workspaceKey))
            {
                var workspaceRow = new VisualElement();
                workspaceRow.style.flexDirection = FlexDirection.Row;
                workspaceRow.style.alignItems = Align.Center;
                workspaceRow.style.marginBottom = 12;
                workspaceRow.Add(MakeLabel("Workspace", 10, TextMute, bold: true, mb: 0));
                var workspaceChip = MakeRoundedBox(SurfaceRaise, 6, 1, Border);
                workspaceChip.style.marginLeft = 8;
                workspaceChip.style.paddingLeft = 8;
                workspaceChip.style.paddingRight = 8;
                workspaceChip.style.paddingTop = 4;
                workspaceChip.style.paddingBottom = 4;
                workspaceChip.Add(MakeLabel(_accountState.workspaceKey, 10, TextSec, mb: 0));
                workspaceRow.Add(workspaceChip);
                section.Add(workspaceRow);
            }

            if (_accountState?.availablePlans != null && _accountState.availablePlans.Length > 0)
            {
                section.Add(MakeLabel("Plans", 10, TextMute, bold: true, mb: 8));

                foreach (var plan in _accountState.availablePlans.OrderByDescending(p => p.priority))
                {
                    section.Add(BuildPlanCard(plan, showPlanActions));
                }
            }
            else
            {
                section.Add(MakeLabel(
                    "No certificate plans are configured on this signing server yet.",
                    10,
                    TextMute,
                    mb: 0,
                    wrap: true));
            }

            return section;
        }

        private VisualElement BuildBillingMetricTile(string label, string value)
        {
            var tile = MakeRoundedBox(SurfaceRaise, 8, 1, Border);
            tile.style.minWidth = 120;
            tile.style.marginRight = 8;
            tile.style.marginBottom = 8;
            tile.style.paddingLeft = 10;
            tile.style.paddingRight = 10;
            tile.style.paddingTop = 9;
            tile.style.paddingBottom = 9;
            tile.Add(MakeLabel(label, 9, TextMute, bold: true, mb: 4));
            tile.Add(MakeLabel(value, 12, TextPri, bold: true, mb: 0, wrap: true));
            return tile;
        }

        private VisualElement BuildPlanCard(PackageSigningService.CertificatePlanInfo plan, bool showPlanActions)
        {
            bool isCurrent = string.Equals(
                _accountState?.billing?.planKey,
                plan?.planKey,
                StringComparison.OrdinalIgnoreCase);

            var card = MakeRoundedBox(
                isCurrent ? new Color(0.212f, 0.749f, 0.694f, 0.08f) : SurfaceRaise,
                8,
                1,
                isCurrent ? new Color(0.212f, 0.749f, 0.694f, 0.32f) : Border);
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.paddingTop = 12;
            card.style.paddingBottom = 12;
            card.style.marginBottom = 8;

            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.justifyContent = Justify.SpaceBetween;
            topRow.style.alignItems = Align.FlexStart;

            var titleCol = new VisualElement();
            string planName = !string.IsNullOrEmpty(plan.displayName) ? plan.displayName : plan.planKey;
            titleCol.Add(MakeLabel(planName, 13, TextPri, bold: true, mb: 3));
            titleCol.Add(MakeLabel(
                $"{FormatSupportTier(plan.supportTier)} support",
                10,
                TextSec,
                mb: 0));
            topRow.Add(titleCol);

            if (isCurrent)
            {
                topRow.Add(BuildStatusPill("active"));
            }

            card.Add(topRow);

            var features = new VisualElement();
            features.style.marginTop = 10;
            if (!string.IsNullOrEmpty(plan.description))
            {
                features.Add(MakeLabel(plan.description, 10, TextSec, mb: 6, wrap: true));
            }

            string[] highlights = plan.highlights != null && plan.highlights.Length > 0
                ? plan.highlights
                : new[]
                {
                    $"{plan.deviceCap} active device slots",
                    $"{FormatQuota(plan.signQuotaPerPeriod)} signing events per period",
                    $"{FormatRetention(plan.auditRetentionDays)} of audit retention",
                    $"{plan.billingGraceDays} billing grace days",
                };

            for (int index = 0; index < highlights.Length; index++)
            {
                string highlight = highlights[index];
                if (string.IsNullOrEmpty(highlight))
                    continue;

                features.Add(MakeLabel(highlight, 10, TextSec, mb: index == highlights.Length - 1 ? 0 : 4, wrap: true));
            }
            card.Add(features);

            if (showPlanActions)
            {
                var actions = new VisualElement();
                actions.style.flexDirection = FlexDirection.Row;
                actions.style.justifyContent = Justify.FlexEnd;
                actions.style.marginTop = 12;

                if (isCurrent && _accountState?.billing?.status == "active")
                {
                    actions.Add(MakeGhostButton("Manage plan", OpenBillingPortalPage, small: true));
                }
                else
                {
                    actions.Add(MakeGhostButton("Choose plan", () => OpenCheckoutForPlan(plan.planKey), small: true));
                }

                card.Add(actions);
            }

            return card;
        }

        private static string FormatQuota(int value)
        {
            return value > 0 ? value.ToString("N0") : "Unlimited";
        }

        private static string FormatRetention(int days)
        {
            return days > 0 ? $"{days} days" : "Retention varies";
        }

        private static string FormatSupportTier(string supportTier)
        {
            if (string.IsNullOrEmpty(supportTier))
                return "Standard";

            if (supportTier.Length == 1)
                return supportTier.ToUpperInvariant();

            return char.ToUpperInvariant(supportTier[0]) + supportTier.Substring(1);
        }

        private string ResolvePlanDisplayName(string planKey)
        {
            if (string.IsNullOrEmpty(planKey) || _accountState?.availablePlans == null)
                return planKey;

            foreach (var plan in _accountState.availablePlans)
            {
                if (plan == null || !string.Equals(plan.planKey, planKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                return !string.IsNullOrEmpty(plan.displayName) ? plan.displayName : plan.planKey;
            }

            return planKey;
        }

        private VisualElement BuildPackageInfo()
        {
            var body = MakePad(16, 18, 16, 16);

            // Package name + version row
            var pkgRow = new VisualElement();
            pkgRow.style.flexDirection  = FlexDirection.Row;
            pkgRow.style.alignItems     = Align.Center;
            pkgRow.style.justifyContent = Justify.SpaceBetween;
            pkgRow.style.marginBottom   = 14;

            var nameCol = new VisualElement();
            if (!string.IsNullOrEmpty(_profile.packageName))
                nameCol.Add(MakeLabel(_profile.packageName, 13, TextPri, bold: true, mb: 0));
            if (!string.IsNullOrEmpty(_profile.version))
                nameCol.Add(MakeLabel("v" + _profile.version, 10, TextMute, mb: 0));
            pkgRow.Add(nameCol);

            var badgeSlot = new VisualElement();
            badgeSlot.style.alignSelf = Align.Center;
            if (!string.IsNullOrEmpty(_profile.packageId))
                LoadPackageStatusBadge(badgeSlot);
            pkgRow.Add(badgeSlot);

            body.Add(pkgRow);

            // Canonical product picker
            _productPickerSlot = new VisualElement();
            body.Add(_productPickerSlot);
            RebuildProductPicker();

            if (_canonicalProducts == null && !_productsLoading)
                LoadCreatorProducts();

            // ── Certificate Provider override ──────────────────────────────────────
            body.Add(BuildCertificateProviderRow());

            return body;
        }

        private VisualElement BuildCertificateProviderRow()
        {
            var container = new VisualElement();
            container.style.marginTop = 14;

            container.Add(MakeLabel("Certificate Provider", 10, TextMute, bold: true, mb: 6));

            string effectiveUrl = GetServerUrl();
            bool hasOverride = !string.IsNullOrEmpty(_profile?.signingServerUrl);

            var urlRow = new VisualElement();
            urlRow.style.flexDirection = FlexDirection.Row;
            urlRow.style.alignItems    = Align.Center;
            urlRow.style.marginBottom  = 4;

            var urlField = new TextField();
            urlField.style.flexGrow   = 1;
            urlField.style.marginRight = 6;
            urlField.AddToClassList("yucp-input");
            urlField.value      = hasOverride ? _profile.signingServerUrl : effectiveUrl;
            urlField.isReadOnly = !hasOverride;
            urlField.tooltip    = hasOverride
                ? "Custom certificate provider URL for this profile"
                : "Using default from Signing Settings. Enable override to customize.";
            if (!hasOverride)
                urlField.style.opacity = 0.5f;
            urlRow.Add(urlField);

            var overrideToggle = new Toggle("Override");
            overrideToggle.value = hasOverride;
            overrideToggle.style.flexShrink = 0;
            overrideToggle.RegisterValueChangedCallback(evt =>
            {
                if (_profile == null) return;
                if (evt.newValue)
                {
                    _profile.signingServerUrl = urlField.value != effectiveUrl ? urlField.value : effectiveUrl;
                    urlField.SetValueWithoutNotify(_profile.signingServerUrl);
                    urlField.isReadOnly = false;
                    urlField.style.opacity = 1f;
                }
                else
                {
                    _profile.signingServerUrl = "";
                    urlField.SetValueWithoutNotify(GetServerUrl());
                    urlField.isReadOnly = true;
                    urlField.style.opacity = 0.5f;
                }
                EditorUtility.SetDirty(_profile);
            });
            urlRow.Add(overrideToggle);

            container.Add(urlRow);

            urlField.RegisterValueChangedCallback(evt =>
            {
                if (_profile == null || !overrideToggle.value) return;
                _profile.signingServerUrl = evt.newValue;
                EditorUtility.SetDirty(_profile);
            });

            if (!hasOverride)
            {
                var hint = MakeLabel($"Default: {effectiveUrl}", 9, TextMute, mb: 0);
                hint.style.whiteSpace = WhiteSpace.Normal;
                container.Add(hint);
            }

            return container;
        }


        private void LoadPackageStatusBadge(VisualElement slot)
        {
            PackageInfoService.GetPublisherPackages(
                packages => EditorApplication.delayCall += () =>
                {
                    if (_root == null) return;
                    var pkg = packages?.FirstOrDefault(p => p.packageId == _profile?.packageId);
                    if (pkg == null) return;
                    slot.Add(BuildStatusPill(pkg.status));

                    if (pkg.status == "active") return;
                    if (pkg.status == "revoked" && !string.IsNullOrEmpty(pkg.reason))
                    {
                        var note = MakeLabel(pkg.reason, 10, new Color(0.89f, 0.50f, 0.29f), mb: 0, wrap: true);
                        note.style.marginTop = 6;
                        slot.parent?.Add(note);
                    }
                },
                _ => { });
        }

        // ── Custom product picker ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the active set of selected product IDs, migrating from the legacy
        /// single-product field if <c>licenseProductIds</c> is empty.
        /// </summary>
        private List<string> GetSelectedProductIds()
        {
            if (_profile == null) return new List<string>();
            // Lazy migration: if the new list is empty but the old single-ID field is set, import it
            if ((_profile.licenseProductIds == null || _profile.licenseProductIds.Count == 0)
                && !string.IsNullOrEmpty(_profile.licenseProductId))
            {
                if (_profile.licenseProductIds == null) _profile.licenseProductIds = new List<string>();
                _profile.licenseProductIds.Add(_profile.licenseProductId);
                EditorUtility.SetDirty(_profile);
            }
            return _profile.licenseProductIds ?? new List<string>();
        }

        private void RebuildProductPicker()
        {
            if (_productPickerSlot == null) return;
            _productPickerSlot.Clear();
            _productListPanel = null;

            // Section header: "Linked Product" + refresh button
            var header = new VisualElement();
            header.style.flexDirection  = FlexDirection.Row;
            header.style.alignItems     = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom   = 8;
            header.Add(MakeLabel("Linked Product", 10, TextMute, bold: true, mb: 0));

            var refreshLbl = new Label("↻");
            refreshLbl.style.fontSize       = 12;
            refreshLbl.style.color          = new StyleColor(TextMute);
            refreshLbl.style.paddingLeft    = refreshLbl.style.paddingRight = 4;
            refreshLbl.style.unityTextAlign = TextAnchor.MiddleCenter;
            refreshLbl.RegisterCallback<MouseEnterEvent>(_ => refreshLbl.style.color = new StyleColor(TextSec));
            refreshLbl.RegisterCallback<MouseLeaveEvent>(_ => refreshLbl.style.color = new StyleColor(TextMute));
            refreshLbl.RegisterCallback<ClickEvent>(_ =>
            {
                _canonicalProducts = null; _productsLoading = false; _productPickerExpanded = false;
                _productLoadErrorTitle = null; _productLoadErrorMessage = null;
                LoadCreatorProducts();
            });
            header.Add(refreshLbl);
            _productPickerSlot.Add(header);

            // Loading state
            if (_productsLoading)
            {
                var loadCard = MakePickerShell();
                loadCard.Add(MakeLabel("Loading products…", 11, TextMute, mb: 0));
                _productPickerSlot.Add(loadCard);
                return;
            }

            if (!string.IsNullOrEmpty(_productLoadErrorTitle) || !string.IsNullOrEmpty(_productLoadErrorMessage))
            {
                var errorCard = MakePickerShell();
                errorCard.style.marginBottom = 8;
                errorCard.Add(MakeLabel(_productLoadErrorTitle ?? "Could not load products", 11, Amber, bold: true, mb: 3));
                errorCard.Add(MakeLabel(_productLoadErrorMessage ?? "Refresh to try again.", 10, TextMute, wrap: true));
                _productPickerSlot.Add(errorCard);
            }

            // Empty state
            if (_canonicalProducts == null || _canonicalProducts.Count == 0)
            {
                var emptyCard = MakePickerShell();
                emptyCard.style.paddingTop = emptyCard.style.paddingBottom = 10;
                emptyCard.Add(MakeLabel("No products found", 11, TextSec, bold: true, mb: 3));
                emptyCard.Add(MakeLabel(
                    string.IsNullOrEmpty(_productLoadErrorMessage)
                        ? "Connect a store or configure a product, then refresh."
                        : "The server catalog is unavailable. Fix the error above, then refresh.",
                    10,
                    TextMute,
                    wrap: true));
                _productPickerSlot.Add(emptyCard);
                return;
            }

            // Custom picker: trigger + collapsible list
            _productPickerSlot.Add(BuildPickerTrigger());
            _productListPanel = BuildPickerList();
            _productListPanel.style.display = _productPickerExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            _productPickerSlot.Add(_productListPanel);
        }

        // Shared shell for loading/empty cards
        private VisualElement MakePickerShell()
        {
            var c = new VisualElement();
            c.style.paddingLeft = c.style.paddingRight = 12;
            c.style.paddingTop  = c.style.paddingBottom = 9;
            c.style.backgroundColor = new Color(0.125f, 0.125f, 0.125f);
            c.style.borderTopLeftRadius = c.style.borderTopRightRadius =
                c.style.borderBottomLeftRadius = c.style.borderBottomRightRadius = 5;
            c.style.borderLeftWidth = c.style.borderRightWidth =
                c.style.borderTopWidth = c.style.borderBottomWidth = 1;
            c.style.borderLeftColor = c.style.borderRightColor =
                c.style.borderTopColor = c.style.borderBottomColor = Border;
            return c;
        }

        // The always-visible trigger button showing the current selection
        private VisualElement BuildPickerTrigger()
        {
            var selectedIds = GetSelectedProductIds();
            var selectedProducts = _canonicalProducts?
                .Where(p => ProductIsSelected(p, selectedIds))
                .ToList() ?? new List<CanonicalProduct>();

            bool open         = _productPickerExpanded;
            var  accentBorder = open ? Teal : Border;
            var  normalBg     = new Color(0.125f, 0.125f, 0.125f);
            var  hoverBg      = new Color(0.155f, 0.155f, 0.155f);

            var trigger = new VisualElement();
            trigger.style.flexDirection  = FlexDirection.Row;
            trigger.style.alignItems     = Align.Center;
            trigger.style.paddingLeft    = 12;
            trigger.style.paddingRight   = 10;
            trigger.style.paddingTop     = 9;
            trigger.style.paddingBottom  = 9;
            trigger.style.backgroundColor = normalBg;
            trigger.style.borderTopLeftRadius    = 5;
            trigger.style.borderTopRightRadius   = 5;
            trigger.style.borderBottomLeftRadius  = open ? 0 : 5;
            trigger.style.borderBottomRightRadius = open ? 0 : 5;
            trigger.style.borderLeftWidth  = trigger.style.borderRightWidth  =
                trigger.style.borderTopWidth = 1;
            trigger.style.borderBottomWidth = 1;
            trigger.style.borderLeftColor  = trigger.style.borderRightColor  =
                trigger.style.borderTopColor = accentBorder;
            trigger.style.borderBottomColor = open ? normalBg : accentBorder; // seam-less when open

            // Left column: product name(s) + provider badges
            var leftCol = new VisualElement();
            leftCol.style.flexGrow = 1;
            if (selectedProducts.Count == 0)
            {
                leftCol.Add(MakeLabel("Select products…", 12, TextMute, mb: 0));
            }
            else if (selectedProducts.Count == 1)
            {
                var sp = selectedProducts[0];
                leftCol.Add(MakeLabel(sp.displayName, 12, TextPri, bold: true, mb: 0));
                if (!string.IsNullOrEmpty(sp.owner))
                    leftCol.Add(MakeLabel("via " + sp.owner, 9, new Color(0.68f, 0.55f, 0.27f), mb: 0));
                if (sp.providers?.Count > 0)
                {
                    var badgeRow = BuildProviderBadgeRow(sp.providers);
                    badgeRow.style.marginTop = 4;
                    leftCol.Add(badgeRow);
                }
            }
            else
            {
                leftCol.Add(MakeLabel($"{selectedProducts.Count} products selected", 12, TextPri, bold: true, mb: 3));
                foreach (var sp in selectedProducts)
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems    = Align.Center;
                    row.style.marginBottom  = 2;
                    row.Add(MakeLabel("· " + sp.displayName, 10, TextSec, mb: 0));
                    if (sp.providers?.Count > 0)
                    {
                        var badgeRow = BuildProviderBadgeRow(sp.providers);
                        badgeRow.style.marginLeft = 6;
                        row.Add(badgeRow);
                    }
                    leftCol.Add(row);
                }
            }
            trigger.Add(leftCol);

            // Chevron
            var chevron = new Label(open ? "▴" : "▾");
            chevron.style.color          = new StyleColor(TextMute);
            chevron.style.fontSize       = 10;
            chevron.style.marginLeft     = 6;
            chevron.style.unityTextAlign = TextAnchor.MiddleCenter;
            trigger.Add(chevron);

            trigger.RegisterCallback<MouseEnterEvent>(_ => { if (!_productPickerExpanded) trigger.style.backgroundColor = hoverBg; });
            trigger.RegisterCallback<MouseLeaveEvent>(_ => { if (!_productPickerExpanded) trigger.style.backgroundColor = normalBg; });
            trigger.RegisterCallback<ClickEvent>(_ =>
            {
                _productPickerExpanded = !_productPickerExpanded;
                if (_productListPanel != null)
                    _productListPanel.style.display = _productPickerExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                EditorApplication.delayCall += () => RebuildProductPicker();
            });

            return trigger;
        }

        // Collapsible list of all products
        private VisualElement BuildPickerList()
        {
            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(0.105f, 0.105f, 0.105f);
            panel.style.borderLeftWidth  = panel.style.borderRightWidth  = 1;
            panel.style.borderBottomWidth = 1;
            panel.style.borderTopWidth    = 0;
            panel.style.borderLeftColor   = panel.style.borderRightColor  =
                panel.style.borderBottomColor = Teal;
            panel.style.borderBottomLeftRadius = panel.style.borderBottomRightRadius = 5;
            panel.style.overflow = Overflow.Hidden;

            var filteredProducts = GetFilteredProducts();
            panel.Add(BuildPickerControls(filteredProducts.Count));
            panel.Add(MakeListDivider());

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 320;
            scroll.style.flexGrow = 1;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.Add(BuildPickerItem(null)); // "No product" entry

            if (filteredProducts.Count == 0)
            {
                scroll.Add(MakeListDivider());
                var emptyState = new VisualElement();
                emptyState.style.paddingLeft = 12;
                emptyState.style.paddingRight = 12;
                emptyState.style.paddingTop = 12;
                emptyState.style.paddingBottom = 12;
                emptyState.Add(MakeLabel("No products match the current filters.", 10, TextMute, mb: 0));
                scroll.Add(emptyState);
            }
            else
            {
                foreach (var product in filteredProducts)
                {
                    scroll.Add(MakeListDivider());
                    scroll.Add(BuildPickerItem(product));
                }
            }

            panel.Add(scroll);
            return panel;
        }

        private VisualElement BuildPickerControls(int filteredCount)
        {
            var controls = new VisualElement();
            controls.style.paddingLeft = 12;
            controls.style.paddingRight = 12;
            controls.style.paddingTop = 10;
            controls.style.paddingBottom = 10;

            var searchField = new TextField();
            searchField.value = _productSearchQuery ?? string.Empty;
            searchField.style.marginBottom = 8;
            searchField.style.height = 24;
            searchField.style.backgroundColor = new Color(0.145f, 0.145f, 0.145f);
            searchField.style.borderBottomColor = Border;
            searchField.style.borderLeftColor = Border;
            searchField.style.borderRightColor = Border;
            searchField.style.borderTopColor = Border;
            searchField.style.borderBottomWidth = 1;
            searchField.style.borderLeftWidth = 1;
            searchField.style.borderRightWidth = 1;
            searchField.style.borderTopWidth = 1;
            searchField.style.borderBottomLeftRadius = 4;
            searchField.style.borderBottomRightRadius = 4;
            searchField.style.borderTopLeftRadius = 4;
            searchField.style.borderTopRightRadius = 4;
            searchField.RegisterValueChangedCallback(evt =>
            {
                _productSearchQuery = evt.newValue ?? string.Empty;
                EditorApplication.delayCall += RebuildProductPicker;
            });
            controls.Add(searchField);

            var filterRow = new VisualElement();
            filterRow.style.flexDirection = FlexDirection.Row;
            filterRow.style.alignItems = Align.Center;
            filterRow.style.marginBottom = 6;

            var sourceChoices = new List<string>
            {
                AllSourcesFilter,
                ConfiguredSourcesFilter,
                StoreSourcesFilter,
                SelectedSourcesFilter,
            };
            var sourceField = new PopupField<string>(sourceChoices, Mathf.Max(0, sourceChoices.IndexOf(_productSourceFilter)));
            sourceField.style.minWidth = 120;
            sourceField.RegisterValueChangedCallback(evt =>
            {
                _productSourceFilter = evt.newValue ?? AllSourcesFilter;
                EditorApplication.delayCall += RebuildProductPicker;
            });
            filterRow.Add(sourceField);

            var providerChoices = new List<string> { AllProvidersFilter };
            providerChoices.AddRange(
                _canonicalProducts
                    .SelectMany(product => product.providers ?? new List<ProviderRef>())
                    .Select(provider => GetProviderStyle(provider.provider).label)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(label => label, StringComparer.OrdinalIgnoreCase));
            if (!providerChoices.Contains(_productProviderFilter))
            {
                _productProviderFilter = AllProvidersFilter;
            }

            var providerField = new PopupField<string>(providerChoices, Mathf.Max(0, providerChoices.IndexOf(_productProviderFilter)));
            providerField.style.marginLeft = 8;
            providerField.style.minWidth = 140;
            providerField.RegisterValueChangedCallback(evt =>
            {
                _productProviderFilter = evt.newValue ?? AllProvidersFilter;
                EditorApplication.delayCall += RebuildProductPicker;
            });
            filterRow.Add(providerField);
            controls.Add(filterRow);

            controls.Add(MakeLabel($"{filteredCount} of {_canonicalProducts?.Count ?? 0} products", 9, TextMute, mb: 0));
            return controls;
        }

        private List<CanonicalProduct> GetFilteredProducts()
        {
            var selectedIds = GetSelectedProductIds();
            return (_canonicalProducts ?? new List<CanonicalProduct>())
                .Where(product => MatchesSourceFilter(product, selectedIds))
                .Where(product => MatchesProviderFilter(product))
                .Where(product => MatchesSearch(product))
                .OrderByDescending(product => ProductIsSelected(product, selectedIds))
                .ThenByDescending(product => product.configured || product.localConfigured)
                .ThenBy(product => product.displayName ?? product.productId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private bool MatchesSourceFilter(CanonicalProduct product, List<string> selectedIds)
        {
            switch (_productSourceFilter)
            {
                case ConfiguredSourcesFilter:
                    return product != null && (product.configured || product.localConfigured);
                case StoreSourcesFilter:
                    return product != null && IsStoreProduct(product);
                case SelectedSourcesFilter:
                    return ProductIsSelected(product, selectedIds);
                default:
                    return true;
            }
        }

        private bool MatchesProviderFilter(CanonicalProduct product)
        {
            if (product == null || string.Equals(_productProviderFilter, AllProvidersFilter, StringComparison.OrdinalIgnoreCase))
                return true;

            return (product.providers ?? new List<ProviderRef>())
                .Select(provider => GetProviderStyle(provider.provider).label)
                .Any(label => string.Equals(label, _productProviderFilter, StringComparison.OrdinalIgnoreCase));
        }

        private bool MatchesSearch(CanonicalProduct product)
        {
            if (product == null || string.IsNullOrWhiteSpace(_productSearchQuery))
                return true;

            string query = _productSearchQuery.Trim();
            if (!string.IsNullOrEmpty(product.displayName) &&
                product.displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (!string.IsNullOrEmpty(product.owner) &&
                product.owner.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            foreach (var provider in product.providers ?? new List<ProviderRef>())
            {
                if ((!string.IsNullOrEmpty(provider.provider) &&
                     provider.provider.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(provider.providerRef) &&
                     provider.providerRef.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;
            }

            return !string.IsNullOrEmpty(product.productId) &&
                   product.productId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsStoreProduct(CanonicalProduct product)
        {
            if (product == null)
                return false;

            if (product.live)
                return true;

            return (product.providers ?? new List<ProviderRef>())
                .Any(provider =>
                    !string.IsNullOrEmpty(provider.provider) &&
                    !string.Equals(provider.provider, "discord", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(provider.provider, "manual", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(provider.provider, "vrchat", StringComparison.OrdinalIgnoreCase));
        }

        private VisualElement MakeListDivider()
        {
            var d = new VisualElement();
            d.style.height          = 1;
            d.style.backgroundColor = Border;
            return d;
        }

        private VisualElement BuildPickerItem(CanonicalProduct product)
        {
            bool isNone     = product == null;
            var  selectedIds = GetSelectedProductIds();
            bool isSelected = !isNone && ProductIsSelected(product, selectedIds);

            var normalBg = isSelected ? new Color(0.165f, 0.165f, 0.165f) : new Color(0f, 0f, 0f, 0f);
            var hoverBg  = new Color(0.185f, 0.185f, 0.185f);

            var item = new VisualElement();
            item.style.paddingLeft   = 12;
            item.style.paddingRight  = 12;
            item.style.paddingTop    = 8;
            item.style.paddingBottom = 8;
            item.style.backgroundColor = normalBg;
            item.style.flexDirection = FlexDirection.Row;
            item.style.alignItems    = Align.FlexStart;

            if (isNone)
            {
                var noLbl = MakeLabel("— Clear selection —", 11, TextMute, mb: 0);
                noLbl.style.unityFontStyleAndWeight = FontStyle.Italic;
                item.Add(noLbl);
            }
            else
            {
                // Checkbox indicator
                var checkBox = new VisualElement();
                checkBox.style.width  = 14; checkBox.style.height = 14;
                checkBox.style.flexShrink = 0;
                checkBox.style.marginRight = 8; checkBox.style.marginTop = 2;
                checkBox.style.borderTopLeftRadius = checkBox.style.borderTopRightRadius =
                    checkBox.style.borderBottomLeftRadius = checkBox.style.borderBottomRightRadius = 3;
                checkBox.style.borderLeftWidth = checkBox.style.borderRightWidth =
                    checkBox.style.borderTopWidth = checkBox.style.borderBottomWidth = 1;
                checkBox.style.backgroundColor = isSelected ? Teal : new Color(0f, 0f, 0f, 0f);
                checkBox.style.borderLeftColor = checkBox.style.borderRightColor =
                    checkBox.style.borderTopColor = checkBox.style.borderBottomColor = isSelected ? Teal : Border;
                if (isSelected)
                {
                    var checkMark = MakeLabel("✓", 9, new Color(0.05f, 0.05f, 0.05f), bold: true, mb: 0);
                    checkMark.style.unityTextAlign = TextAnchor.MiddleCenter;
                    checkBox.Add(checkMark);
                }
                item.Add(checkBox);

                var content = new VisualElement();
                content.style.flexGrow = 1;

                var nameRow = new VisualElement();
                nameRow.style.flexDirection  = FlexDirection.Row;
                nameRow.style.alignItems     = Align.Center;
                nameRow.style.justifyContent = Justify.SpaceBetween;
                nameRow.style.marginBottom   = (product.providers?.Count > 0) ? 4 : 0;
                nameRow.Add(MakeLabel(product.displayName, 11, isSelected ? TextPri : TextSec, bold: isSelected, mb: 0));
                content.Add(nameRow);

                if (!string.IsNullOrEmpty(product.owner))
                {
                    var collabRow = new VisualElement();
                    collabRow.style.flexDirection = FlexDirection.Row;
                    collabRow.style.alignItems    = Align.Center;
                    collabRow.style.marginBottom  = 3;
                    var collabDot = new VisualElement();
                    collabDot.style.width  = 4; collabDot.style.height = 4;
                    collabDot.style.borderTopLeftRadius = collabDot.style.borderTopRightRadius =
                        collabDot.style.borderBottomLeftRadius = collabDot.style.borderBottomRightRadius = 2;
                    collabDot.style.backgroundColor = Amber;
                    collabDot.style.marginRight = 4; collabDot.style.flexShrink = 0;
                    collabRow.Add(collabDot);
                    collabRow.Add(MakeLabel("via " + product.owner, 9, new Color(0.68f, 0.55f, 0.27f), mb: 0));
                    content.Add(collabRow);
                }
                content.Add(BuildProductSourceBadgeRow(product));
                if (product.providers?.Count > 0)
                    content.Add(BuildProviderBadgeRow(product.providers));

                item.Add(content);
            }

            item.RegisterCallback<MouseEnterEvent>(_ => item.style.backgroundColor = hoverBg);
            item.RegisterCallback<MouseLeaveEvent>(_ => item.style.backgroundColor = isSelected ? new Color(0.165f, 0.165f, 0.165f) : new Color(0f, 0f, 0f, 0f));
            item.RegisterCallback<ClickEvent>(_ =>
            {
                if (_profile == null) return;
                Undo.RecordObject(_profile, isNone ? "Clear License Products" : "Toggle License Product");

                if (isNone)
                {
                    _profile.licenseProductIds.Clear();
                    _profile.licenseProductId = "";
                    _profile.gumroadProductId = "";
                    _profile.jinxxyProductId  = "";
                }
                else
                {
                    var ids = GetSelectedProductIds();
                    bool hasCanonicalIds = GetProductIds(product).Any();
                    if (ProductIsSelected(product, ids))
                    {
                        foreach (var productId in GetProductIds(product))
                            ids.Remove(productId);
                        ApplySelectedProductMetadata(null, ids);
                    }
                    else
                    {
                        if (hasCanonicalIds)
                        {
                            foreach (var productId in GetProductIds(product))
                            {
                                if (!ids.Contains(productId))
                                    ids.Add(productId);
                            }
                            ApplySelectedProductMetadata(null, ids);
                        }
                        else
                        {
                            ids.Clear();
                            ApplySelectedProductMetadata(product, ids);
                        }
                    }
                }

                EditorUtility.SetDirty(_profile);
                EditorApplication.delayCall += () => RebuildProductPicker();
            });
            return item;
        }

        // Row of provider pill badges for a product
        private VisualElement BuildProviderBadgeRow(List<ProviderRef> providers)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap      = Wrap.Wrap;
            foreach (var p in providers) row.Add(MakeProviderBadge(p.provider));
            return row;
        }

        private VisualElement BuildProductSourceBadgeRow(CanonicalProduct product)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginBottom = 4;

            if (product.configured || product.localConfigured)
                row.Add(MakeStatusBadge("Configured", new Color(0.26f, 0.56f, 0.44f, 0.22f), new Color(0.62f, 0.82f, 0.72f)));
            if (IsStoreProduct(product))
                row.Add(MakeStatusBadge("Store", new Color(0.34f, 0.34f, 0.34f, 0.28f), new Color(0.82f, 0.82f, 0.82f)));

            return row.childCount > 0 ? row : new VisualElement();
        }

        private VisualElement MakeStatusBadge(string text, Color background, Color foreground)
        {
            var badge = new VisualElement();
            badge.style.paddingLeft = 6;
            badge.style.paddingRight = 6;
            badge.style.paddingTop = 2;
            badge.style.paddingBottom = 2;
            badge.style.marginRight = 4;
            badge.style.backgroundColor = background;
            badge.style.borderBottomLeftRadius = 10;
            badge.style.borderBottomRightRadius = 10;
            badge.style.borderTopLeftRadius = 10;
            badge.style.borderTopRightRadius = 10;
            badge.Add(MakeLabel(text, 8, foreground, bold: true, mb: 0));
            return badge;
        }

        // Provider pill: full name with branded color. New providers get a graceful default style.
        private VisualElement MakeProviderBadge(string provider)
        {
            var (label, textColor, bgColor) = GetProviderStyle(provider);
            var badge = new VisualElement();
            badge.style.paddingLeft   = 6; badge.style.paddingRight = 6;
            badge.style.paddingTop    = 2; badge.style.paddingBottom = 2;
            badge.style.backgroundColor = bgColor;
            badge.style.borderTopLeftRadius = badge.style.borderTopRightRadius =
                badge.style.borderBottomLeftRadius = badge.style.borderBottomRightRadius = 3;
            badge.style.marginRight = 4; badge.style.marginTop = 2;
            badge.Add(MakeLabel(label, 9, textColor, bold: true, mb: 0));
            return badge;
        }

        private static (string label, Color text, Color bg) GetProviderStyle(string provider)
        {
            switch (provider?.ToLowerInvariant())
            {
                case "gumroad": return ("Gumroad", new Color(0.95f, 0.65f, 0.82f), new Color(0.45f, 0.10f, 0.25f, 0.55f));
                case "jinxxy":  return ("Jinxxy",  new Color(0.60f, 0.70f, 0.98f), new Color(0.18f, 0.23f, 0.60f, 0.55f));
                case "discord": return ("Discord", new Color(0.60f, 0.65f, 0.98f), new Color(0.22f, 0.25f, 0.60f, 0.55f));
                case "vrchat":  return ("VRChat",  new Color(0.45f, 0.85f, 0.80f), new Color(0.08f, 0.32f, 0.32f, 0.60f));
                case "manual":  return ("Manual",  new Color(0.75f, 0.75f, 0.75f), new Color(0.22f, 0.22f, 0.22f, 0.55f));
                default:
                    string name = !string.IsNullOrEmpty(provider)
                        ? char.ToUpperInvariant(provider[0]) + provider.Substring(1) : "Unknown";
                    return (name, new Color(0.70f, 0.70f, 0.70f), new Color(0.20f, 0.20f, 0.20f, 0.55f));
            }
        }

        // ── Product load helpers ───────────────────────────────────────────────────

        [Serializable]
        private class ProductProviderJson
        {
            public string provider;
            public string providerProductRef;
        }

        [Serializable]
        private class CanonicalProductJson
        {
            public string productId;
            public string displayName;
            public string owner;   // null = own; set = collaborator's product
            public bool configured;
            public bool live;
            public ProductProviderJson[] providers;
        }

        [Serializable]
        private class ProductsResponse { public CanonicalProductJson[] products; }

        private async void LoadCreatorProducts()
        {
            string serverUrl = GetServerUrl();
            string accessToken = await YucpOAuthService.GetValidAccessTokenAsync(serverUrl);
            if (string.IsNullOrEmpty(accessToken)) return;

            _productsLoading = true;
            _productLoadErrorTitle = null;
            _productLoadErrorMessage = null;
            EditorApplication.delayCall += () => RebuildProductPicker();

            try
            {
                using var request = UnityEngine.Networking.UnityWebRequest.Get(serverUrl.TrimEnd('/') + "/v1/products");
                request.SetRequestHeader("Authorization", "Bearer " + accessToken);
                request.SetRequestHeader("Accept", "application/json");
                request.SetRequestHeader("Accept-Encoding", "identity");

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                    await System.Threading.Tasks.Task.Delay(50);

                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    var parsed = JsonUtility.FromJson<ProductsResponse>(request.downloadHandler.text);
                    var mergedProducts = new List<CanonicalProduct>();
                    if (parsed?.products != null)
                    {
                        foreach (var p in parsed.products)
                        {
                            MergeCanonicalProduct(mergedProducts, new CanonicalProduct
                            {
                                productId = p.productId ?? string.Empty,
                                productIds = string.IsNullOrEmpty(p.productId) ? new List<string>() : new List<string> { p.productId },
                                displayName = !string.IsNullOrEmpty(p.displayName) ? p.displayName : p.productId,
                                owner = string.IsNullOrEmpty(p.owner) ? null : p.owner,
                                providers = (p.providers ?? Array.Empty<ProductProviderJson>())
                                    .Where(prov => !string.IsNullOrEmpty(prov.provider))
                                    .Select(prov => new ProviderRef
                                    {
                                        provider = prov.provider,
                                        providerRef = prov.providerProductRef ?? string.Empty,
                                    })
                                    .ToList(),
                                configured = p.configured,
                                live = p.live,
                            });
                        }
                    }

                    foreach (var localProduct in GetLocallyConfiguredProducts())
                    {
                        MergeCanonicalProduct(mergedProducts, localProduct);
                    }

                    _canonicalProducts = mergedProducts
                        .OrderBy(product => product.displayName, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    _productLoadErrorTitle = null;
                    _productLoadErrorMessage = null;
                }
                else
                {
                    _canonicalProducts = GetLocallyConfiguredProducts();
                    _productLoadErrorTitle = "Could not load products";
                    _productLoadErrorMessage = BuildProductLoadErrorMessage(request.responseCode, request.downloadHandler != null ? request.downloadHandler.text : null);
                }
            }
            catch (Exception ex)
            {
                _canonicalProducts = GetLocallyConfiguredProducts();
                _productLoadErrorTitle = "Could not load products";
                _productLoadErrorMessage = string.IsNullOrEmpty(ex.Message)
                    ? "The request failed before the server returned a catalog."
                    : ex.Message;
            }
            finally
            {
                _productsLoading = false;
                EditorApplication.delayCall += () => RebuildProductPicker();
            }
        }

        private static string BuildProductLoadErrorMessage(long statusCode, string responseBody)
        {
            string trimmedBody = string.IsNullOrEmpty(responseBody) ? string.Empty : responseBody.Trim();
            if (!string.IsNullOrEmpty(trimmedBody))
            {
                return $"Server returned {(int)statusCode}: {trimmedBody}";
            }

            if (statusCode > 0)
            {
                return $"Server returned {(int)statusCode}.";
            }

            return "The request failed before the server returned a catalog.";
        }

        private bool ProductIsSelected(CanonicalProduct product, List<string> selectedIds)
        {
            if (product == null || selectedIds == null || selectedIds.Count == 0)
                return MatchesLegacyProviderSelection(product);

            foreach (var productId in GetProductIds(product))
            {
                if (selectedIds.Contains(productId))
                    return true;
            }

            return MatchesLegacyProviderSelection(product);
        }

        private static IEnumerable<string> GetProductIds(CanonicalProduct product)
        {
            if (product == null)
                yield break;

            if (product.productIds != null)
            {
                foreach (var productId in product.productIds)
                {
                    if (!string.IsNullOrEmpty(productId))
                        yield return productId;
                }
            }

            if (!string.IsNullOrEmpty(product.productId) && (product.productIds == null || !product.productIds.Contains(product.productId)))
                yield return product.productId;
        }

        private static string GetProductMergeKey(string displayName, string owner)
        {
            return NormalizeProductKey(displayName) + "||" + NormalizeProductKey(owner ?? string.Empty);
        }

        private static string NormalizeProductKey(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                    builder.Append(char.ToLowerInvariant(c));
            }
            return builder.ToString();
        }

        private bool MatchesLegacyProviderSelection(CanonicalProduct product)
        {
            if (_profile == null || product == null || product.providers == null)
                return false;

            return product.providers.Any(provider =>
                (string.Equals(provider.provider, "gumroad", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(provider.providerRef, _profile.gumroadProductId ?? string.Empty, StringComparison.OrdinalIgnoreCase)) ||
                (string.Equals(provider.provider, "jinxxy", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(provider.providerRef, _profile.jinxxyProductId ?? string.Empty, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool ProviderRefsOverlap(CanonicalProduct left, CanonicalProduct right)
        {
            if (left?.providers == null || right?.providers == null) return false;
            return left.providers.Any(
                existing => right.providers.Any(candidate =>
                    string.Equals(existing.provider, candidate.provider, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.providerRef ?? string.Empty, candidate.providerRef ?? string.Empty, StringComparison.OrdinalIgnoreCase)));
        }

        private static void MergeCanonicalProduct(List<CanonicalProduct> mergedProducts, CanonicalProduct candidate)
        {
            if (candidate == null) return;

            var existing = mergedProducts.FirstOrDefault(product =>
                (!string.IsNullOrEmpty(candidate.productId) &&
                 !string.IsNullOrEmpty(product.productId) &&
                 string.Equals(product.productId, candidate.productId, StringComparison.OrdinalIgnoreCase)) ||
                ProviderRefsOverlap(product, candidate) ||
                string.Equals(GetProductMergeKey(product.displayName, product.owner), GetProductMergeKey(candidate.displayName, candidate.owner), StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                mergedProducts.Add(candidate);
                return;
            }

            if (string.IsNullOrEmpty(existing.productId) && !string.IsNullOrEmpty(candidate.productId))
                existing.productId = candidate.productId;
            if ((string.IsNullOrEmpty(existing.displayName) || string.Equals(existing.displayName, existing.productId, StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrEmpty(candidate.displayName))
                existing.displayName = candidate.displayName;

            existing.configured |= candidate.configured;
            existing.live |= candidate.live;
            existing.localConfigured |= candidate.localConfigured;

            if (candidate.productIds != null)
            {
                foreach (var productId in candidate.productIds.Where(id => !string.IsNullOrEmpty(id)))
                {
                    if (!existing.productIds.Contains(productId))
                        existing.productIds.Add(productId);
                }
            }

            if (candidate.providers != null)
            {
                foreach (var provider in candidate.providers)
                {
                    bool alreadyPresent = existing.providers.Any(current =>
                        string.Equals(current.provider, provider.provider, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(current.providerRef ?? string.Empty, provider.providerRef ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                    if (!alreadyPresent)
                        existing.providers.Add(provider);
                }
            }
        }

        private List<CanonicalProduct> GetLocallyConfiguredProducts()
        {
            var configuredProducts = new List<CanonicalProduct>();
            foreach (var guid in AssetDatabase.FindAssets("t:ExportProfile"))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<ExportProfile>(assetPath);
                if (profile == null) continue;

                var productIds = new List<string>();
                if (!string.IsNullOrEmpty(profile.licenseProductId))
                    productIds.Add(profile.licenseProductId);
                if (profile.licenseProductIds != null)
                {
                    foreach (var productId in profile.licenseProductIds)
                    {
                        if (!string.IsNullOrEmpty(productId) && !productIds.Contains(productId))
                            productIds.Add(productId);
                    }
                }

                var providers = new List<ProviderRef>();
                if (!string.IsNullOrEmpty(profile.gumroadProductId))
                    providers.Add(new ProviderRef { provider = "gumroad", providerRef = profile.gumroadProductId });
                if (!string.IsNullOrEmpty(profile.jinxxyProductId))
                    providers.Add(new ProviderRef { provider = "jinxxy", providerRef = profile.jinxxyProductId });

                if (productIds.Count == 0 && providers.Count == 0)
                    continue;

                configuredProducts.Add(new CanonicalProduct
                {
                    productId = productIds.FirstOrDefault() ?? string.Empty,
                    productIds = productIds,
                    displayName = !string.IsNullOrEmpty(profile.packageName)
                        ? profile.packageName
                        : (productIds.FirstOrDefault() ?? providers.First().providerRef),
                    owner = null,
                    providers = providers,
                    configured = true,
                    localConfigured = true,
                });
            }

            return configuredProducts;
        }

        private void ApplySelectedProductMetadata(CanonicalProduct preferredProduct, List<string> selectedIds)
        {
            _profile.licenseProductIds = selectedIds.Distinct().ToList();
            _profile.licenseProductId = _profile.licenseProductIds.Count > 0 ? _profile.licenseProductIds[0] : string.Empty;

            CanonicalProduct primaryProduct = null;
            if (!string.IsNullOrEmpty(_profile.licenseProductId))
            {
                primaryProduct = _canonicalProducts?.FirstOrDefault(product =>
                    GetProductIds(product).Any(productId =>
                        string.Equals(productId, _profile.licenseProductId, StringComparison.OrdinalIgnoreCase)));
            }

            primaryProduct ??= preferredProduct;
            _profile.gumroadProductId = primaryProduct?.GetRef("gumroad") ?? string.Empty;
            _profile.jinxxyProductId = primaryProduct?.GetRef("jinxxy") ?? string.Empty;
        }

        private VisualElement BuildLicenseProtectionSection()
        {
            var body = MakePad(16, 18, 10, 16);

            // Section header
            body.Add(MakeLabel("License Protection", 10, TextMute, bold: true, letterSpacing: 0.5f, mb: 12));

            // Collect all profiles: main + bundled
            var allProfiles = new List<ExportProfile>();
            if (_profile != null)
            {
                allProfiles.Add(_profile);
                if (_profile.HasIncludedProfiles())
                    allProfiles.AddRange(_profile.GetIncludedProfiles().Where(p => p != null));
            }

            foreach (var prof in allProfiles)
            {
                if (prof == null) continue;
                body.Add(BuildLicenseRow(prof));
            }

            // Info note
            var note = MakeLabel(
                "When enabled, derived FBX assets require a verified purchase before they are applied. Licensed profiles automatically export through the embedded container/bootstrap path. Enabling this requires sign-in, an active certificate on this machine, and a plan with Protected Exports enabled.",
                9, TextMute, wrap: true);
            note.style.marginTop = 10;
            body.Add(note);

            return body;
        }

        private VisualElement BuildLicenseRow(ExportProfile prof)
        {
            bool isOn       = prof.requiresLicenseVerification;
            bool isSignedIn = YucpOAuthService.IsSignedIn();
            bool hasCertificateContext = HasEligibleLicenseCertificateContext();
            bool hasProtectedExports = HasActiveBillingCapability(ProtectedExportsCapabilityKey);
            bool showSignInCta = !isOn && !isSignedIn;
            bool showCertificateCta = !isOn && isSignedIn && !hasCertificateContext;
            bool showUpgradeCta = !isOn && isSignedIn && hasCertificateContext && !hasProtectedExports;
            string recommendedPlanKey = FindPlanKeyForCapability(ProtectedExportsCapabilityKey);

            // ── Row ───────────────────────────────────────────────────────────────
            var row = new VisualElement();
            row.style.flexDirection   = FlexDirection.Row;
            row.style.alignItems      = Align.Center;
            row.style.justifyContent  = Justify.SpaceBetween;
            row.style.paddingLeft     = 12;
            row.style.paddingRight    = 10;
            row.style.paddingTop      = 9;
            row.style.paddingBottom   = 9;
            row.style.marginBottom    = 6;
            row.style.backgroundColor = SurfaceRaise;
            row.style.borderTopLeftRadius = row.style.borderTopRightRadius =
                row.style.borderBottomLeftRadius = row.style.borderBottomRightRadius = 6;
            row.style.borderTopWidth = row.style.borderBottomWidth =
                row.style.borderLeftWidth = row.style.borderRightWidth = 1;
            row.style.borderTopColor = row.style.borderBottomColor =
                row.style.borderLeftColor = row.style.borderRightColor =
                    isOn ? new Color(0.212f, 0.749f, 0.694f, 0.20f) : Border;

            // Left: package name + id
            string displayName = !string.IsNullOrEmpty(prof.packageName)
                ? prof.packageName
                : (!string.IsNullOrEmpty(prof.packageId) ? prof.packageId : "Unnamed");

            var nameCol = new VisualElement();
            nameCol.style.flexGrow = 1;
            nameCol.Add(MakeLabel(displayName, 11, isOn ? TextPri : TextSec, mb: 0));
            if (!string.IsNullOrEmpty(prof.packageId))
            {
                var idLbl = MakeLabel(prof.packageId, 9, TextMute, mb: 0);
                idLbl.style.marginTop = 2;
                nameCol.Add(idLbl);
            }

            string stateMessage = null;
            if (showSignInCta)
            {
                stateMessage = "Sign in to enable licensed exports.";
            }
            else if (showCertificateCta)
            {
                stateMessage = "Restore or enroll a certificate on this machine to enable licensed exports.";
            }
            else if (showUpgradeCta)
            {
                stateMessage = "Protected Exports are not enabled on the current plan.";
            }
            else if (isOn)
            {
                stateMessage = "Embedded container/bootstrap mode is enabled automatically for licensed exports.";
            }

            if (!string.IsNullOrEmpty(stateMessage))
            {
                var stateLbl = MakeLabel(stateMessage, 9, TextMute, mb: 0, wrap: true);
                stateLbl.style.marginTop = 2;
                nameCol.Add(stateLbl);
            }
            row.Add(nameCol);

            // Right side
            var rightCol = new VisualElement();
            rightCol.style.flexDirection = FlexDirection.Row;
            rightCol.style.alignItems    = Align.Center;
            rightCol.style.flexShrink    = 0;
            rightCol.style.marginLeft    = 10;

            if (showSignInCta)
            {
                rightCol.Add(BuildLicenseActionChip(
                    "Sign in to enable",
                    EditorGUIUtility.IconContent("LockIcon-On").image,
                    new Color(0.212f, 0.749f, 0.694f, 0.10f),
                    new Color(0.212f, 0.749f, 0.694f, 0.28f),
                    Teal,
                    () =>
                    {
                        if (_isSigningIn) return;
                        _isSigningIn = true;
                        RefreshUI();
#pragma warning disable CS4014
                        YucpOAuthService.SignInAsync(GetServerUrl(),
                            onSuccess: () => EditorApplication.delayCall += () => { _isSigningIn = false; LoadSettings(); RefreshUI(); },
                            onError:   e  => EditorApplication.delayCall += () => { _isSigningIn = false; RefreshUI(); EditorUtility.DisplayDialog("Sign-in failed", e, "OK"); });
#pragma warning restore CS4014
                    }));
            }
            else if (showCertificateCta)
            {
                rightCol.Add(BuildLicenseActionChip(
                    "Certificates & Billing",
                    EditorGUIUtility.IconContent("d_Settings").image,
                    new Color(0.247f, 0.600f, 0.960f, 0.10f),
                    new Color(0.247f, 0.600f, 0.960f, 0.26f),
                    new Color(0.560f, 0.788f, 1.000f),
                    OpenAccountCertificatesPage));
            }
            else if (showUpgradeCta)
            {
                rightCol.Add(BuildLicenseActionChip(
                    "Creator Suite+ required",
                    EditorGUIUtility.IconContent("d_winbtn_mac_max").image,
                    new Color(0.925f, 0.690f, 0.255f, 0.12f),
                    new Color(0.925f, 0.690f, 0.255f, 0.28f),
                    new Color(1.000f, 0.855f, 0.486f),
                    () =>
                    {
                        if (!string.IsNullOrEmpty(recommendedPlanKey))
                        {
                            OpenCheckoutForPlan(recommendedPlanKey);
                        }
                        else
                        {
                            OpenBillingPortalPage();
                        }
                    }));
            }
            else
            {
                // ── Status pill ────────────────────────────────────────────────
                var pill = new VisualElement();
                pill.style.paddingLeft   = 8;
                pill.style.paddingRight  = 8;
                pill.style.paddingTop    = 3;
                pill.style.paddingBottom = 3;
                pill.style.marginRight   = 8;
                pill.style.borderTopLeftRadius = pill.style.borderTopRightRadius =
                    pill.style.borderBottomLeftRadius = pill.style.borderBottomRightRadius = 10;
                pill.style.backgroundColor = isOn
                    ? new Color(0.212f, 0.749f, 0.694f, 0.14f)
                    : new Color(0.18f, 0.18f, 0.18f);
                pill.Add(MakeLabel(isOn ? "ON" : "OFF", 9, isOn ? Teal : TextMute,
                    bold: true, letterSpacing: 0.5f, mb: 0));
                rightCol.Add(pill);

                // ── iOS-style toggle track ─────────────────────────────────────
                var track = new VisualElement();
                track.style.width   = 32;
                track.style.height  = 18;
                track.style.borderTopLeftRadius = track.style.borderTopRightRadius =
                    track.style.borderBottomLeftRadius = track.style.borderBottomRightRadius = 9;
                track.style.backgroundColor = isOn
                    ? new Color(0.212f, 0.749f, 0.694f, 0.85f)
                    : new Color(0.22f, 0.22f, 0.22f);
                track.style.flexShrink     = 0;
                track.style.justifyContent = Justify.Center;
                track.style.alignItems     = isOn ? Align.FlexEnd : Align.FlexStart;
                track.style.paddingLeft    = 2;
                track.style.paddingRight   = 2;
                var thumb = new VisualElement();
                thumb.style.width  = 14;
                thumb.style.height = 14;
                thumb.style.borderTopLeftRadius = thumb.style.borderTopRightRadius =
                    thumb.style.borderBottomLeftRadius = thumb.style.borderBottomRightRadius = 7;
                thumb.style.backgroundColor = Color.white;
                thumb.style.flexShrink = 0;
                track.Add(thumb);
                rightCol.Add(track);

                // Hover / click for the whole row
                row.RegisterCallback<MouseEnterEvent>(_ =>
                    row.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f));
                row.RegisterCallback<MouseLeaveEvent>(_ =>
                    row.style.backgroundColor = SurfaceRaise);
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    Undo.RecordObject(prof, "Toggle License Protection");
                    prof.requiresLicenseVerification = !prof.requiresLicenseVerification;
                    EditorUtility.SetDirty(prof);
                    EditorApplication.delayCall += () =>
                    {
                        RebuildLicenseSection();
                        _onProfileChanged?.Invoke();
                    };
                });
            }

            row.Add(rightCol);
            return row;
        }

        private bool HasEligibleLicenseCertificateContext()
        {
            return _accountState?.currentDeviceKnown == true && _accountState?.billing != null && _accountState.billing.allowSigning;
        }

        private bool HasActiveBillingCapability(string capabilityKey)
        {
            if (string.IsNullOrEmpty(capabilityKey) || _accountState?.billing?.capabilities == null)
                return false;

            foreach (var capability in _accountState.billing.capabilities)
            {
                if (capability == null)
                    continue;

                if (string.Equals(capability.capabilityKey, capabilityKey, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(capability.status, "active", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string FindPlanKeyForCapability(string capabilityKey)
        {
            if (string.IsNullOrEmpty(capabilityKey) || _accountState?.availablePlans == null)
                return null;

            foreach (var plan in _accountState.availablePlans.OrderByDescending(plan => plan.priority))
            {
                if (plan?.capabilities == null)
                    continue;

                if (plan.capabilities.Any(entry => string.Equals(entry, capabilityKey, StringComparison.OrdinalIgnoreCase)))
                    return plan.planKey;
            }

            return null;
        }

        private VisualElement BuildLicenseActionChip(
            string label,
            Texture iconTexture,
            Color backgroundColor,
            Color borderColor,
            Color textColor,
            Action onClick)
        {
            var chip = new VisualElement();
            chip.style.flexDirection = FlexDirection.Row;
            chip.style.alignItems = Align.Center;
            chip.style.paddingLeft = 10;
            chip.style.paddingRight = 10;
            chip.style.paddingTop = 5;
            chip.style.paddingBottom = 5;
            chip.style.borderTopLeftRadius = chip.style.borderTopRightRadius =
                chip.style.borderBottomLeftRadius = chip.style.borderBottomRightRadius = 5;
            chip.style.backgroundColor = backgroundColor;
            chip.style.borderTopWidth = chip.style.borderBottomWidth =
                chip.style.borderLeftWidth = chip.style.borderRightWidth = 1;
            chip.style.borderTopColor = chip.style.borderBottomColor =
                chip.style.borderLeftColor = chip.style.borderRightColor = borderColor;

            if (iconTexture != null)
            {
                var icon = new Image { image = iconTexture };
                icon.style.width = 11;
                icon.style.height = 11;
                icon.style.flexShrink = 0;
                icon.style.marginRight = 6;
                chip.Add(icon);
            }

            chip.Add(MakeLabel(label, 10, textColor, mb: 0));

            chip.RegisterCallback<MouseEnterEvent>(_ =>
                chip.style.backgroundColor = new Color(
                    Mathf.Clamp01(backgroundColor.r + 0.05f),
                    Mathf.Clamp01(backgroundColor.g + 0.05f),
                    Mathf.Clamp01(backgroundColor.b + 0.05f),
                    Mathf.Clamp01(backgroundColor.a + 0.08f)));
            chip.RegisterCallback<MouseLeaveEvent>(_ => chip.style.backgroundColor = backgroundColor);
            chip.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                onClick?.Invoke();
            });

            return chip;
        }

        private VisualElement _licenseSectionSlot;

        private void RebuildLicenseSection()
        {
            if (_licenseSectionSlot == null) return;
            _licenseSectionSlot.Clear();
            _licenseSectionSlot.Add(BuildLicenseProtectionSection());
        }

        // ── Account bar (shared across states 2 & 3) ──────────────────────────────

        private VisualElement BuildAccountBar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection  = FlexDirection.Row;
            bar.style.alignItems     = Align.Center;
            bar.style.justifyContent = Justify.SpaceBetween;
            bar.style.paddingLeft    = 16;
            bar.style.paddingRight   = 14;
            bar.style.paddingTop     = 12;
            bar.style.paddingBottom  = 12;

            // Left: avatar + name
            string name = YucpOAuthService.GetDisplayName() ?? "Creator";

            var left = new VisualElement();
            left.style.flexDirection = FlexDirection.Row;
            left.style.alignItems    = Align.Center;

            var avatar = SigningProfileAvatar.Create(
                name,
                YucpOAuthService.GetProfileImageUrl(),
                28f,
                1f,
                TealSub,
                new Color(0.212f, 0.749f, 0.694f, 0.35f),
                Teal,
                11);
            avatar.style.marginRight   = 9;
            avatar.style.flexShrink    = 0;
            left.Add(avatar);
            left.Add(MakeLabel(name, 12, TextPri, bold: true, mb: 0));
            bar.Add(left);

            bar.Add(MakeGhostButton("Sign out", OnSignOutClicked, small: true));
            return bar;
        }

        // ── Shared button builders ─────────────────────────────────────────────────

        private VisualElement BuildPrimaryButton(string text, Action onClick)
        {
            var btn = MakeRoundedBox(Teal, 8, 0, Color.clear);
            btn.style.paddingTop     = 11;
            btn.style.paddingBottom  = 11;
            btn.style.alignItems     = Align.Center;
            btn.style.justifyContent = Justify.Center;
            var lbl = MakeLabel(text, 13, new Color(0.07f, 0.07f, 0.07f), bold: true, mb: 0);
            btn.Add(lbl);
            btn.RegisterCallback<MouseEnterEvent>(_ => btn.style.opacity = 0.88f);
            btn.RegisterCallback<MouseLeaveEvent>(_ => btn.style.opacity = 1.0f);
            btn.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());
            return btn;
        }

        private VisualElement BuildLoadingButton(string text)
        {
            var btn = MakeRoundedBox(new Color(0.212f, 0.749f, 0.694f, 0.35f), 8, 1,
                new Color(0.212f, 0.749f, 0.694f, 0.4f));
            btn.style.paddingTop     = 11;
            btn.style.paddingBottom  = 11;
            btn.style.alignItems     = Align.Center;
            btn.style.justifyContent = Justify.Center;
            btn.SetEnabled(false);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;

            var dot = new VisualElement();
            dot.style.width  = 5;
            dot.style.height = 5;
            dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius =
                dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 3;
            dot.style.backgroundColor = Teal;
            dot.style.marginRight     = 9;
            dot.schedule.Execute(() => dot.style.opacity = dot.style.opacity.value > 0.6f ? 0.25f : 1.0f).Every(500);
            row.Add(dot);
            row.Add(MakeLabel(text, 13, TextSec, mb: 0));
            btn.Add(row);
            return btn;
        }

        private VisualElement MakeGhostButton(string text, Action onClick, bool small = false)
        {
            var btn = new VisualElement();
            btn.style.paddingLeft   = small ? 8 : 12;
            btn.style.paddingRight  = small ? 8 : 12;
            btn.style.paddingTop    = small ? 4 : 7;
            btn.style.paddingBottom = small ? 4 : 7;
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 6;
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = 1;
            btn.style.borderTopColor = btn.style.borderBottomColor =
                btn.style.borderLeftColor = btn.style.borderRightColor = Border;
            var lbl = MakeLabel(text, small ? 10 : 12, TextSec, mb: 0);
            btn.Add(lbl);
            btn.RegisterCallback<MouseEnterEvent>(_ => { btn.style.borderTopColor = btn.style.borderBottomColor = btn.style.borderLeftColor = btn.style.borderRightColor = TextMute; lbl.style.color = TextPri; });
            btn.RegisterCallback<MouseLeaveEvent>(_ => { btn.style.borderTopColor = btn.style.borderBottomColor = btn.style.borderLeftColor = btn.style.borderRightColor = Border; lbl.style.color = TextSec; });
            btn.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());
            return btn;
        }

        // ── Status pill ────────────────────────────────────────────────────────────

        private VisualElement BuildStatusPill(string status)
        {
            Color bg, fg;
            string text;
            switch (status)
            {
                case "active":  bg = TealSub; fg = Teal;  text = "ACTIVE";  break;
                case "revoked": bg = new Color(0.6f, 0.2f, 0.2f, 0.25f); fg = Red;   text = "REVOKED"; break;
                default:        bg = new Color(0.5f, 0.5f, 0.2f, 0.25f); fg = Amber; text = status.ToUpper(); break;
            }
            var pill = MakeRoundedBox(bg, 4, 0, Color.clear);
            pill.style.paddingLeft   = 7;
            pill.style.paddingRight  = 7;
            pill.style.paddingTop    = 3;
            pill.style.paddingBottom = 3;
            pill.Add(MakeLabel(text, 9, fg, bold: true, letterSpacing: 0.8f, mb: 0));
            return pill;
        }

        // ── Action handlers ────────────────────────────────────────────────────────

        private void OnSignOutClicked()
        {
            YucpOAuthService.SignOut();
            _accountState = null;
            _isLoadingAccountState = false;
            RefreshUI();
        }

        private async void OnRequestCertClicked()
        {
            if (_isRequestingCert) return;

            if (_accountState?.billing != null &&
                !_accountState.billing.allowEnrollment &&
                !_accountState.currentDeviceKnown)
            {
                if (EditorUtility.DisplayDialog(
                    "Certificate enrollment blocked",
                    _accountState.error ?? "This machine cannot enroll a certificate right now.",
                    "Open Certificates & Billing",
                    "Cancel"))
                {
                    OpenAccountCertificatesPage();
                }
                return;
            }

            if (_accountState?.deviceCapReachedForCurrentMachine == true)
            {
                if (EditorUtility.DisplayDialog(
                    "Device limit reached",
                    _accountState.error ?? "This plan has no free device slots for this machine.",
                    "Manage Devices",
                    "Cancel"))
                {
                    OpenAccountCertificatesPage();
                }
                return;
            }

            string serverUrl = GetServerUrl();
            string accessToken = await YucpOAuthService.GetValidAccessTokenAsync(serverUrl);
            if (string.IsNullOrEmpty(accessToken))
            {
                EditorUtility.DisplayDialog("Not signed in", "Please sign in before requesting a certificate.", "OK");
                return;
            }

            string devPublicKey  = DevKeyManager.GetPublicKeyBase64();
            string publisherName = YucpOAuthService.GetDisplayName() ?? "YUCP Creator";
            var    service       = new PackageSigningService(serverUrl);

            _isRequestingCert = true;
            RefreshUI();

            try
            {
                var refreshedAccountState = await service.GetCertificateAccountStateAsync(accessToken, devPublicKey);
                if (refreshedAccountState != null)
                {
                    _accountState = refreshedAccountState;
                }

                if (_accountState?.billing != null)
                {
                    if (!_accountState.billing.allowEnrollment && !_accountState.currentDeviceKnown)
                    {
                        _isRequestingCert = false;
                        RefreshUI();
                        if (EditorUtility.DisplayDialog(
                            "Certificate enrollment blocked",
                            _accountState.error ?? "This machine cannot enroll a certificate right now.",
                            "Open Certificates & Billing",
                            "Cancel"))
                        {
                            OpenAccountCertificatesPage();
                        }
                        return;
                    }

                    if (!_accountState.billing.allowSigning && _accountState.currentDeviceKnown)
                    {
                        _isRequestingCert = false;
                        RefreshUI();
                        if (EditorUtility.DisplayDialog(
                            "Certificate restore blocked",
                            _accountState.error ?? "This machine cannot restore its certificate until billing is fixed.",
                            "Open Certificates & Billing",
                            "Cancel"))
                        {
                            OpenAccountCertificatesPage();
                        }
                        return;
                    }

                    if (_accountState.deviceCapReachedForCurrentMachine)
                    {
                        _isRequestingCert = false;
                        RefreshUI();
                        if (EditorUtility.DisplayDialog(
                            "Device limit reached",
                            _accountState.error ?? "This plan has no free device slots for this machine.",
                            "Manage Devices",
                            "Cancel"))
                        {
                            OpenAccountCertificatesPage();
                        }
                        return;
                    }
                }

                string restoredJson = await service.RestoreCertificateAsync(accessToken, devPublicKey);
                if (!string.IsNullOrEmpty(restoredJson))
                {
                    _isRequestingCert = false;
                    var restoreResult = CertificateManager.ImportAndVerifyFromJson(restoredJson);
                    if (restoreResult.valid)
                    {
                        LoadSettings();
                        EnsureAccountStateRefresh(force: true);
                        RefreshUI();
                        return;
                    }
                }

                await RequestNewCertificateAsync(service, accessToken, devPublicKey, publisherName);
            }
            catch (Exception ex)
            {
                _isRequestingCert = false;
                RefreshUI();
                EditorUtility.DisplayDialog("Certificate failed", ex.Message, "OK");
            }
        }

        private async Task RequestNewCertificateAsync(
            PackageSigningService service, string accessToken,
            string devPublicKey, string publisherName)
        {
            var (success, responseCode, error, certJson) = await service.RequestCertificateAsync(accessToken, devPublicKey, publisherName);
            _isRequestingCert = false;
            if (!success)
            {
                RefreshUI();
                string friendly = PackageSigningService.NormalizeCertificateRequestError(
                    responseCode,
                    error,
                    _accountState?.currentDeviceKnown == true);
                bool openAccount = friendly.IndexOf("Certificates & Billing", StringComparison.OrdinalIgnoreCase) >= 0;
                if (openAccount && EditorUtility.DisplayDialog(
                    "Certificate failed",
                    friendly,
                    "Open Certificates & Billing",
                    "Close"))
                {
                    OpenAccountCertificatesPage();
                }
                else if (!openAccount)
                {
                    EditorUtility.DisplayDialog("Certificate failed", friendly, "OK");
                }
                return;
            }

            var result = CertificateManager.ImportAndVerifyFromJson(certJson);
            if (result.valid)
            {
                LoadSettings();
                EnsureAccountStateRefresh(force: true);
                RefreshUI();
                return;
            }

            RefreshUI();
            EditorUtility.DisplayDialog("Certificate error", result.error, "OK");
        }

        // ── Low-level helpers ──────────────────────────────────────────────────────

        private static VisualElement MakeCard()
        {
            var c = new VisualElement();
            c.style.backgroundColor = new Color(0.102f, 0.102f, 0.102f);
            c.style.borderTopLeftRadius      = 10;
            c.style.borderTopRightRadius     = 10;
            c.style.borderBottomLeftRadius   = 10;
            c.style.borderBottomRightRadius  = 10;
            // Remove borders to match Quick Actions section
            c.style.borderTopWidth = c.style.borderBottomWidth =
                c.style.borderLeftWidth = c.style.borderRightWidth = 0;
            c.style.borderTopColor = c.style.borderBottomColor =
                c.style.borderLeftColor = c.style.borderRightColor = Color.clear;
            c.style.overflow    = Overflow.Hidden;
            c.style.marginBottom = 4;
            return c;
        }

        private static VisualElement MakePad(float l, float b, float r, float t)
        {
            var v = new VisualElement();
            v.style.paddingLeft   = l;
            v.style.paddingBottom = b;
            v.style.paddingRight  = r;
            v.style.paddingTop    = t;
            return v;
        }

        private static VisualElement MakeDivider()
        {
            var d = new VisualElement();
            d.style.height          = 1;
            d.style.backgroundColor = Border;
            return d;
        }

        private static VisualElement MakeRoundedBox(Color bg, float radius, float borderW, Color borderColor)
        {
            var b = new VisualElement();
            b.style.backgroundColor          = bg;
            b.style.borderTopLeftRadius      = radius;
            b.style.borderTopRightRadius     = radius;
            b.style.borderBottomLeftRadius   = radius;
            b.style.borderBottomRightRadius  = radius;
            b.style.borderTopWidth = b.style.borderBottomWidth =
                b.style.borderLeftWidth = b.style.borderRightWidth = borderW;
            b.style.borderTopColor = b.style.borderBottomColor =
                b.style.borderLeftColor = b.style.borderRightColor = borderColor;
            return b;
        }

        private static Label MakeLabel(string text, float size, Color color, bool bold = false,
            float letterSpacing = 0f, float mb = 0f, bool wrap = false,
            TextAnchor align = TextAnchor.UpperLeft)
        {
            // Sanitize text to remove characters that SDF font assets may not contain
            // (notably emoji which are outside the BMP and often missing from editor SDF fonts).
            var clean = RemoveSurrogatePairs(text);
            var l = new Label(clean);
            l.style.fontSize              = size;
            l.style.color                 = color;
            l.style.marginBottom          = mb;
            l.style.unityTextAlign        = align;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (letterSpacing > 0f) l.style.letterSpacing = letterSpacing;
            if (wrap) l.style.whiteSpace  = WhiteSpace.Normal;
            return l;
        }

        // Remove surrogate pair characters (emoji and other non-BMP glyphs) because
        // UI Toolkit SDF font assets used in the Editor often don't include them
        // and will log a warning. This keeps labels clean and avoids spamming the console.
        private static string RemoveSurrogatePairs(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                // Skip high surrogate and its following low surrogate (surrogate pair)
                if (char.IsHighSurrogate(c))
                {
                    // If next is low surrogate, skip both
                    if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    {
                        i++; // skip next low surrogate
                        continue;
                    }
                    // otherwise skip the isolated high surrogate
                    continue;
                }
                // Skip isolated low surrogate as well
                if (char.IsLowSurrogate(c)) continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static void ApplyInputStyle(TextField f)
        {
            f.style.backgroundColor        = new Color(0.102f, 0.102f, 0.102f);
            f.style.borderTopLeftRadius    = f.style.borderTopRightRadius    =
                f.style.borderBottomLeftRadius = f.style.borderBottomRightRadius = 5;
            f.style.borderTopWidth = f.style.borderBottomWidth =
                f.style.borderLeftWidth = f.style.borderRightWidth = 1;
            f.style.borderTopColor = f.style.borderBottomColor =
                f.style.borderLeftColor = f.style.borderRightColor =
                    new Color(0.20f, 0.20f, 0.20f);
        }

        // Overload for DropdownField so dropdowns receive the same input styling
        private static void ApplyInputStyle(DropdownField f)
        {
            f.style.backgroundColor        = new Color(0.102f, 0.102f, 0.102f);
            f.style.borderTopLeftRadius    = f.style.borderTopRightRadius    =
                f.style.borderBottomLeftRadius = f.style.borderBottomRightRadius = 5;
            f.style.borderTopWidth = f.style.borderBottomWidth =
                f.style.borderLeftWidth = f.style.borderRightWidth = 1;
            f.style.borderTopColor = f.style.borderBottomColor =
                f.style.borderLeftColor = f.style.borderRightColor =
                    new Color(0.20f, 0.20f, 0.20f);
        }

        // ── Utilities ──────────────────────────────────────────────────────────────

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
            if (!string.IsNullOrEmpty(_profile?.signingServerUrl)) return _profile.signingServerUrl;
            if (!string.IsNullOrEmpty(_settings?.serverUrl)) return _settings.serverUrl;
            string fromService = PackageSigningService.GetServerUrl();
            return !string.IsNullOrEmpty(fromService) ? fromService : "https://api.creators.yucp.club";
        }

        private string GetAccountCertificatesUrl()
        {
            return _settings?.GetEffectiveAccountCertificatesUrl(GetServerUrl()) ?? "https://creators.yucp.club/dashboard/certificates";
        }

        private static Dictionary<string, string> ParseUrlQuery(string query)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query))
                return values;

            foreach (var pair in query.TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var pieces = pair.Split(new[] { '=' }, 2);
                string key = Uri.UnescapeDataString(pieces[0]);
                if (string.IsNullOrEmpty(key))
                    continue;

                string value = pieces.Length > 1 ? Uri.UnescapeDataString(pieces[1]) : "";
                values[key] = value;
            }

            return values;
        }

        private string BuildAccountCertificatesUrl(string planKey = null, bool openCheckout = false, bool openPortal = false)
        {
            string baseUrl = GetAccountCertificatesUrl();
            if (string.IsNullOrEmpty(baseUrl))
                return null;

            try
            {
                var builder = new UriBuilder(baseUrl);
                var query = ParseUrlQuery(builder.Query);
                query["source"] = "unity-package-signing";

                if (!string.IsNullOrEmpty(planKey))
                {
                    query["plan"] = planKey;
                }

                if (openCheckout)
                {
                    query["checkout"] = "1";
                }

                if (openPortal)
                {
                    query["portal"] = "1";
                }

                builder.Query = string.Join("&",
                    query.Select(entry => $"{Uri.EscapeDataString(entry.Key)}={Uri.EscapeDataString(entry.Value ?? "")}"));
                return builder.Uri.ToString();
            }
            catch
            {
                return baseUrl;
            }
        }

        private void OpenAccountCertificatesPage()
        {
            string url = BuildAccountCertificatesUrl();
            if (string.IsNullOrEmpty(url))
            {
                EditorUtility.DisplayDialog(
                    "Certificates & Billing",
                    "No account URL is configured for this signing provider.",
                    "OK");
                return;
            }

            Application.OpenURL(url);
        }

        private void OpenCheckoutForPlan(string planKey)
        {
            string url = BuildAccountCertificatesUrl(planKey, openCheckout: true);
            if (string.IsNullOrEmpty(url))
            {
                EditorUtility.DisplayDialog(
                    "Certificates & Billing",
                    "No account URL is configured for this signing provider.",
                    "OK");
                return;
            }

            Application.OpenURL(url);
        }

        private void OpenBillingPortalPage()
        {
            string url = BuildAccountCertificatesUrl(openPortal: true);
            if (string.IsNullOrEmpty(url))
            {
                EditorUtility.DisplayDialog(
                    "Certificates & Billing",
                    "No account URL is configured for this signing provider.",
                    "OK");
                return;
            }

            Application.OpenURL(url);
        }

        private string TryGetCurrentDevPublicKey()
        {
            try
            {
                return DevKeyManager.GetPublicKeyBase64();
            }
            catch
            {
                return null;
            }
        }

        private void EnsureAccountStateRefresh(bool force = false)
        {
            if (!YucpOAuthService.IsSignedIn())
            {
                _accountState = null;
                _isLoadingAccountState = false;
                return;
            }

            string serverUrl = GetServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
                return;

            bool stale = _accountState == null ||
                !string.Equals(_accountStateServerUrl, serverUrl, StringComparison.Ordinal) ||
                EditorApplication.timeSinceStartup - _accountStateRefreshedAt > AccountStateRefreshIntervalSeconds;
            if (!force && (!stale || _isLoadingAccountState))
                return;

            if (force && _isLoadingAccountState)
                return;

            _isLoadingAccountState = true;
            _accountStateServerUrl = serverUrl;
#pragma warning disable CS4014
            RefreshAccountStateAsync(serverUrl);
#pragma warning restore CS4014
        }

        private async Task RefreshAccountStateAsync(string serverUrl)
        {
            PackageSigningService.CertificateAccountState nextState = null;
            try
            {
                string accessToken = await YucpOAuthService.GetValidAccessTokenAsync(serverUrl);
                if (!string.IsNullOrEmpty(accessToken))
                {
                    var service = new PackageSigningService(serverUrl);
                    nextState = await service.GetCertificateAccountStateAsync(
                        accessToken,
                        TryGetCurrentDevPublicKey());
                }
            }
            catch (Exception ex)
            {
                nextState = new PackageSigningService.CertificateAccountState
                {
                    error = ex.Message,
                };
            }

            EditorApplication.delayCall += () =>
            {
                _accountState = nextState;
                _isLoadingAccountState = false;
                _accountStateRefreshedAt = EditorApplication.timeSinceStartup;
                RefreshUI();
            };
        }
    }
}
