using System;
using System.Collections.Generic;
using System.Linq;
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

        // ── Product catalog cache ──────────────────────────────────────────────────
        // Loaded once per sign-in from GET /v1/products. The server groups by canonical
        // productId so one entry = one logical product across all providers.
        private class CanonicalProduct
        {
            public string productId;
            public string displayName;
            public string owner;   // null = own product; non-null = collaborator's product (owner's name)
            public List<ProviderRef> providers = new List<ProviderRef>();
            public string GetRef(string p) { foreach (var r in providers) if (r.provider == p) return r.providerRef; return null; }
        }
        private struct ProviderRef { public string provider; public string providerRef; }
        private List<CanonicalProduct> _canonicalProducts;
        private bool  _productsLoading;
        private bool  _productPickerExpanded;
        private VisualElement _productPickerSlot;
        private VisualElement _productListPanel;

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

        public PackageSigningTab(ExportProfile profile = null) => _profile = profile;

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
            return YucpOAuthService.IsSignedIn() && _settings != null && _settings.HasValidCertificate();
        }

        // ── State dispatcher ───────────────────────────────────────────────────────

        private VisualElement BuildCard()
        {
            if (!YucpOAuthService.IsSignedIn()) return BuildSignInHero();
            bool hasCert = _settings != null && _settings.HasValidCertificate();
            return hasCert ? BuildActiveCard() : BuildNoCertCard();
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

            // Empty-state icon ring
            var ring = MakeRoundedBox(Color.clear, 28, 1, new Color(0.212f, 0.749f, 0.694f, 0.25f));
            ring.style.width  = 56;
            ring.style.height = 56;
            ring.style.alignItems    = Align.Center;
            ring.style.justifyContent = Justify.Center;
            ring.style.alignSelf     = Align.Center;
            ring.style.marginBottom  = 16;

            var ringInner = new VisualElement();
            ringInner.style.width  = 20;
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

            body.Add(MakeLabel("No certificate yet", 14, TextPri, bold: true, align: TextAnchor.MiddleCenter, mb: 6));
            body.Add(MakeLabel("Get one to start signing your packages.", 11, TextSec, align: TextAnchor.MiddleCenter, mb: 22, wrap: true));

            // Primary action
            var getBtn = _isRequestingCert
                ? BuildLoadingButton("Getting certificate\u2026")
                : BuildPrimaryButton("\u2726  Get Certificate", OnRequestCertClicked);
            getBtn.style.marginBottom = 14;
            body.Add(getBtn);

            // Import ghost link
            var importRow = new VisualElement();
            importRow.style.flexDirection  = FlexDirection.Row;
            importRow.style.alignItems     = Align.Center;
            importRow.style.justifyContent = Justify.Center;
            var importHint = MakeLabel("Have a .yucp_cert file?", 11, TextMute, mb: 0);
            importHint.style.marginRight = 5;
            importRow.Add(importHint);
            importRow.Add(MakeGhostButton("Import \u2197", () =>
            {
                string path = EditorUtility.OpenFilePanel("Import Certificate", "", "yucp_cert");
                if (string.IsNullOrEmpty(path)) return;
                var r = CertificateManager.ImportAndVerify(path);
                if (r.valid) { LoadSettings(); RefreshUI(); }
                else EditorUtility.DisplayDialog("Import failed", r.error, "OK");
            }, small: true));
            body.Add(importRow);

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

            // ── Status banner ──────────────────────────────────────────────────────
            var statusBand = new VisualElement();
            statusBand.style.backgroundColor  = TealSub;
            statusBand.style.flexDirection    = FlexDirection.Row;
            statusBand.style.alignItems       = Align.Center;
            statusBand.style.paddingLeft      = 18;
            statusBand.style.paddingRight     = 18;
            statusBand.style.paddingTop       = 11;
            statusBand.style.paddingBottom    = 11;

            // Pulsing live dot
            var liveDot = new VisualElement();
            liveDot.style.width  = 7;
            liveDot.style.height = 7;
            liveDot.style.borderTopLeftRadius = liveDot.style.borderTopRightRadius =
                liveDot.style.borderBottomLeftRadius = liveDot.style.borderBottomRightRadius = 4;
            liveDot.style.backgroundColor = Teal;
            liveDot.style.marginRight     = 10;
            liveDot.style.flexShrink      = 0;
            liveDot.schedule.Execute(() => liveDot.style.opacity = liveDot.style.opacity.value > 0.6f ? 0.35f : 1.0f).Every(900);
            statusBand.Add(liveDot);

            statusBand.Add(MakeLabel("Ready to sign", 13, TextPri, bold: true));
            card.Add(statusBand);

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

            card.Add(certBody);

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
                card.Add(BuildLicenseProtectionSection());
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
            footer.Add(MakeGhostButton("Manage \u2192", () => SigningSettingsWindow.ShowWindow()));
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

            return body;
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

            // Empty state
            if (_canonicalProducts == null || _canonicalProducts.Count == 0)
            {
                var emptyCard = MakePickerShell();
                emptyCard.style.paddingTop = emptyCard.style.paddingBottom = 10;
                emptyCard.Add(MakeLabel("No products found", 11, TextSec, bold: true, mb: 3));
                emptyCard.Add(MakeLabel("Add your store products at yucp.app, then refresh.", 10, TextMute, wrap: true));
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
                .Where(p => selectedIds.Contains(p.productId))
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

            panel.Add(BuildPickerItem(null)); // "No product" entry
            foreach (var product in _canonicalProducts)
            {
                panel.Add(MakeListDivider());
                panel.Add(BuildPickerItem(product));
            }
            return panel;
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
            bool isSelected = !isNone && selectedIds.Contains(product.productId);

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
                    if (ids.Contains(product.productId))
                        ids.Remove(product.productId);
                    else
                        ids.Add(product.productId);
                    _profile.licenseProductIds = ids;
                    // Keep licenseProductId as the first selection for back-compat
                    _profile.licenseProductId  = ids.Count > 0 ? ids[0] : "";
                    // Sync provider-specific refs from the first selected product
                    var first = _canonicalProducts?.Find(p => p.productId == _profile.licenseProductId);
                    _profile.gumroadProductId  = first?.GetRef("gumroad") ?? "";
                    _profile.jinxxyProductId   = first?.GetRef("jinxxy")  ?? "";
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
            public ProductProviderJson[] providers;
        }

        [Serializable]
        private class ProductsResponse { public CanonicalProductJson[] products; }

        private async void LoadCreatorProducts()
        {
            string accessToken = YucpOAuthService.GetAccessToken();
            if (string.IsNullOrEmpty(accessToken)) return;

            string serverUrl = GetServerUrl();
            _productsLoading = true;
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
                    _canonicalProducts = new List<CanonicalProduct>();
                    if (parsed?.products != null)
                    {
                        foreach (var p in parsed.products)
                        {
                            var entry = new CanonicalProduct
                            {
                                productId   = p.productId,
                                displayName = !string.IsNullOrEmpty(p.displayName) ? p.displayName : p.productId,
                                owner       = string.IsNullOrEmpty(p.owner) ? null : p.owner,
                                providers   = new List<ProviderRef>(),
                            };
                            if (p.providers != null)
                            {
                                foreach (var prov in p.providers)
                                {
                                    if (!string.IsNullOrEmpty(prov.provider))
                                        entry.providers.Add(new ProviderRef { provider = prov.provider, providerRef = prov.providerProductRef ?? "" });
                                }
                            }
                            _canonicalProducts.Add(entry);
                        }
                    }
                }
                else
                {
                    _canonicalProducts = new List<CanonicalProduct>();
                }
            }
            catch
            {
                _canonicalProducts = new List<CanonicalProduct>();
            }
            finally
            {
                _productsLoading = false;
                EditorApplication.delayCall += () => RebuildProductPicker();
            }
        }

        // ── License Protection section ─────────────────────────────────────────────

        private VisualElement BuildLicenseProtectionSection()
        {
            var body = MakePad(16, 18, 10, 16);

            // Header row
            var headerRow = new VisualElement();
            headerRow.style.flexDirection  = FlexDirection.Row;
            headerRow.style.alignItems     = Align.Center;
            headerRow.style.justifyContent = Justify.SpaceBetween;
            headerRow.style.marginBottom   = 10;
            headerRow.Add(MakeLabel("License Protection", 11, TextMute, bold: true, mb: 0));
            body.Add(headerRow);

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
                bool isLicensed = prof.requiresLicenseVerification;

                var row = new VisualElement();
                row.style.flexDirection  = FlexDirection.Row;
                row.style.alignItems     = Align.Center;
                row.style.justifyContent = Justify.SpaceBetween;
                row.style.marginBottom   = 6;
                row.style.paddingLeft    = 4;
                row.style.paddingRight   = 4;
                row.style.paddingTop     = 6;
                row.style.paddingBottom  = 6;
                row.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.6f);
                row.style.borderTopLeftRadius = row.style.borderTopRightRadius =
                    row.style.borderBottomLeftRadius = row.style.borderBottomRightRadius = 4;

                // Package name
                string displayName = !string.IsNullOrEmpty(prof.packageName)
                    ? prof.packageName
                    : (!string.IsNullOrEmpty(prof.packageId) ? prof.packageId : "Unnamed");

                var nameCol = new VisualElement();
                nameCol.style.flexGrow = 1;
                nameCol.Add(MakeLabel(displayName, 11, TextPri, mb: 0));
                if (!string.IsNullOrEmpty(prof.packageId))
                    nameCol.Add(MakeLabel(prof.packageId, 9, TextMute, mb: 0));
                row.Add(nameCol);

                // Toggle
                var toggle = new Toggle { value = isLicensed };
                toggle.style.flexShrink = 0;

                toggle.RegisterValueChangedCallback(e =>
                {
                    var target = prof; // capture
                    Undo.RecordObject(target, "Toggle License Protection");
                    target.requiresLicenseVerification = e.newValue;
                    EditorUtility.SetDirty(target);
                });
                row.Add(toggle);

                body.Add(row);
            }

            // Info note
            var note = MakeLabel(
                "When enabled, derived FBX assets require a verified purchase before they are applied.",
                9, TextMute, wrap: true);
            note.style.marginTop = 4;
            body.Add(note);

            return body;
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
            string init = name.Length > 0 ? name.Substring(0, 1).ToUpper() : "C";

            var left = new VisualElement();
            left.style.flexDirection = FlexDirection.Row;
            left.style.alignItems    = Align.Center;

            var avatar = MakeRoundedBox(TealSub, 15, 1, new Color(0.212f, 0.749f, 0.694f, 0.35f));
            avatar.style.width  = 28;
            avatar.style.height = 28;
            avatar.style.alignItems    = Align.Center;
            avatar.style.justifyContent = Justify.Center;
            avatar.style.marginRight   = 9;
            avatar.style.flexShrink    = 0;
            var initLbl = MakeLabel(init, 11, Teal, bold: true, mb: 0, align: TextAnchor.MiddleCenter);
            avatar.Add(initLbl);
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
            RefreshUI();
        }

        private void OnRequestCertClicked()
        {
            if (_isRequestingCert) return;

            string accessToken = YucpOAuthService.GetAccessToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                EditorUtility.DisplayDialog("Not signed in", "Please sign in before requesting a certificate.", "OK");
                return;
            }

            string devPublicKey  = DevKeyManager.GetPublicKeyBase64();
            string publisherName = YucpOAuthService.GetDisplayName() ?? "YUCP Creator";
            string serverUrl     = GetServerUrl();
            var    service       = new PackageSigningService(serverUrl);

            _isRequestingCert = true;
            RefreshUI();

            _ = service.RestoreCertificateAsync(accessToken, devPublicKey)
                .ContinueWith(restoreTask =>
                {
                    string restoredJson = restoreTask.Result;
                    if (!string.IsNullOrEmpty(restoredJson))
                    {
                        EditorApplication.delayCall += () =>
                        {
                            _isRequestingCert = false;
                            var r = CertificateManager.ImportAndVerifyFromJson(restoredJson);
                            if (r.valid) { LoadSettings(); RefreshUI(); }
                            else RequestNewCertificate(service, accessToken, devPublicKey, publisherName);
                        };
                        return;
                    }
                    EditorApplication.delayCall += () =>
                        RequestNewCertificate(service, accessToken, devPublicKey, publisherName);
                });
        }

        private void RequestNewCertificate(
            PackageSigningService service, string accessToken,
            string devPublicKey, string publisherName)
        {
            _ = service.RequestCertificateAsync(accessToken, devPublicKey, publisherName)
                .ContinueWith(task =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        _isRequestingCert = false;
                        var (success, error, certJson) = task.Result;
                        if (!success) { RefreshUI(); EditorUtility.DisplayDialog("Certificate failed", error, "OK"); return; }
                        var r = CertificateManager.ImportAndVerifyFromJson(certJson);
                        if (r.valid) { LoadSettings(); RefreshUI(); }
                        else { RefreshUI(); EditorUtility.DisplayDialog("Certificate error", r.error, "OK"); }
                    };
                });
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
            if (!string.IsNullOrEmpty(_settings?.serverUrl)) return _settings.serverUrl;
            string fromService = PackageSigningService.GetServerUrl();
            return !string.IsNullOrEmpty(fromService) ? fromService : "https://api.creators.yucp.club";
        }
    }
}

