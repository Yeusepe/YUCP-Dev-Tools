using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using CompanionTutorialRunner = YUCP.CompanionTutorial.Generated.Source.CompanionTutorialRunner;

namespace YUCP.DevTools.Editor.PackageExporter.UI.Components
{
    /// <summary>
    /// Pure UI Toolkit editor for a <see cref="CompanionTutorialDefinition"/>: an enable toggle,
    /// the tutorial title, a Preview / Demo / Stop action bar, and a drag-to-reorder list of
    /// <see cref="TutorialStepCard"/>s with add / duplicate / delete / test-from-here, plus a live
    /// validation summary. Replaces the old IMGUI ReorderableList so the section matches the rest of
    /// the exporter UI.
    /// </summary>
    public class CompanionTutorialEditor : VisualElement
    {
        private const string UssPath =
            "Packages/com.yucp.devtools/Editor/PackageExporter/UI/Components/CompanionTutorial.uss";

        private readonly CompanionTutorialDefinition _def;
        private readonly UnityEngine.Object _owner;

        private readonly VisualElement _content;
        private readonly Label _countLabel;
        private readonly VisualElement _stepsContainer;
        private readonly VisualElement _dropLine;
        private readonly VisualElement _summary;
        private Button _previewButton;

        private readonly List<TutorialStepCard> _cards = new List<TutorialStepCard>();

        // Drag-reorder state.
        private int _dragIndex = -1;
        private int _dropIndex = -1;

        public CompanionTutorialEditor(CompanionTutorialDefinition def, UnityEngine.Object owner)
        {
            _def = def;
            _owner = owner;
            AddToClassList("yucp-ct-editor");

            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (sheet != null)
                styleSheets.Add(sheet);

            // ── Enable toggle + step count toolbar ──
            var toolbar = new VisualElement();
            toolbar.AddToClassList("yucp-ct-toolbar");

            var left = new VisualElement();
            left.AddToClassList("yucp-ct-toolbar__left");
            var enableToggle = new Toggle("Enable installation tutorial") { value = _def.enabled };
            enableToggle.AddToClassList("yucp-toggle");
            enableToggle.tooltip = "Auto-plays once after a buyer imports the package (Windows-only overlay).";
            enableToggle.RegisterValueChangedCallback(evt =>
            {
                Mutate("Toggle Companion Tutorial", () => _def.enabled = evt.newValue);
                ApplyEnabledState();
                RefreshSummary();
                RefreshPreviewButton();
            });
            left.Add(enableToggle);
            toolbar.Add(left);

            _countLabel = new Label();
            _countLabel.AddToClassList("yucp-ct-count");
            toolbar.Add(_countLabel);
            Add(toolbar);

            // ── Dimmable content ──
            _content = new VisualElement();
            Add(_content);

            // Tutorial title
            var titleRow = new VisualElement();
            titleRow.AddToClassList("yucp-ct-title-row");
            var titleLabel = new Label("Tutorial title");
            titleLabel.AddToClassList("yucp-ct-field-label");
            titleLabel.style.width = StyleKeyword.Auto;
            titleLabel.style.marginBottom = 4;
            titleRow.Add(titleLabel);
            var titleField = new TextField { value = _def.title };
            titleField.AddToClassList("yucp-input");
            titleField.RegisterValueChangedCallback(evt => Mutate("Edit Tutorial Title", () => _def.title = evt.newValue));
            titleRow.Add(titleField);
            _content.Add(titleRow);

            // Action bar
            _content.Add(BuildActionBar());

            // Steps
            _stepsContainer = new VisualElement();
            _stepsContainer.AddToClassList("yucp-ct-steps");
            _content.Add(_stepsContainer);

            _dropLine = new VisualElement();
            _dropLine.AddToClassList("yucp-ct-drop-line");

            // Add-step button
            var addButton = new Button(AddStep) { text = "＋  Add step" };
            addButton.AddToClassList("yucp-ct-add");
            _content.Add(addButton);

            // Validation summary
            _summary = new VisualElement();
            _content.Add(_summary);

            RebuildSteps();
            ApplyEnabledState();
            RefreshSummary();
        }

        // ── Action bar ───────────────────────────────────────────────────────────────────────────

        private VisualElement BuildActionBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("yucp-ct-actionbar");

            _previewButton = new Button(() =>
            {
                if (_def != null)
                    CompanionTutorialRunner.QueueRunFromJson(JsonUtility.ToJson(_def));
            })
            { text = "▶  Preview" };
            _previewButton.AddToClassList("yucp-button");
            bar.Add(_previewButton);

