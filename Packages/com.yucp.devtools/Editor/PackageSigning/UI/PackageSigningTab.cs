using System;
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

            // Package server status badge (async loaded)
            var badgeSlot = new VisualElement();
            badgeSlot.style.alignSelf = Align.Center;
            if (!string.IsNullOrEmpty(_profile.packageId))
                LoadPackageStatusBadge(badgeSlot);
            pkgRow.Add(badgeSlot);

            body.Add(pkgRow);

            // Product ID fields
            body.Add(BuildProductField("Gumroad", _profile.gumroadProductId ?? "",
                v => { if (_profile != null) { Undo.RecordObject(_profile, "Set Gumroad ID"); _profile.gumroadProductId = v; EditorUtility.SetDirty(_profile); } }));
            body.Add(BuildProductField("Jinxxy",  _profile.jinxxyProductId  ?? "",
                v => { if (_profile != null) { Undo.RecordObject(_profile, "Set Jinxxy ID");  _profile.jinxxyProductId  = v; EditorUtility.SetDirty(_profile); } }));

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

        private VisualElement BuildProductField(string label, string value, Action<string> onChange)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 8;

            var lbl = MakeLabel(label, 10, TextMute, mb: 0);
            lbl.style.width     = 60;
            lbl.style.flexShrink = 0;
            row.Add(lbl);

            var field = new TextField { value = value };
            field.style.flexGrow  = 1;
            field.style.fontSize  = 11;
            field.style.height    = 24;
            ApplyInputStyle(field);
            field.RegisterValueChangedCallback(e => onChange?.Invoke(e.newValue));
            row.Add(field);
            return row;
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
            var l = new Label(text);
            l.style.fontSize              = size;
            l.style.color                 = color;
            l.style.marginBottom          = mb;
            l.style.unityTextAlign        = align;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (letterSpacing > 0f) l.style.letterSpacing = letterSpacing;
            if (wrap) l.style.whiteSpace  = WhiteSpace.Normal;
            return l;
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

