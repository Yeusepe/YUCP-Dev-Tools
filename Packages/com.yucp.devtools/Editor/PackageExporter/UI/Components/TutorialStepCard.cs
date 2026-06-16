using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Tokens = YUCP.DevTools.Editor.PackageExporter.CompanionTutorialTokens;

namespace YUCP.DevTools.Editor.PackageExporter.UI.Components
{
    /// <summary>
    /// A self-contained, beautiful UI Toolkit card that edits a single
    /// <see cref="CompanionTutorialStep"/>. Collapsed it reads like a summary row (drag handle,
    /// number badge, title, target/wait chips, validation dot); expanded it exposes friendly
    /// dropdowns for every "magic string" plus an advanced foldout for spotlight / manual rect.
    ///
    /// The card mutates the model directly (with Undo + dirty), so the owning editor only has to
    /// listen for <see cref="OnChanged"/> to refresh summaries. Drag-to-reorder is wired by the
    /// owner via <see cref="DragHandle"/>.
    /// </summary>
    public class TutorialStepCard : VisualElement
    {
        // Friendly labels for the raw token arrays (value ↔ label kept parallel).
        private static readonly string[] MouseActionLabels =
            { "None", "Single click", "Double click", "Right click", "Drag" };
        private static readonly string[] OverlayModeLabels =
            { "Intrusive — dim & spotlight", "Unintrusive — cursor only" };

        private readonly CompanionTutorialStep _step;
        private readonly UnityEngine.Object _owner;

        public CompanionTutorialStep Step => _step;
        public int Index { get; private set; }
        public VisualElement DragHandle { get; private set; }

        /// <summary>Raised after the model is mutated (already recorded for Undo and marked dirty).</summary>
        public event Action OnChanged;
        public event Action OnRequestDelete;
        public event Action OnRequestDuplicate;
        public event Action OnRequestTestFromHere;

        private bool _expanded;
        private bool _advancedExpanded;

        // Header pieces refreshed in place.
        private VisualElement _header;
        private Label _stepNo;
        private Label _titleLabel;
        private VisualElement _chips;
        private Button _chevron;

        // Body is rebuilt wholesale on structural change (category switches, advanced toggle).
        private VisualElement _body;

        public TutorialStepCard(CompanionTutorialStep step, int index, UnityEngine.Object owner, bool expanded)
        {
            _step = step;
            _owner = owner;
            Index = index;
            _expanded = expanded;

            AddToClassList("yucp-ct-card");

            BuildHeader();
            _body = new VisualElement();
            _body.AddToClassList("yucp-ct-card__body");
            Add(_body);

            RebuildBody();
            RefreshHeader();
            ApplyExpandedState();
        }

        public void SetIndex(int index)
        {
            Index = index;
            if (_stepNo != null)
                _stepNo.text = $"STEP {index + 1}";
        }

        // ── Header ─────────────────────────────────────────────────────────────────────────────

        private void BuildHeader()
        {
            _header = new VisualElement();
            _header.AddToClassList("yucp-ct-card__header");

            // Left → right: text grows, then the controls cluster (status dot, drag handle, chevron).
            var main = new VisualElement();
            main.AddToClassList("yucp-ct-header-main");

            var titleLine = new VisualElement();
            titleLine.AddToClassList("yucp-ct-title-line");

            _stepNo = new Label($"STEP {Index + 1}");
            _stepNo.AddToClassList("yucp-ct-stepno");
            titleLine.Add(_stepNo);

            _titleLabel = new Label();
            _titleLabel.AddToClassList("yucp-ct-card__title");
            titleLine.Add(_titleLabel);

            main.Add(titleLine);

            _chips = new VisualElement();
            _chips.AddToClassList("yucp-ct-chips");
            main.Add(_chips);

            _header.Add(main);

            DragHandle = new Label("⋮⋮"); // ⋮⋮ grip
            DragHandle.AddToClassList("yucp-ct-handle");
            DragHandle.tooltip = "Drag to reorder this step";
            _header.Add(DragHandle);

            _chevron = new Button(ToggleExpanded);
            _chevron.AddToClassList("yucp-ct-chevron");
            _header.Add(_chevron);

            // Click anywhere on the header (except handle/chevron) toggles expansion.
            _header.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (evt.target == DragHandle || evt.target == _chevron) return;
                ToggleExpanded();
                evt.StopPropagation();
            });