            var demo = new Button(CompanionTutorialRunner.StartDemo) { text = "Run Demo" };
            demo.AddToClassList("yucp-button");
            bar.Add(demo);

            var stop = new Button(CompanionTutorialRunner.Stop) { text = "Stop" };
            stop.AddToClassList("yucp-button");
            bar.Add(stop);

            // Even gaps between the three buttons, ends flush with the row.
            const float gap = 10f;
            _previewButton.style.marginLeft = 0;
            _previewButton.style.marginRight = gap;
            demo.style.marginLeft = 0;
            demo.style.marginRight = gap;
            stop.style.marginLeft = 0;
            stop.style.marginRight = 0;

            RefreshPreviewButton();
            return bar;
        }

        private void RefreshPreviewButton()
        {
            _previewButton?.SetEnabled(_def != null && _def.enabled && _def.steps != null && _def.steps.Count > 0);
        }

        // ── Steps list ───────────────────────────────────────────────────────────────────────────

        private void RebuildSteps(int expandIndex = -1)
        {
            _stepsContainer.Clear();
            _cards.Clear();

            int count = _def.steps?.Count ?? 0;
            _countLabel.text = count == 1 ? "1 step" : $"{count} steps";

            if (count == 0)
            {
                _stepsContainer.Add(BuildEmptyState());
                RefreshPreviewButton();
                return;
            }

            for (int i = 0; i < count; i++)
            {
                bool expanded = i == expandIndex || (expandIndex < 0 && count <= 1);
                var card = new TutorialStepCard(_def.steps[i], i, _owner, expanded);
                card.OnChanged += () => { RefreshSummary(); RefreshPreviewButton(); };
                card.OnRequestDelete += () => DeleteStep(card.Step);
                card.OnRequestDuplicate += () => DuplicateStep(card.Step);
                card.OnRequestTestFromHere += () => TestFromHere(card.Step);
                WireDrag(card);
                _cards.Add(card);
                _stepsContainer.Add(card);
            }

            _stepsContainer.Add(_dropLine);
            RefreshPreviewButton();
        }

        private VisualElement BuildEmptyState()
        {
            var empty = new VisualElement();
            empty.AddToClassList("yucp-ct-empty");

            var title = new Label("No steps yet");
            title.AddToClassList("yucp-ct-empty__title");
            empty.Add(title);

            var desc = new Label("Add steps to walk buyers through installing your package. Each step can point the overlay at a Unity window, toolbar control, menu, or scene object.");
            desc.AddToClassList("yucp-ct-empty__desc");
            empty.Add(desc);

            var add = new Button(AddStep) { text = "＋  Add first step" };
            add.AddToClassList("yucp-ct-add");
            add.style.paddingLeft = 18;
            add.style.paddingRight = 18;
            empty.Add(add);

            return empty;
        }

        private void AddStep()
        {
            Mutate("Add Tutorial Step", () =>
            {
                if (_def.steps == null) _def.steps = new List<CompanionTutorialStep>();
                _def.steps.Add(new CompanionTutorialStep
                {
                    id = Guid.NewGuid().ToString("N"),
                    title = $"Step {_def.steps.Count + 1}",
                });
            });
            // Expand the freshly added card so the author can edit it immediately.
            RebuildSteps(expandIndex: _def.steps.Count - 1);
            RefreshSummary();
        }

        private void DeleteStep(CompanionTutorialStep step)
        {
            int idx = _def.steps.IndexOf(step);
            if (idx < 0) return;
            Mutate("Delete Tutorial Step", () => _def.steps.RemoveAt(idx));
            RebuildSteps();
            RefreshSummary();
        }

        private void DuplicateStep(CompanionTutorialStep step)
        {
            int idx = _def.steps.IndexOf(step);
            if (idx < 0) return;
            Mutate("Duplicate Tutorial Step", () =>
            {
                var copy = new CompanionTutorialStep
                {
                    id = Guid.NewGuid().ToString("N"),
                    title = step.title,
                    text = step.text,
                    target = step.target,
                    targetRect = step.targetRect,
                    waitFor = step.waitFor,
                    mouseAction = step.mouseAction,
                    overlayMode = step.overlayMode,
                    spotlightPadding = step.spotlightPadding,
                };
                _def.steps.Insert(idx + 1, copy);
            });
            RebuildSteps();
            RefreshSummary();
        }

