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
        private static readonly Color Surface      = new Color(0.086f, 0.086f, 0.086f);  // #161616
        private static readonly Color SurfaceRaise = new Color(0.118f, 0.118f, 0.118f); // #1E1E1E
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

            // Subtle teal glow band at the very top
            var glow = new VisualElement();
            glow.style.height = 80;
            glow.style.backgroundColor = TealGlow;
            glow.style.borderTopLeftRadius  = 10;
            glow.style.borderTopRightRadius = 10;
            card.Add(glow);

            var body = MakePad(24, 28, 24, 24);
            body.style.marginTop = -4; // overlap into glow
            card.Add(body);

            // Eyebrow
            body.Add(MakeLabel("CREATOR SIGNING", 9, Teal, bold: true, letterSpacing: 2f, mb: 14));

            // Headline
            body.Add(MakeLabel("Sign packages with\nyour creator identity", 17, TextPri, bold: true, mb: 6, wrap: true));

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

            // White pill — loads image from resources
            var btn = new VisualElement();
            btn.style.backgroundColor       = Color.white;
            btn.style.borderTopLeftRadius    = 22;
            btn.style.borderTopRightRadius   = 22;
            btn.style.borderBottomLeftRadius = 22;
            btn.style.borderBottomRightRadius = 22;
            btn.style.flexDirection  = FlexDirection.Row;
            btn.style.alignItems     = Align.Center;
            btn.style.justifyContent = Justify.Center;
            btn.style.paddingLeft    = 28;
            btn.style.paddingRight   = 28;
            btn.style.paddingTop     = 14;
            btn.style.paddingBottom  = 14;
            btn.style.alignSelf      = Align.Center;

            const string imgPath = "Packages/com.yucp.devtools/Editor/PackageSigning/Resources/SignInAsCreatorInnerElements.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(imgPath);
            if (tex != null)
            {
                int w = tex.width, h = tex.height;
                if (AssetImporter.GetAtPath(imgPath) is TextureImporter imp)
                    imp.GetSourceTextureWidthAndHeight(out w, out h);

                var wrap = new VisualElement();
                wrap.style.width     = w * 0.65f;
                wrap.style.height    = h * 0.65f;
                wrap.style.flexShrink = 0;
                wrap.style.overflow  = Overflow.Hidden;
                var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit };
                img.style.width    = Length.Percent(100);
                img.style.height   = Length.Percent(100);
                img.style.position = Position.Absolute;
                img.style.left = img.style.top = img.style.right = img.style.bottom = 0;
                wrap.Add(img);
                btn.Add(wrap);
            }
            else
            {
                btn.Add(MakeLabel("Sign in as Creator", 13, new Color(0.1f, 0.1f, 0.1f), bold: true));
            }

            btn.RegisterCallback<MouseEnterEvent>(_ => btn.style.opacity = 0.85f);
            btn.RegisterCallback<MouseLeaveEvent>(_ => btn.style.opacity = 1.0f);
            btn.RegisterCallback<ClickEvent>(_ =>
            {
                if (_isSigningIn) return;
                _isSigningIn = true;
                RefreshUI();
#pragma warning disable CS4014
                YucpOAuthService.SignInAsync(GetServerUrl(),
                    onSuccess: () => EditorApplication.delayCall += () => { _isSigningIn = false; LoadSettings(); RefreshUI(); },
                    onError:   e  => EditorApplication.delayCall += () => { _isSigningIn = false; RefreshUI(); EditorUtility.DisplayDialog("Sign-in failed", e, "OK"); });
#pragma warning restore CS4014
            });
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
            c.style.backgroundColor          = Surface;
            c.style.borderTopLeftRadius      = 10;
            c.style.borderTopRightRadius     = 10;
            c.style.borderBottomLeftRadius   = 10;
            c.style.borderBottomRightRadius  = 10;
            c.style.borderTopWidth = c.style.borderBottomWidth =
                c.style.borderLeftWidth = c.style.borderRightWidth = 1;
            c.style.borderTopColor = c.style.borderBottomColor =
                c.style.borderLeftColor = c.style.borderRightColor = Border;
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

            // Try to restore an existing cert first (covers: new project, same machine).
            // Only issues a new cert if no active cert exists for this machine key.
            _ = service.RestoreCertificateAsync(accessToken, devPublicKey)
                .ContinueWith(restoreTask =>
                {
                    string restoredJson = restoreTask.Result;
                    if (!string.IsNullOrEmpty(restoredJson))
                    {
                        // Cert found on server — import locally without issuing a new one
                        EditorApplication.delayCall += () =>
                        {
                            _isRequestingCert = false;
                            var result = CertificateManager.ImportAndVerifyFromJson(restoredJson);
                            if (result.valid)
                            {
                                LoadSettings();
                                RefreshUI();
                                EditorUtility.DisplayDialog("Certificate Restored",
                                    $"Existing certificate restored for this machine.\n\nPublisher: {result.publisherName}\nExpires: {result.expiresAt:MMM dd, yyyy}",
                                    "OK");
                            }
                            else
                            {
                                // Cert on server but invalid locally — fall through to issue new
                                RequestNewCertificate(service, accessToken, devPublicKey, publisherName);
                            }
                        };
                        return;
                    }

                    // No existing cert — issue a new one
                    EditorApplication.delayCall += () =>
                        RequestNewCertificate(service, accessToken, devPublicKey, publisherName);
                });
        }

        // ─── Utilities ─────────────────────────────────────────────────────────────

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