            Add(_header);
        }

        public void RefreshHeader()
        {
            _stepNo.text = $"STEP {Index + 1}";

            bool emptyTitle = string.IsNullOrWhiteSpace(_step.title);
            _titleLabel.text = emptyTitle ? "Untitled step" : _step.title;
            _titleLabel.EnableInClassList("yucp-ct-card__title--empty", emptyTitle);

            _chips.Clear();
            _chips.Add(MakeChip(TargetSummary(), "yucp-ct-chip--target", "Where the overlay points"));
            _chips.Add(MakeChip(WaitSummary(), "yucp-ct-chip--wait", "When the step advances"));
            if (!string.Equals(NormalizedOverlay(), "intrusive", StringComparison.OrdinalIgnoreCase))
                _chips.Add(MakeChip("cursor-only", "yucp-ct-chip--overlay", "Unintrusive overlay mode"));

            // Validation state tints the step-number label so a collapsed card still flags problems
            // (the full findings show inline when expanded, and in the summary below the list).
            var worst = WorstSeverity();
            EnableInClassList("yucp-ct-card--invalid", worst == CompanionTutorialValidator.Severity.Error);
            EnableInClassList("yucp-ct-card--warning", worst == CompanionTutorialValidator.Severity.Warning);

            _chevron.text = _expanded ? "▼" : "▶";
        }

        private static VisualElement MakeChip(string text, string modifier, string tooltip)
        {
            var chip = new Label(text);
            chip.AddToClassList("yucp-ct-chip");
            chip.AddToClassList(modifier);
            chip.tooltip = tooltip;
            return chip;
        }

        private void ToggleExpanded()
        {
            _expanded = !_expanded;
            ApplyExpandedState();
            RefreshHeader();
        }

        private void ApplyExpandedState()
        {
            _body.style.display = _expanded ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList("yucp-ct-card--expanded", _expanded);
        }

        // ── Body ───────────────────────────────────────────────────────────────────────────────

        private void RebuildBody()
        {
            _body.Clear();

            // Title
            var titleField = new TextField { value = _step.title };
            titleField.AddToClassList("yucp-ct-input");
            titleField.RegisterValueChangedCallback(evt => Mutate("Edit Step Title", () =>
            {
                _step.title = evt.newValue;
            }));
            _body.Add(FieldRow("Title", titleField));

            // Body text (multiline)
            var textField = new TextField { value = _step.text, multiline = true };
            textField.AddToClassList("yucp-ct-input");
            textField.AddToClassList("yucp-ct-textarea");
            textField.RegisterValueChangedCallback(evt => Mutate("Edit Step Text", () =>
            {
                _step.text = evt.newValue;
            }));
            _body.Add(FieldRow("Message", textField, stacked: true));

            // ── Targeting ──
            _body.Add(Subhead("TARGETING"));
            _body.Add(BuildTargetControls());

            // ── Behavior ──
            _body.Add(Subhead("BEHAVIOR"));
            _body.Add(BuildWaitControls());

            var mousePopup = StringPopup(Tokens.MouseActions, MouseActionLabels, _step.mouseAction, "none",
                v => Mutate("Edit Mouse Action", () => _step.mouseAction = v));
            _body.Add(FieldRow("Pointer", mousePopup));

            var overlayPopup = StringPopup(Tokens.OverlayModes, OverlayModeLabels, NormalizedOverlay(), "intrusive",
                v => Mutate("Edit Overlay Mode", () => _step.overlayMode = v));
            _body.Add(FieldRow("Overlay", overlayPopup));

            // Inline validation
            _body.Add(BuildFindings());

            // Advanced
            _body.Add(BuildAdvanced());

            // Footer actions
            _body.Add(BuildFooter());
        }

        private VisualElement BuildTargetControls()
        {
            var wrapper = new VisualElement();

            var category = Tokens.ParseTargetCategory(_step.target, out string selector);

            var catPopup = StringPopup(
                Enumerable.Range(0, Tokens.TargetCategoryLabels.Length).Select(i => i.ToString()).ToArray(),
                Tokens.TargetCategoryLabels,
                ((int)category).ToString(), "0",
                v =>
                {
                    var newCat = (Tokens.TargetCategory)int.Parse(v);
                    Mutate("Edit Target", () =>
                    {
                        _step.target = Tokens.ComposeTarget(newCat, Tokens.DefaultSelectorFor(newCat));
                    });
                    RebuildBody(); // selector control depends on the category
                });
            wrapper.Add(FieldRow("Target", catPopup));

            // Conditional selector control
            switch (category)
            {
                case Tokens.TargetCategory.Center:
                case Tokens.TargetCategory.Gizmo:
                    break;

                case Tokens.TargetCategory.Custom:
                {
                    var raw = new TextField { value = _step.target };
                    raw.AddToClassList("yucp-ct-input");
                    raw.RegisterValueChangedCallback(evt => Mutate("Edit Target", () => _step.target = evt.newValue));
                    wrapper.Add(FieldRow("Raw target", raw));
                    break;
                }

                case Tokens.TargetCategory.EditorWindow:
                {
                    var windows = Tokens.EditorWindowTargets;
                    var titleCase = windows.Select(Capitalize).ToArray();
                    var popup = StringPopup(windows, titleCase,
                        windows.Contains(selector) ? selector : windows[0], windows[0],
                        v => Mutate("Edit Target Window", () => _step.target = v));
                    wrapper.Add(FieldRow("Window", popup));
                    break;
                }

                default:
                {
                    var selField = new TextField { value = selector };
                    selField.AddToClassList("yucp-ct-input");
                    selField.RegisterValueChangedCallback(evt => Mutate("Edit Target Selector", () =>
                    {
                        _step.target = Tokens.ComposeTarget(category, evt.newValue);
                    }));
                    wrapper.Add(FieldRow(Tokens.SelectorLabelFor(category), selField));
                    break;
                }
            }

            return wrapper;
        }

        private VisualElement BuildWaitControls()
        {
            var wrapper = new VisualElement();

            var category = Tokens.ParseWaitCategory(_step.waitFor, out string arg);

            var catPopup = StringPopup(
                Enumerable.Range(0, Tokens.WaitCategoryLabels.Length).Select(i => i.ToString()).ToArray(),
                Tokens.WaitCategoryLabels,
                ((int)category).ToString(), "0",
                v =>
                {
                    var newCat = (Tokens.WaitCategory)int.Parse(v);
                    Mutate("Edit Advance Condition", () =>
                    {
                        _step.waitFor = Tokens.ComposeWait(newCat, Tokens.DefaultWaitArg(newCat));
                    });
                    RebuildBody();
                });
            wrapper.Add(FieldRow("Advance when", catPopup));

            if (category == Tokens.WaitCategory.Custom)
            {
                var raw = new TextField { value = _step.waitFor };
                raw.AddToClassList("yucp-ct-input");
                raw.RegisterValueChangedCallback(evt => Mutate("Edit Advance Condition", () => _step.waitFor = evt.newValue));
                wrapper.Add(FieldRow("Raw condition", raw));
            }
            else if (Tokens.WaitNeedsParam(category))
            {
                var argField = new TextField { value = arg };
                argField.AddToClassList("yucp-ct-input");
                argField.RegisterValueChangedCallback(evt => Mutate("Edit Advance Condition", () =>
                {
                    _step.waitFor = Tokens.ComposeWait(category, evt.newValue);
                }));
                wrapper.Add(FieldRow(Tokens.WaitParamLabel(category), argField));
            }

            return wrapper;
        }

        private VisualElement BuildAdvanced()
        {
            var advanced = new VisualElement();
            advanced.AddToClassList("yucp-ct-advanced");

            var toggle = new VisualElement();
            toggle.AddToClassList("yucp-ct-advanced__toggle");
            var caret = new Label(_advancedExpanded ? "▼" : "▶");
            caret.AddToClassList("yucp-ct-advanced__caret");
            toggle.Add(caret);
            var label = new Label("Advanced — spotlight padding & manual rect");
            label.AddToClassList("yucp-ct-advanced__label");
            toggle.Add(label);
            toggle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                _advancedExpanded = !_advancedExpanded;
                RebuildBody();
                evt.StopPropagation();
            });
            advanced.Add(toggle);

            if (_advancedExpanded)
            {
                var padCaptions = new[] { "L", "T", "R", "B" };
                var padField = Vec4Group(padCaptions, _step.spotlightPadding, v =>
                    Mutate("Edit Spotlight Padding", () => _step.spotlightPadding = v));
                var padRow = FieldRow("Spotlight pad", padField, stacked: true);
                padRow.style.marginTop = 8;
                advanced.Add(padRow);

                var rectCaptions = new[] { "X", "Y", "W", "H" };
                var rectField = Vec4Group(rectCaptions, _step.targetRect, v =>
                    Mutate("Edit Manual Rect", () => _step.targetRect = v));
                advanced.Add(FieldRow("Manual rect", rectField, stacked: true));

                var hint = new Label("Manual rect overrides the target (needs both W & H). Leave at 0 to use the target above.");
                hint.style.fontSize = 10;
                hint.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
                hint.style.whiteSpace = WhiteSpace.Normal;
                hint.style.marginTop = 2;
                advanced.Add(hint);
            }

            return advanced;
        }

        private VisualElement BuildFindings()
        {
            var container = new VisualElement();
            container.AddToClassList("yucp-ct-findings");

            foreach (var finding in FindingsForStep())
            {
                var box = new VisualElement();
                box.AddToClassList("yucp-ct-finding");
                string sev;
                string glyph;
                switch (finding.Severity)
                {
                    case CompanionTutorialValidator.Severity.Error: sev = "error"; glyph = "✕"; break;
                    case CompanionTutorialValidator.Severity.Warning: sev = "warning"; glyph = "⚠"; break;
                    default: sev = "info"; glyph = "ℹ"; break;
                }
                box.AddToClassList("yucp-ct-finding--" + sev);

                var icon = new Label(glyph);
                icon.AddToClassList("yucp-ct-finding__icon");
                box.Add(icon);

                var text = new Label(finding.Message);
                text.AddToClassList("yucp-ct-finding__text");
                box.Add(text);

                container.Add(box);
            }

            return container;
        }

        private VisualElement BuildFooter()
        {
            var footer = new VisualElement();
            footer.AddToClassList("yucp-ct-card__footer");

            var test = new Button(() => OnRequestTestFromHere?.Invoke()) { text = "Test from here" };
            test.AddToClassList("yucp-ct-foot-btn");
            test.AddToClassList("yucp-ct-foot-btn--primary");
            test.tooltip = "Preview the tutorial starting at this step";
            footer.Add(test);

            var dup = new Button(() => OnRequestDuplicate?.Invoke()) { text = "Duplicate" };
            dup.AddToClassList("yucp-ct-foot-btn");
            footer.Add(dup);

            var del = new Button(() => OnRequestDelete?.Invoke()) { text = "Delete" };
            del.AddToClassList("yucp-ct-foot-btn");
            del.AddToClassList("yucp-ct-foot-btn--danger");
            footer.Add(del);

            return footer;
        }

        // ── Helpers ────────────────────────────────────────────────────────────────────────────

        private void Mutate(string undoLabel, Action apply)
        {
            if (_owner != null)
                Undo.RecordObject(_owner, undoLabel);
            apply();
            if (_owner != null)
                EditorUtility.SetDirty(_owner);
            RefreshHeader();
            OnChanged?.Invoke();
        }

        private static VisualElement FieldRow(string label, VisualElement control, bool stacked = false)
        {
            var row = new VisualElement();
            row.AddToClassList("yucp-ct-field-row");
            if (stacked) row.AddToClassList("yucp-ct-field-row--stacked");

            var lbl = new Label(label);
            lbl.AddToClassList("yucp-ct-field-label");
            row.Add(lbl);

            control.AddToClassList("yucp-ct-field-control");
            row.Add(control);
            return row;
        }

        private static Label Subhead(string text)
        {
            var l = new Label(text);
            l.AddToClassList("yucp-ct-subhead");
            return l;
        }

        /// <summary>A PopupField whose displayed labels differ from the stored token values.</summary>
        private static PopupField<string> StringPopup(string[] values, string[] labels, string current, string fallback, Action<string> onSet)
        {
            var valueList = values.ToList();
            int idx = Array.IndexOf(values, current);
            if (idx < 0) idx = Math.Max(0, Array.IndexOf(values, fallback));
            if (idx < 0) idx = 0;

            var popup = new PopupField<string>(valueList, idx,
                formatSelectedValueCallback: v => LabelFor(values, labels, v),
                formatListItemCallback: v => LabelFor(values, labels, v));
            popup.AddToClassList("yucp-ct-popup");
            popup.RegisterValueChangedCallback(evt => onSet(evt.newValue));
            return popup;
        }

        private static string LabelFor(string[] values, string[] labels, string value)
        {
            int i = Array.IndexOf(values, value);
            return (i >= 0 && i < labels.Length) ? labels[i] : value;
        }

        private VisualElement Vec4Group(string[] captions, Vector4 value, Action<Vector4> onSet)
        {
            var group = new VisualElement();
            group.AddToClassList("yucp-ct-vec");

            var current = new[] { value };
            float[] comps = { value.x, value.y, value.z, value.w };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var cell = new VisualElement();
                cell.AddToClassList("yucp-ct-vec__cell");
                cell.style.marginRight = i < 3 ? 6 : 0;

                var cap = new Label(captions[i]);
                cap.AddToClassList("yucp-ct-vec__caption");
                cell.Add(cap);

                var field = new FloatField { value = comps[i] };
                field.AddToClassList("yucp-ct-vec__field");
                field.RegisterValueChangedCallback(evt =>
                {
                    var v = current[0];
                    v[idx] = evt.newValue;
                    current[0] = v;
                    onSet(v);
                });
                cell.Add(field);
                group.Add(cell);
            }

            return group;
        }

        private string NormalizedOverlay()
        {
            return string.IsNullOrWhiteSpace(_step.overlayMode) ? "intrusive" : _step.overlayMode;
        }

        private string TargetSummary()
        {
            var cat = Tokens.ParseTargetCategory(_step.target, out string selector);
            switch (cat)
            {
                case Tokens.TargetCategory.Center: return "Centered card";
                case Tokens.TargetCategory.Gizmo: return "Transform gizmo";
                case Tokens.TargetCategory.EditorWindow: return Capitalize(selector) + " window";
                case Tokens.TargetCategory.Custom: return _step.target;
                default:
                    return string.IsNullOrEmpty(selector)
                        ? Tokens.TargetCategoryLabels[(int)cat]
                        : selector;
            }
        }

        private string WaitSummary()
        {
            var cat = Tokens.ParseWaitCategory(_step.waitFor, out string arg);
            switch (cat)
            {
                case Tokens.WaitCategory.Manual: return "Manual";
                case Tokens.WaitCategory.Selection: return "On selection";
                case Tokens.WaitCategory.Delay: return $"Delay {arg}s";
                case Tokens.WaitCategory.AssetExists: return "Asset exists";
                case Tokens.WaitCategory.PackageInstalled: return "Package installed";
                case Tokens.WaitCategory.ComponentExists: return "Component exists";
                case Tokens.WaitCategory.TransformMoved: return "Transform moves";
                default: return _step.waitFor;
            }
        }

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private List<CompanionTutorialValidator.Finding> FindingsForStep()
        {
            var def = new CompanionTutorialDefinition
            {
                enabled = true,
                steps = new List<CompanionTutorialStep> { _step }
            };
            return CompanionTutorialValidator.Validate(def);
        }

        private CompanionTutorialValidator.Severity WorstSeverity()
        {
            var worst = (CompanionTutorialValidator.Severity)(-1);
            foreach (var f in FindingsForStep())
                if ((int)f.Severity > (int)worst)
                    worst = f.Severity;
            return worst;
        }
    }
}