        private void TestFromHere(CompanionTutorialStep step)
        {
            int idx = _def.steps.IndexOf(step);
            if (idx < 0) idx = 0;
            if (_def != null)
                CompanionTutorialRunner.QueueRunFromJson(JsonUtility.ToJson(_def), idx);
        }

        // ── Drag to reorder ──────────────────────────────────────────────────────────────────────

        private void WireDrag(TutorialStepCard card)
        {
            var handle = card.DragHandle;
            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || !_content.enabledSelf) return;
                _dragIndex = _cards.IndexOf(card);
                _dropIndex = _dragIndex;
                card.AddToClassList("yucp-ct-card--dragging");
                handle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_dragIndex < 0 || !handle.HasPointerCapture(evt.pointerId)) return;
                UpdateDrop(evt.position);
                evt.StopPropagation();
            });

            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_dragIndex < 0) return;
                handle.ReleasePointer(evt.pointerId);
                card.RemoveFromClassList("yucp-ct-card--dragging");
                _dropLine.style.display = DisplayStyle.None;
                CommitReorder();
                evt.StopPropagation();
            });
        }

        private void UpdateDrop(Vector2 worldPos)
        {
            float localY = _stepsContainer.WorldToLocal(worldPos).y;

            int target = _cards.Count;
            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                float mid = c.layout.yMin + c.layout.height / 2f;
                if (localY < mid) { target = i; break; }
            }
            _dropIndex = target;

            float lineY = target < _cards.Count
                ? _cards[target].layout.yMin - 1f
                : _cards[_cards.Count - 1].layout.yMax - 1f;

            _dropLine.style.top = lineY;
            _dropLine.style.display = DisplayStyle.Flex;
            _dropLine.BringToFront();
        }

        private void CommitReorder()
        {
            int from = _dragIndex;
            int to = _dropIndex;
            _dragIndex = -1;
            _dropIndex = -1;

            if (from < 0 || from >= _def.steps.Count) return;
            if (to > from) to--; // removing 'from' shifts everything after it left by one
            to = Mathf.Clamp(to, 0, _def.steps.Count - 1);
            if (to == from)
            {
                RebuildSteps(); // nothing moved, but layout/badges are clean
                return;
            }

            Mutate("Reorder Tutorial Steps", () =>
            {
                var step = _def.steps[from];
                _def.steps.RemoveAt(from);
                _def.steps.Insert(to, step);
            });
            RebuildSteps();
            RefreshSummary();
        }

        // ── Validation summary ───────────────────────────────────────────────────────────────────

        private void RefreshSummary()
        {
            _summary.Clear();
            if (_def == null || !_def.enabled)
                return;

            var findings = CompanionTutorialValidator.Validate(_def);
            int errors = 0, warnings = 0;
            foreach (var f in findings)
            {
                if (f.Severity == CompanionTutorialValidator.Severity.Error) errors++;
                else if (f.Severity == CompanionTutorialValidator.Severity.Warning) warnings++;
            }

            string message;
            string cls;
            if (errors > 0)
            {
                message = $"{errors} error{(errors == 1 ? "" : "s")}" +
                          (warnings > 0 ? $" and {warnings} warning{(warnings == 1 ? "" : "s")}" : "") +
                          " — see the highlighted steps above.";
                cls = "yucp-validation-error";
            }
            else if (warnings > 0)
            {
                message = $"{warnings} warning{(warnings == 1 ? "" : "s")} — the tutorial will still run.";
                cls = "yucp-validation-warning";
            }
            else
            {
                message = "Tutorial looks good — ready to ship.";
                cls = "yucp-validation-success";
            }

            var box = new VisualElement();
            box.AddToClassList(cls);
            box.style.marginTop = 12;
            var text = new Label(message);
            text.AddToClassList(cls + "-text");
            text.style.whiteSpace = WhiteSpace.Normal;
            box.Add(text);
            _summary.Add(box);
        }

        // ── Enabled / dimming ────────────────────────────────────────────────────────────────────

        private void ApplyEnabledState()
        {
            _content.SetEnabled(_def.enabled);
            _content.EnableInClassList("yucp-ct-body-disabled", !_def.enabled);
        }

        // ── Undo helper ──────────────────────────────────────────────────────────────────────────

        private void Mutate(string undoLabel, Action apply)
        {
            if (_owner != null)
                Undo.RecordObject(_owner, undoLabel);
            apply();
            if (_owner != null)
                EditorUtility.SetDirty(_owner);
        }
    }
}
