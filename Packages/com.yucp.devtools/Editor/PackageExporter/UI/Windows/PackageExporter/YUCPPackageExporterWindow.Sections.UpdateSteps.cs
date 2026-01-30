using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.PackageExporter
{
    public partial class YUCPPackageExporterWindow
    {
        private VisualElement CreateUpdateStepsSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.name = "section-update-steps";

            var header = CreateCollapsibleHeader("Custom Update Steps",
                () => showUpdateSteps,
                (value) => { showUpdateSteps = value; },
                () => UpdateProfileDetails());
            section.Add(header);

            if (!showUpdateSteps)
            {
                return section;
            }

            // Warning Box
            var warningBox = new VisualElement();
            warningBox.AddToClassList("yucp-help-box");
            var warningText = new Label("Update steps run in a specific order defined below. " +
                                        "Destructive actions require explicit confirmation.");
            warningText.AddToClassList("yucp-help-box-text");
            warningBox.Add(warningText);
            section.Add(warningBox);

            // Enable Toggle Row
            var enableRow = new VisualElement();
            enableRow.style.flexDirection = FlexDirection.Row;
            enableRow.style.alignItems = Align.Center;
            enableRow.style.marginTop = 8;
            enableRow.style.marginBottom = 8;
            
            var enableToggle = new Toggle();
            enableToggle.value = profile.updateSteps.enabled;
            enableToggle.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(profile, "Toggle Custom Update Steps");
                profile.updateSteps.enabled = evt.newValue;
                EditorUtility.SetDirty(profile);
                UpdateProfileDetails();
            });
            enableRow.Add(enableToggle);
            
            var enableLabel = new Label("Enable custom update steps");
            enableLabel.style.marginLeft = 4;
            enableLabel.RegisterCallback<ClickEvent>(evt => enableToggle.value = !enableToggle.value);
            enableRow.Add(enableLabel);
            section.Add(enableRow);

            if (!profile.updateSteps.enabled)
            {
                var disabledNote = new Label("Custom update steps are disabled for this profile.");
                disabledNote.style.opacity = 0.6f;
                disabledNote.style.fontSize = 12;
                disabledNote.style.paddingLeft = 2;
                section.Add(disabledNote);
                return section;
            }

            // Ensure list exists
            if (profile.updateSteps.steps == null)
            {
                profile.updateSteps.steps = new List<UpdateStep>();
            }

            // --- Phase Groups ---
            
            // 1. Pre-Import
            RenderPhaseGroup(section, UpdatePhase.PreImport, profile, "1. Pre-Import Steps", 
                "Runs BEFORE the package is imported. Useful for backups or cleaning up old files.");

            // 2. Manual
            RenderPhaseGroup(section, UpdatePhase.Manual, profile, "2. Manual Steps", 
                "Runs AFTER Pre-Import but requires user interaction. Use for prompts, manual checks, or instructions.");

            // 3. Post-Import
            RenderPhaseGroup(section, UpdatePhase.PostImport, profile, "3. Post-Import Steps", 
                "Runs AFTER the package is imported. Use for fixing references, moving new assets, or validation.");

            return section;
        }

        private void RenderPhaseGroup(VisualElement container, UpdatePhase phase, ExportProfile profile, string title, string description)
        {
            var groupContainer = new VisualElement();
            groupContainer.style.marginTop = 12;
            groupContainer.style.marginBottom = 12;
            groupContainer.style.backgroundColor = new Color(0, 0, 0, 0.1f);
            groupContainer.style.borderTopLeftRadius = 8;
            groupContainer.style.borderTopRightRadius = 8;
            groupContainer.style.borderBottomLeftRadius = 8;
            groupContainer.style.borderBottomRightRadius = 8;
            groupContainer.style.paddingTop = 8;
            groupContainer.style.paddingBottom = 8;
            groupContainer.style.paddingLeft = 8;
            groupContainer.style.paddingRight = 8;
            groupContainer.style.borderTopWidth = 1;
            groupContainer.style.borderBottomWidth = 1;
            groupContainer.style.borderLeftWidth = 1;
            groupContainer.style.borderRightWidth = 1;
            
            var borderColor = new Color(1, 1, 1, 0.1f);
            groupContainer.style.borderTopColor = borderColor;
            groupContainer.style.borderBottomColor = borderColor;
            groupContainer.style.borderLeftColor = borderColor;
            groupContainer.style.borderRightColor = borderColor;

            var titleLabel = new Label(title);
            titleLabel.style.fontSize = 12;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.marginBottom = 2;
            groupContainer.Add(titleLabel);

            var descLabel = new Label(description);
            descLabel.style.fontSize = 10;
            descLabel.style.opacity = 0.6f;
            descLabel.style.whiteSpace = WhiteSpace.Normal;
            descLabel.style.marginBottom = 8;
            groupContainer.Add(descLabel);

            var stepsContainer = new VisualElement();
            
            // Filter steps for this phase
            // We need to keep track of the ORIGINAL index in the main list to perform operations
            var stepsInPhase = new List<(UpdateStep step, int originalIndex)>();
            for (int i = 0; i < profile.updateSteps.steps.Count; i++)
            {
                if (profile.updateSteps.steps[i].phase == phase)
                {
                    stepsInPhase.Add((profile.updateSteps.steps[i], i));
                }
            }

            foreach (var item in stepsInPhase)
            {
                stepsContainer.Add(CreateUpdateStepItem(profile, item.originalIndex, item.step));
            }
            groupContainer.Add(stepsContainer);

            // Add Button for this Phase
            var addBtn = new Button(() =>
            {
                ShowAddActionMenu(profile, phase);
            }) { text = "+ Add Action..." };
            addBtn.AddToClassList("yucp-button");
            addBtn.style.alignSelf = Align.FlexStart;
            addBtn.style.marginTop = 4;
            
            groupContainer.Add(addBtn);

            container.Add(groupContainer);
        }

        private void ShowAddActionMenu(ExportProfile profile, UpdatePhase phase)
        {
            var menu = new GenericMenu();

            void Add(string path, UpdateStepType type)
            {
                menu.AddItem(new GUIContent(path), false, () => {
                   Undo.RecordObject(profile, $"Add {type} Step");
                   var newStep = new UpdateStep { 
                       name = ObjectNames.NicifyVariableName(type.ToString()), // Default name
                       phase = phase, 
                       type = type 
                   };
                   // Pre-fill lists so UI isn't empty
                   if (type == UpdateStepType.MoveAssets || type == UpdateStepType.CopyAssets)
                   {
                       newStep.paths = new List<string> { "", "" };
                   }
                   else if (type == UpdateStepType.DeleteAssets)
                   {
                       newStep.paths = new List<string> { "" };
                   }
                   
                   profile.updateSteps.steps.Add(newStep);
                   EditorUtility.SetDirty(profile);
                   UpdateProfileDetails();
                });
            }

            Add("File Operations/Move Assets", UpdateStepType.MoveAssets);
            Add("File Operations/Copy Assets", UpdateStepType.CopyAssets);
            Add("File Operations/Delete Assets", UpdateStepType.DeleteAssets);
            Add("File Operations/Delete Folder", UpdateStepType.DeleteFolder);
            Add("File Operations/Create Folder", UpdateStepType.CreateFolder);
            
            menu.AddSeparator("");
            Add("Validation/Validate Asset Exists", UpdateStepType.ValidatePresence);
            Add("Validation/Validate Package Version", UpdateStepType.ValidateVersion);
            
            menu.AddSeparator("");
            Add("User Interaction/Show Message", UpdateStepType.PromptUser);
            Add("User Interaction/Wait For User", UpdateStepType.WaitForUser);
            
            menu.AddSeparator("");
            Add("Scene/Open Scene", UpdateStepType.OpenScene);
            Add("Scene/Remove Objects from Scene", UpdateStepType.RemoveSceneObjects);

            menu.ShowAsContext();
        }

        private VisualElement CreateUpdateStepItem(ExportProfile profile, int index, UpdateStep step)
        {
            if (step == null) return new VisualElement(); // Safety
            if (string.IsNullOrEmpty(step.id))
            {
                step.id = Guid.NewGuid().ToString("N");
                EditorUtility.SetDirty(profile);
            }

            var container = new VisualElement();
            container.AddToClassList("yucp-folder-item");
            container.style.flexDirection = FlexDirection.Column;
            container.style.alignItems = Align.Stretch;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;
            container.style.paddingTop = 8;
            container.style.paddingBottom = 8;
            container.style.marginBottom = 4;
            container.style.height = StyleKeyword.Auto;  // Always size to content
            container.style.flexShrink = 0;               // Never collapse
            
            // --- Header Row (Checkbox, Name, Type Label, Actions) ---
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 4;
            headerRow.style.flexWrap = Wrap.Wrap; 
            headerRow.AddToClassList("yucp-step-header"); // For CSS responsive targeting

            var enabledToggle = new Toggle();
            enabledToggle.value = step.enabled;
            enabledToggle.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(profile, "Toggle Step");
                step.enabled = evt.newValue;
                EditorUtility.SetDirty(profile);
            });
            headerRow.Add(enabledToggle);

            // Action Icon/Badge
            // Action Icon/Badge
            var typeBadge = new Label(ObjectNames.NicifyVariableName(step.type.ToString()));
            typeBadge.style.fontSize = 11;
            // ... styles ...
            typeBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            typeBadge.style.color = new Color(1f, 1f, 1f, 0.9f);
            typeBadge.style.backgroundColor = new Color(1f, 1f, 1f, 0.1f);
            typeBadge.style.paddingTop = 2;
            typeBadge.style.paddingBottom = 2;
            typeBadge.style.paddingLeft = 6;
            typeBadge.style.paddingRight = 6;
            typeBadge.style.borderTopLeftRadius = 4;
            typeBadge.style.borderTopRightRadius = 4;
            typeBadge.style.borderBottomLeftRadius = 4;
            typeBadge.style.borderBottomRightRadius = 4;
            typeBadge.style.marginRight = 8;
            typeBadge.style.marginLeft = 8; 
            typeBadge.style.marginTop = 2; // Spacing for wrap
            typeBadge.style.marginBottom = 2;
            typeBadge.style.flexShrink = 0; 
            headerRow.Add(typeBadge);

            // Actions: Up (Moved before name for responsive wrapping support)
            var actionRow = new VisualElement();
            actionRow.AddToClassList("yucp-step-actions"); 
            actionRow.style.flexDirection = FlexDirection.Row;
            actionRow.style.flexShrink = 0; 
            
            var upBtn = new Button(() => { MoveStepSmart(profile, index, -1); }) { text = "↑" };
            upBtn.AddToClassList("yucp-button");
            upBtn.style.width = 24; 
            upBtn.style.paddingLeft = 0;
            upBtn.style.paddingRight = 0;
            actionRow.Add(upBtn);

            var downBtn = new Button(() => { MoveStepSmart(profile, index, 1); }) { text = "↓" };
            downBtn.AddToClassList("yucp-button");
            downBtn.style.width = 24;
            downBtn.style.paddingLeft = 0;
            downBtn.style.paddingRight = 0;
            actionRow.Add(downBtn);

            // Actions: Remove
            var removeBtn = new Button(() =>
            {
                Undo.RecordObject(profile, "Remove Step");
                profile.updateSteps.steps.RemoveAt(index);
                EditorUtility.SetDirty(profile);
                UpdateProfileDetails();
            }) { text = "×" };
            removeBtn.AddToClassList("yucp-button");
            removeBtn.AddToClassList("yucp-folder-item-remove");
            removeBtn.style.marginLeft = 4;
            actionRow.Add(removeBtn);
            
            headerRow.Add(actionRow);

            var nameField = new TextField();
            nameField.value = step.name;
            nameField.isDelayed = true;
            nameField.AddToClassList("yucp-input");
            nameField.style.flexGrow = 1;
            nameField.style.flexShrink = 1; 
            nameField.style.marginLeft = 8; // Bit more spacing from actions
            nameField.style.marginRight = 8;
            nameField.style.marginTop = 2;
            nameField.style.marginBottom = 2;
            nameField.style.minWidth = 120; 
            nameField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(profile, "Rename Step");
                step.name = evt.newValue;
                EditorUtility.SetDirty(profile);
            });
            headerRow.Add(nameField);

            container.Add(headerRow);
            
            // Allow wrapping naturally
            // headerRow.style.flexWrap = Wrap.NoWrap; // Removed enforced NoWrap  

            // --- Details Content ---
            var content = new VisualElement();
            content.AddToClassList("yucp-step-content"); // For CSS responsive targeting
            content.style.paddingLeft = 24; 
            content.style.paddingTop = 4;
            content.style.flexShrink = 0;  // Never let parent collapse this
            content.style.height = StyleKeyword.Auto;
            
            // No Config Row anymore! Type is in header. Phase is in group.

            // Dynamic Content based on Type
            RenderStepDetails(content, step, profile);

            // Validation Rules (no-code, per-step)
            RenderValidationRulesUI(content, step, profile);

            container.Add(content);
            return container;
        }

        private void RenderStepDetails(VisualElement container, UpdateStep step, ExportProfile profile)
        {
            switch (step.type)
            {
                case UpdateStepType.MoveAssets:
                case UpdateStepType.CopyAssets:
                    RenderMoveCopyUI(container, step, profile);
                    break;
                case UpdateStepType.DeleteAssets:
                case UpdateStepType.DeleteFolder:
                case UpdateStepType.CreateFolder:
                case UpdateStepType.RemoveSceneObjects:
                    RenderSimplePathListUI(container, step, profile);
                    break;
                case UpdateStepType.PromptUser:
                case UpdateStepType.WaitForUser:
                    RenderMessageUI(container, step, profile);
                    break;
                case UpdateStepType.ValidatePresence:
                    RenderValidationPresenceUI(container, step, profile);
                    break;
                case UpdateStepType.ValidateVersion:
                    RenderValidationVersionUI(container, step, profile);
                    break;
                case UpdateStepType.OpenScene:
                    RenderSceneUI(container, step, profile);
                    break;
                case UpdateStepType.BackupAssets:
                case UpdateStepType.RefreshAssetDatabase:
                case UpdateStepType.ReimportAssets:
                case UpdateStepType.ResolveGuidReferences:
                    // These typically just run without extra params, or maybe just paths
                    RenderGenericUI(container, step, profile);
                    break;
            }

            // Common Flags (Safety/Settings) - Only show relevant ones
            RenderStepFlags(container, step, profile);
        }

        private void RenderValidationRulesUI(VisualElement container, UpdateStep step, ExportProfile profile)
        {
            bool isOpen = _updateStepValidationFoldouts != null &&
                          _updateStepValidationFoldouts.TryGetValue(step.id, out var stored) && stored;
            var validationFoldout = new Foldout { text = "Validation (No Code)", value = isOpen };
            validationFoldout.style.marginTop = 8;
            validationFoldout.style.marginBottom = 6;

            var foldoutToggle = validationFoldout.Q<Toggle>();
            if (foldoutToggle != null) foldoutToggle.style.marginLeft = 0;
            validationFoldout.RegisterValueChangedCallback(evt =>
            {
                _updateStepValidationFoldouts[step.id] = evt.newValue;
            });

            var intro = new Label("Validation rules check whether a manual step was completed correctly.");
            intro.style.fontSize = 10;
            intro.style.opacity = 0.65f;
            intro.style.whiteSpace = WhiteSpace.Normal;
            intro.style.marginBottom = 6;
            validationFoldout.Add(intro);

            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.alignItems = Align.Center;
            modeRow.style.marginBottom = 6;
            var modeLabel = new Label("Mode");
            modeLabel.style.width = 80;
            modeLabel.style.fontSize = 10;
            modeLabel.style.opacity = 0.7f;
            modeRow.Add(modeLabel);
            var modeField = new EnumField(step.validationMode);
            modeField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(profile, "Change Validation Mode");
                step.validationMode = (UpdateValidationMode)evt.newValue;
                EditorUtility.SetDirty(profile);
            });
            modeRow.Add(modeField);
            validationFoldout.Add(modeRow);

            if (step.validationRules == null)
                step.validationRules = new List<UpdateValidationRule>();

            var rulesContainer = new VisualElement();
            rulesContainer.style.marginBottom = 6;

            for (int i = 0; i < step.validationRules.Count; i++)
            {
                int ruleIndex = i;
                var rule = step.validationRules[i] ?? new UpdateValidationRule();
                step.validationRules[i] = rule;

                var ruleCard = new VisualElement();
                ruleCard.style.backgroundColor = new Color(0, 0, 0, 0.15f);
                ruleCard.style.borderTopLeftRadius = 6;
                ruleCard.style.borderTopRightRadius = 6;
                ruleCard.style.borderBottomLeftRadius = 6;
                ruleCard.style.borderBottomRightRadius = 6;
                ruleCard.style.paddingTop = 6;
                ruleCard.style.paddingBottom = 6;
                ruleCard.style.paddingLeft = 6;
                ruleCard.style.paddingRight = 6;
                ruleCard.style.marginBottom = 6;

                var headerRow = new VisualElement();
                headerRow.style.flexDirection = FlexDirection.Row;
                headerRow.style.alignItems = Align.Center;
                headerRow.style.marginBottom = 4;
                headerRow.style.flexWrap = Wrap.Wrap; // User didn't add this, likely causing issues!

                var enabledToggle = new Toggle();
                enabledToggle.value = rule.enabled;
                enabledToggle.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(profile, "Toggle Validation Rule");
                    rule.enabled = evt.newValue;
                    EditorUtility.SetDirty(profile);
                });
                headerRow.Add(enabledToggle);

                var nameField = new TextField { value = rule.name };
                nameField.isDelayed = true;
                nameField.AddToClassList("yucp-input");
                nameField.style.flexGrow = 1;
                nameField.style.marginLeft = 6;
                nameField.style.minWidth = 100; // Prevent crush
                nameField.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(profile, "Rename Validation Rule");
                    rule.name = evt.newValue;
                    EditorUtility.SetDirty(profile);
                });
                headerRow.Add(nameField);

                var removeBtn = new Button(() =>
                {
                    Undo.RecordObject(profile, "Remove Validation Rule");
                    step.validationRules.RemoveAt(ruleIndex);
                    EditorUtility.SetDirty(profile);
                    UpdateProfileDetails();
                }) { text = "×" };
                removeBtn.AddToClassList("yucp-button");
                removeBtn.AddToClassList("yucp-button-danger");
                removeBtn.style.width = 20;
                removeBtn.style.marginLeft = 6;
                headerRow.Add(removeBtn);

                ruleCard.Add(headerRow);

                ruleCard.Add(CreateRuleEnumRow("Scope", rule.scope, v =>
                {
                    Undo.RecordObject(profile, "Change Rule Scope");
                    rule.scope = (UpdateValidationScope)v;
                    EditorUtility.SetDirty(profile);
                }));

                ruleCard.Add(CreateRuleEnumRow("Condition", rule.condition, v =>
                {
                    Undo.RecordObject(profile, "Change Rule Condition");
                    rule.condition = (UpdateValidationCondition)v;
                    EditorUtility.SetDirty(profile);
                    UpdateProfileDetails();
                }));

                ruleCard.Add(CreateRuleEnumRow("Severity", rule.severity, v =>
                {
                    Undo.RecordObject(profile, "Change Rule Severity");
                    rule.severity = (UpdateValidationSeverity)v;
                    EditorUtility.SetDirty(profile);
                }));

                ruleCard.Add(CreateRuleToggleRow("Allow Skip", "Allow skipping if this rule fails",
                    rule.allowSkip, v =>
                    {
                        Undo.RecordObject(profile, "Change Rule Skip");
                        rule.allowSkip = v;
                        EditorUtility.SetDirty(profile);
                    }));

                ruleCard.Add(CreateLabeledTextField("Selector", rule.selector, v =>
                {
                    rule.selector = v;
                }, profile));

                if (rule.condition == UpdateValidationCondition.CountEquals ||
                    rule.condition == UpdateValidationCondition.CountAtLeast ||
                    rule.condition == UpdateValidationCondition.CountAtMost)
                {
                    ruleCard.Add(CreateLabeledIntField("Expected Count", rule.expectedCount, v =>
                    {
                        rule.expectedCount = v;
                    }, profile));
                }

                if (rule.condition == UpdateValidationCondition.ContentContains ||
                    rule.condition == UpdateValidationCondition.ContentMatches)
                {
                    ruleCard.Add(CreateLabeledTextField(
                        rule.condition == UpdateValidationCondition.ContentContains ? "Text" : "Regex",
                        rule.text,
                        v => { rule.text = v; },
                        profile));
                }

                rulesContainer.Add(ruleCard);
            }

            validationFoldout.Add(rulesContainer);

            var addRuleBtn = new Button(() =>
            {
                Undo.RecordObject(profile, "Add Validation Rule");
                step.validationRules.Add(new UpdateValidationRule());
                EditorUtility.SetDirty(profile);
                UpdateProfileDetails();
            }) { text = "+ Add Validation Rule" };
            addRuleBtn.AddToClassList("yucp-button");
            validationFoldout.Add(addRuleBtn);

            var help = new Label(
                "Selector tokens: path:<text> pathStarts:<text> name:<text> type:<UnityType> tag:<Tag> component:<TypeName> ext:<.png> root:<Assets/SubFolder>.");
            help.style.fontSize = 9;
            help.style.opacity = 0.55f;
            help.style.whiteSpace = WhiteSpace.Normal;
            help.style.marginTop = 6;
            validationFoldout.Add(help);

            container.Add(validationFoldout);
        }

        private VisualElement CreateRuleEnumRow<T>(string label, T value, Action<Enum> setter) where T : Enum
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;

            var lbl = new Label(label);
            lbl.style.fontSize = 10;
            lbl.style.opacity = 0.7f;
            lbl.style.width = 80;
            row.Add(lbl);

            var field = new EnumField(value);
            field.RegisterValueChangedCallback(evt => setter(evt.newValue as Enum));
            row.Add(field);

            return row;
        }

        private VisualElement CreateRuleToggleRow(string label, string tooltip, bool value, Action<bool> setter)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;

            var toggle = new Toggle();
            toggle.value = value;
            toggle.RegisterValueChangedCallback(evt => setter(evt.newValue));
            row.Add(toggle);

            var text = new Label(label);
            text.style.fontSize = 10;
            text.style.opacity = 0.7f;
            text.tooltip = tooltip;
            row.Add(text);

            return row;
        }

        private VisualElement CreateLabeledIntField(string labelText, int value, Action<int> setter, ExportProfile profile)
        {
            var container = new VisualElement();
            container.style.marginBottom = 6;

            var label = new Label(labelText);
            label.style.fontSize = 10;
            label.style.opacity = 0.7f;
            label.style.marginBottom = 2;
            container.Add(label);

            var intField = new IntegerField();
            intField.value = value;
            intField.isDelayed = true;
            intField.AddToClassList("yucp-input");
            intField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(profile, "Change " + labelText);
                setter(evt.newValue);
                EditorUtility.SetDirty(profile);
            });
            container.Add(intField);

            return container;
        }

        private void RenderMoveCopyUI(VisualElement container, UpdateStep step, ExportProfile profile)
        {
             // Title label removed as it's in the header now

             var help = new Label("Source Asset  ➝  Destination Path");
             help.AddToClassList("yucp-label-secondary");
             help.style.marginBottom = 8;
             container.Add(help);

             // Parse paths into pairs
             var pairs = new List<(string src, string dst)>();
             if (step.paths != null)
             {
                 for (int i = 0; i < step.paths.Count; i += 2)
                 {
                     string s = step.paths[i];
                     string d = (i + 1 < step.paths.Count) ? step.paths[i + 1] : "";
                     pairs.Add((s, d));
                 }
             }

             var listContainer = new VisualElement();
             for (int i = 0; i < pairs.Count; i++)
             {
                 int pairIndex = i;
                 var row = new VisualElement();
                 row.AddToClassList("yucp-step-content-row");
                 // NOTE: flex-direction is controlled by CSS for responsive behavior
                 // Default is Row, narrow mode switches to Column via USS
                 row.style.flexWrap = Wrap.Wrap; // Responsive break
                 row.style.alignItems = Align.Center;
                 row.style.marginBottom = 4;
                 row.style.marginTop = 2;
                 row.style.backgroundColor = new Color(0,0,0, 0.1f);
                 row.style.borderTopLeftRadius = 4;
                 row.style.borderTopRightRadius = 4;
                 row.style.borderBottomLeftRadius = 4;
                 row.style.borderBottomRightRadius = 4;
                 row.style.paddingTop = 4;
                 row.style.paddingBottom = 4;
                 row.style.paddingLeft = 4;
                 row.style.paddingRight = 4;
                 
                 // Source Column
                 var srcCol = new VisualElement();
                 srcCol.style.flexGrow = 1;
                 srcCol.style.flexDirection = FlexDirection.Row;
                 
                 // Smart Source Picker
                 var srcObjField = new ObjectField();
                 srcObjField.objectType = typeof(UnityEngine.Object);
                 srcObjField.value = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(pairs[i].src);
                 srcObjField.style.minWidth = 80;
                 srcObjField.style.width = StyleKeyword.Auto;
                 srcObjField.style.flexGrow = 0;
                 srcObjField.style.flexBasis = 100;
                 srcObjField.style.height = 30; // Match yucp-input
                 srcObjField.style.unityTextAlign = TextAnchor.MiddleLeft;
                 srcObjField.RegisterValueChangedCallback(evt => {
                     var newPath = AssetDatabase.GetAssetPath(evt.newValue);
                     if (!string.IsNullOrEmpty(newPath))
                     {
                         // Update Text Field and Data
                         UpdatePathPair(step, pairIndex, newPath, pairs[pairIndex].dst, profile);
                         // Force refresh specific text field? Handled by rebuild usually, but let's try to update UI directly
                         // Actually simpler to just rely on the text field below for display, 
                         // but we need to update the text field value if object changes
                         var tf = row.Q<TextField>("src-tf");
                         if (tf != null) tf.value = newPath;
                     }
                 });
                 srcCol.Add(srcObjField);
 
                 var srcField = new TextField { value = pairs[i].src };
                 srcField.name = "src-tf";
                 srcField.AddToClassList("yucp-input");
                 srcField.style.flexGrow = 1;
                 srcField.style.flexShrink = 1;
                 srcField.style.marginLeft = 4;
                 srcField.RegisterValueChangedCallback(evt => {
                     UpdatePathPair(step, pairIndex, evt.newValue, pairs[pairIndex].dst, profile);
                     // Try to update object field
                     var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(evt.newValue);
                     srcObjField.value = obj;
                 });
                 srcCol.Add(srcField);
                 
                 row.Add(srcCol);

                 var arrow = new Label("➝");
                 arrow.style.alignSelf = Align.Center;
                 arrow.style.color = new Color(1,1,1,0.5f);
                 arrow.style.marginLeft = 4;
                 arrow.style.marginRight = 4;
                 row.Add(arrow);

                 // Dest Column
                 var dstField = new TextField { value = pairs[i].dst };
                 dstField.AddToClassList("yucp-input");
                 dstField.style.flexGrow = 1;
                 dstField.style.width = 100; // Min width to trigger wrap if needed?
                 dstField.style.flexShrink = 1;

                 // Allow wrapping of the whole row if really narrow?
                 // The best way for 2 columns is allow them to shrink equally.
                 row.Add(dstField);
                 
                 var removeBtn = new Button(() => {
                     Undo.RecordObject(profile, "Remove Path Pair");
                     int baseIdx = pairIndex * 2;
                     if (baseIdx < step.paths.Count) step.paths.RemoveAt(baseIdx);
                     if (baseIdx < step.paths.Count) step.paths.RemoveAt(baseIdx);
                     EditorUtility.SetDirty(profile);
                     UpdateProfileDetails();
                 }) { text = "×" };
                 removeBtn.AddToClassList("yucp-button");
                 removeBtn.AddToClassList("yucp-button-danger");
                 removeBtn.style.width = 20;
                 removeBtn.style.height = 20;
                 removeBtn.style.marginLeft = 4;
                 row.Add(removeBtn);

                 listContainer.Add(row);
             }
             container.Add(listContainer);

             var btnRow = new VisualElement();
             btnRow.style.flexDirection = FlexDirection.Row;
             
             var addBtn = new Button(() => {
                 Undo.RecordObject(profile, "Add Path Pair");
                 if (step.paths == null) step.paths = new List<string>();
                 step.paths.Add("");
                 step.paths.Add("");
                 EditorUtility.SetDirty(profile);
                 UpdateProfileDetails();
             }) { text = "+ Add Pair" };
             addBtn.AddToClassList("yucp-button");
             btnRow.Add(addBtn);
             
             container.Add(btnRow);
        }

        private void UpdatePathPair(UpdateStep step, int pairIndex, string newSrc, string newDst, ExportProfile profile)
        {
            Undo.RecordObject(profile, "Update Path Pair");
            int baseIdx = pairIndex * 2;
            while (step.paths.Count <= baseIdx + 1) step.paths.Add("");
            
            step.paths[baseIdx] = newSrc;
            step.paths[baseIdx + 1] = newDst;
            EditorUtility.SetDirty(profile);
        }

        private void RenderSimplePathListUI(VisualElement container, UpdateStep step, ExportProfile profile)
        {
             var label = new Label("Target Paths");
             label.AddToClassList("yucp-label");
             container.Add(label);

             // List of paths with Object Pickers
             if (step.paths == null) step.paths = new List<string>();
             
             var listContainer = new VisualElement();
             
             for (int i = 0; i < step.paths.Count; i++)
             {
                 int pathIndex = i;
                 var row = new VisualElement();
                 row.style.flexDirection = FlexDirection.Row;
                 row.style.flexWrap = Wrap.Wrap;
                 row.style.alignItems = Align.Center;
                 row.style.marginBottom = 2;
                 
                 // Object Picker (visual helper) - Flex Basis with min width
                 var objField = new ObjectField();
                 objField.objectType = typeof(UnityEngine.Object);
                 objField.value = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(step.paths[i]);
                 objField.style.minWidth = 80;
                 objField.style.width = StyleKeyword.Auto;
                 objField.style.flexGrow = 0; // Don't grow too much, prefer text field
                 objField.style.flexBasis = 100;
                 objField.style.height = 30; // Match yucp-input
                 objField.style.unityTextAlign = TextAnchor.MiddleLeft;
                 objField.RegisterValueChangedCallback(evt => {
                     var p = AssetDatabase.GetAssetPath(evt.newValue);
                     if (!string.IsNullOrEmpty(p))
                     {
                         Undo.RecordObject(profile, "Pick Path Asset");
                         step.paths[pathIndex] = p;
                         EditorUtility.SetDirty(profile);
                         // Update sibling text field
                         var tf = row.Q<TextField>("path-tf");
                         if (tf != null) tf.value = p;
                     }
                 });
                 row.Add(objField);

                 // Text Field (actual value)
                 var pathField = new TextField { value = step.paths[i] };
                 pathField.name = "path-tf";
                 pathField.AddToClassList("yucp-input");
                 pathField.style.flexGrow = 1;
                 pathField.style.flexShrink = 1; // Critical for responsiveness
                 pathField.style.marginLeft = 4;
                 pathField.RegisterValueChangedCallback(evt =>
                 {
                     Undo.RecordObject(profile, "Edit Path Value");
                     step.paths[pathIndex] = evt.newValue;
                     EditorUtility.SetDirty(profile);
                     // Try sync object field
                     objField.value = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(evt.newValue);
                 });
                 row.Add(pathField);

                 // Remove Button
                 var removeBtn = new Button(() => {
                     Undo.RecordObject(profile, "Remove Path");
                     step.paths.RemoveAt(pathIndex);
                     EditorUtility.SetDirty(profile);
                     UpdateProfileDetails();
                 }) { text = "×" };
                 removeBtn.AddToClassList("yucp-button");
                 removeBtn.AddToClassList("yucp-button-danger");
                 removeBtn.style.width = 20;
                 removeBtn.style.marginLeft = 4;
                 row.Add(removeBtn);

                 listContainer.Add(row);
             }
             container.Add(listContainer);
             
             // Actions
             var actionRow = new VisualElement();
             actionRow.style.flexDirection = FlexDirection.Row;
             actionRow.style.marginTop = 4;

             var addBtn = new Button(() => {
                 Undo.RecordObject(profile, "Add Empty Path");
                 step.paths.Add("");
                 EditorUtility.SetDirty(profile);
                 UpdateProfileDetails();
             }) { text = "+ Add Path" };
             addBtn.AddToClassList("yucp-button");
             actionRow.Add(addBtn);
             
             var addSelectedBtn = new Button(() => {
                 Undo.RecordObject(profile, "Add Selected Assets");
                 foreach(var obj in Selection.objects)
                 {
                     var p = AssetDatabase.GetAssetPath(obj);
                     if(!string.IsNullOrEmpty(p)) step.paths.Add(p);
                 }
                 EditorUtility.SetDirty(profile);
                 UpdateProfileDetails();
             }) { text = "+ Add Selected Assets" };
             addSelectedBtn.AddToClassList("yucp-button");
             addSelectedBtn.style.marginLeft = 4;
             actionRow.Add(addSelectedBtn);
             
             container.Add(actionRow);
        }

        private void RenderMessageUI(VisualElement container, UpdateStep step, ExportProfile profile)
        {
             container.Add(CreateLabeledTextField("Message Title/Content", step.message, v => step.message = v, profile));
        }

        private void RenderValidationPresenceUI(VisualElement container, UpdateStep step, ExportProfile profile)
        {
             RenderMessageUI(container, step, profile); // Often has a message like "Checking for..."
             RenderSimplePathListUI(container, step, profile);
        }


        private void RenderValidationVersionUI(VisualElement container, UpdateStep step, ExportProfile profile)
        {
             container.Add(CreateLabeledTextField("Package ID (e.g. com.yucp.core)", step.packageNameMatch, v => step.packageNameMatch = v, profile));
             
             var row = new VisualElement();
             row.style.flexDirection = FlexDirection.Row;
             
             var min = CreateLabeledTextField("Min Version", step.versionMin, v => step.versionMin = v, profile);
             min.style.flexGrow = 1;
             min.style.marginRight = 8;
             row.Add(min);
             
             var max = CreateLabeledTextField("Max Version", step.versionMax, v => step.versionMax = v, profile);
             max.style.flexGrow = 1;
             row.Add(max);
             
             container.Add(row);
        }

        private void RenderSceneUI(VisualElement container, UpdateStep step, ExportProfile profile)
        {
             container.Add(CreateLabeledTextField("Scene Path", step.scenePath, v => step.scenePath = v, profile));
             
             if (step.type == UpdateStepType.OpenScene) return;

             // Logic for query if needed
             container.Add(CreateLabeledTextField("Object Query (Optional)", step.query, v => step.query = v, profile));
        }

        private void RenderGenericUI(VisualElement container, UpdateStep step, ExportProfile profile)
        {
             // Fallback to showing paths and simple inputs
             RenderSimplePathListUI(container, step, profile);
             container.Add(CreateLabeledTextField("Parameter (Query)", step.query, v => step.query = v, profile));
        }

        private void RenderStepFlags(VisualElement container, UpdateStep step, ExportProfile profile)
        {
            var configContainer = new VisualElement();
            configContainer.style.marginTop = 12;
            configContainer.style.backgroundColor = new Color(0, 0, 0, 0.2f);
            configContainer.style.borderTopLeftRadius = 6;
            configContainer.style.borderTopRightRadius = 6;
            configContainer.style.borderBottomLeftRadius = 6;
            configContainer.style.borderBottomRightRadius = 6;
            configContainer.style.paddingLeft = 8;
            configContainer.style.paddingRight = 8;
            configContainer.style.paddingTop = 6;
            configContainer.style.paddingBottom = 6;
            
            var foldout = new Foldout { text = "Configuration" };
            foldout.value = false;
            
            // Style the foldout toggle to look cleaner
            var toggle = foldout.Q<Toggle>();
            if(toggle != null) toggle.style.marginLeft = 0;

            var settingsList = new VisualElement();
            settingsList.style.paddingLeft = 16;
            settingsList.style.paddingTop = 4;

            // 1. Critical Safety Settings
            if (step.IsDestructive)
            {
                // "Allow Delete" is redundant for explicit delete actions (Type=Delete...) or Move (which implies deleting source)
                bool impliesDelete = step.type == UpdateStepType.DeleteAssets || 
                                     step.type == UpdateStepType.DeleteFolder || 
                                     step.type == UpdateStepType.MoveAssets;
                                     
                if (!impliesDelete) 
                {
                    settingsList.Add(CreateSettingRow("Allow Delete", 
                        "Allow this step to delete files?", 
                        step.allowDelete, v => step.allowDelete = v, profile, isDestructive: true));
                }
                else
                {
                    // Ensure it is true if implied, though user can't uncheck it
                    if(!step.allowDelete) { step.allowDelete = true; EditorUtility.SetDirty(profile); }
                }
                
                // "Allow Overwrite" is irrelevant for Delete actions
                bool actsLikeDelete = step.type == UpdateStepType.DeleteAssets || step.type == UpdateStepType.DeleteFolder;
                
                if (!actsLikeDelete)
                {
                    settingsList.Add(CreateSettingRow("Allow Overwrite", 
                        "Allow this step to overwrite existing files?", 
                        step.allowOverwrite, v => step.allowOverwrite = v, profile, isDestructive: true));
                }
            }

            // 2. Interaction Settings
            if (step.type != UpdateStepType.PromptUser && step.type != UpdateStepType.WaitForUser)
            {
                 settingsList.Add(CreateSettingRow("Require User Confirmation", 
                    "Pause and ask user before running this step?", 
                    step.requiresUserConfirm, v => step.requiresUserConfirm = v, profile));
            }

            // 3. System Settings
            settingsList.Add(CreateSettingRow("Undo Support", 
                "Try to register Undo operations where possible?", 
                step.reversible, v => step.reversible = v, profile));

            foldout.Add(settingsList);
            configContainer.Add(foldout);
            container.Add(configContainer);
        }
        
        private VisualElement CreateSettingRow(string label, string tooltip, bool value, Action<bool> setter, ExportProfile profile, bool isDestructive = false)
        {
             var row = new VisualElement();
             row.style.flexDirection = FlexDirection.Row;
             row.style.alignItems = Align.Center;
             row.style.marginBottom = 6;
             
             // Toggle
             var toggle = new Toggle();
             toggle.value = value;
             toggle.RegisterValueChangedCallback(evt =>
             {
                 Undo.RecordObject(profile, "Toggle " + label);
                 setter(evt.newValue);
                 EditorUtility.SetDirty(profile);
             });
             row.Add(toggle);
             
             // Text Column
             var textCol = new VisualElement();
             textCol.style.marginLeft = 4;
             textCol.style.flexShrink = 1;
             
             var title = new Label(label);
             title.style.fontSize = 11;
             if (isDestructive) title.style.color = new Color(1f, 0.5f, 0.5f); // Reddish for destructive
             textCol.Add(title);
             
             var desc = new Label(tooltip);
             desc.style.fontSize = 9;
             desc.style.opacity = 0.6f;
             desc.style.whiteSpace = WhiteSpace.Normal; // Wrapping
             textCol.Add(desc);
             
             row.Add(textCol);
             
             // Click to toggle
             textCol.RegisterCallback<ClickEvent>(e => toggle.value = !toggle.value);
             
             return row;
        }

        private VisualElement CreateLabeledTextField(string labelText, string value, Action<string> setter, ExportProfile profile)
        {
            var container = new VisualElement();
            container.style.marginBottom = 6;
            
            var label = new Label(labelText);
            label.style.fontSize = 10;
            label.style.opacity = 0.7f;
            label.style.marginBottom = 2;
            container.Add(label);

            var textField = new TextField();
            textField.value = value;
            textField.isDelayed = true;
            textField.AddToClassList("yucp-input");
            textField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(profile, "Change " + labelText);
                setter(evt.newValue);
                EditorUtility.SetDirty(profile);
            });
            container.Add(textField);
            
            return container;
        }

        private void MoveStepSmart(ExportProfile profile, int currentIndex, int direction)
        {
            var steps = profile.updateSteps.steps;
            if (currentIndex < 0 || currentIndex >= steps.Count) return;
            
            var currentStep = steps[currentIndex];
            UpdatePhase phase = currentStep.phase;
            
            // Find target index to swap with
            // We look for the nearest neighbor in 'direction' that has the same Phase
            int targetIndex = -1;
            
            if (direction < 0) // Up
            {
                for (int i = currentIndex - 1; i >= 0; i--)
                {
                    if (steps[i].phase == phase)
                    {
                        targetIndex = i;
                        break;
                    }
                }
            }
            else // Down
            {
                for (int i = currentIndex + 1; i < steps.Count; i++)
                {
                    if (steps[i].phase == phase)
                    {
                        targetIndex = i;
                        break;
                    }
                }
            }
             
            if (targetIndex != -1)
            {
                Undo.RecordObject(profile, "Move Step");
                steps[currentIndex] = steps[targetIndex];
                steps[targetIndex] = currentStep;
                EditorUtility.SetDirty(profile);
                UpdateProfileDetails();
            }
        }
    }
}
