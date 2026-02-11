using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.Components;
using YUCP.Components.Resources;
using YUCP.DevTools;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.DevTools.Editor
{
    /// <summary>
    /// Custom inspector for ModelRevisionBase ScriptableObject
    /// Shows registered variants, quick-access buttons, and visual tree of base → variants
    /// </summary>
    [CustomEditor(typeof(ModelRevisionBase))]
    public class ModelRevisionBaseEditor : UnityEditor.Editor
    {
        private ModelRevisionBase revisionBase;
        private VisualElement rootContainer;
        private VisualElement variantsContainer;
        private VisualElement statusContainer;
        private int previousVariantCount;
        
        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            
            revisionBase = (ModelRevisionBase)target;
            previousVariantCount = revisionBase.registeredVariants?.Count ?? 0;
            
            // Create root container
            rootContainer = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(rootContainer);
            rootContainer.Add(YUCPComponentHeader.CreateHeaderOverlay("Model Revision Base"));
            
            // Base configuration card
            var baseCard = YUCPUIToolkitHelper.CreateCard("Base Configuration", "Core prefab references for synchronization");
            var baseContent = YUCPUIToolkitHelper.GetCardContent(baseCard);
            baseContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("basePrefab"), "Base Prefab"));
            baseContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("sourceVariant"), "Source Variant"));
            baseContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("version"), "Version"));
            rootContainer.Add(baseCard);
            
            // Registered variants card
            var variantsCard = YUCPUIToolkitHelper.CreateCard("Registered Variants", "Variant prefabs linked to this base");
            var variantsCardContent = YUCPUIToolkitHelper.GetCardContent(variantsCard);
            
            var variantsHeader = new VisualElement();
            variantsHeader.style.flexDirection = FlexDirection.Row;
            variantsHeader.style.justifyContent = Justify.FlexEnd;
            variantsHeader.style.marginBottom = 8;
            
            var addButton = YUCPUIToolkitHelper.CreateButton("Add Selected", () => AddVariant(), YUCPUIToolkitHelper.ButtonVariant.Primary);
            addButton.style.height = 24;
            variantsHeader.Add(addButton);
            variantsCardContent.Add(variantsHeader);
            
            variantsContainer = new VisualElement();
            variantsContainer.name = "variants-container";
            RefreshVariantsList();
            variantsCardContent.Add(variantsContainer);
            rootContainer.Add(variantsCard);
            
            // Sync settings card
            var syncCard = YUCPUIToolkitHelper.CreateCard("Sync Settings", "Global synchronization configuration");
            var syncContent = YUCPUIToolkitHelper.GetCardContent(syncCard);
            
            var syncSettingsFoldout = YUCPUIToolkitHelper.CreateFoldout("Settings Details", false);
            syncSettingsFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("syncSettings")));
            syncContent.Add(syncSettingsFoldout);
            rootContainer.Add(syncCard);
            
            // Mappings card
            var mappingsCard = YUCPUIToolkitHelper.CreateCard("Mappings", "Blendshape, component, and bone mappings");
            var mappingsContent = YUCPUIToolkitHelper.GetCardContent(mappingsCard);
            
            var blendshapeFoldout = YUCPUIToolkitHelper.CreateFoldout($"Blendshape Mappings ({revisionBase.blendshapeMappings.Count})", false);
            blendshapeFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("blendshapeMappings")));
            mappingsContent.Add(blendshapeFoldout);
            
            var componentFoldout = YUCPUIToolkitHelper.CreateFoldout($"Component Mappings ({revisionBase.componentMappings.Count})", false);
            componentFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("componentMappings")));
            mappingsContent.Add(componentFoldout);
            
            var boneFoldout = YUCPUIToolkitHelper.CreateFoldout($"Bone Path Cache ({revisionBase.bonePathCache.Count})", false);
            boneFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("bonePathCache")));
            mappingsContent.Add(boneFoldout);
            
            rootContainer.Add(mappingsCard);
            
            // Status card
            var statusCard = YUCPUIToolkitHelper.CreateCard("Status & Validation", "Current configuration status");
            var statusCardContent = YUCPUIToolkitHelper.GetCardContent(statusCard);
            
            statusContainer = new VisualElement();
            statusContainer.name = "status-container";
            RefreshStatusInfo();
            statusCardContent.Add(statusContainer);
            rootContainer.Add(statusCard);
            
            // Quick actions card
            var actionsCard = YUCPUIToolkitHelper.CreateCard("Quick Actions", "Common operations");
            var actionsContent = YUCPUIToolkitHelper.GetCardContent(actionsCard);
            
            var buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.justifyContent = Justify.SpaceBetween;
            
            var openWizardButton = YUCPUIToolkitHelper.CreateButton("Open Wizard", () => OpenWizard(), YUCPUIToolkitHelper.ButtonVariant.Primary);
            openWizardButton.style.flexGrow = 1;
            openWizardButton.style.marginRight = 5;
            openWizardButton.style.height = 32;
            buttonContainer.Add(openWizardButton);
            
            var validateButton = YUCPUIToolkitHelper.CreateButton("Validate", () => ValidateConfiguration(), YUCPUIToolkitHelper.ButtonVariant.Secondary);
            validateButton.style.flexGrow = 1;
            validateButton.style.marginLeft = 5;
            validateButton.style.height = 32;
            buttonContainer.Add(validateButton);
            
            actionsContent.Add(buttonContainer);
            
            // Reset button row
            var resetRow = new VisualElement();
            resetRow.style.marginTop = 10;
            
            var resetButton = YUCPUIToolkitHelper.CreateButton("Reset All Mappings", () => ResetMappings(), YUCPUIToolkitHelper.ButtonVariant.Danger);
            resetButton.style.height = 28;
            resetRow.Add(resetButton);
            
            actionsContent.Add(resetRow);
            rootContainer.Add(actionsCard);
            
            // Dynamic updates
            rootContainer.schedule.Execute(() =>
            {
                serializedObject.Update();
                
                int currentVariantCount = revisionBase.registeredVariants?.Count ?? 0;
                if (currentVariantCount != previousVariantCount)
                {
                    RefreshVariantsList();
                    previousVariantCount = currentVariantCount;
                }
            }).Every(500);
            
            rootContainer.Bind(serializedObject);
            return rootContainer;
        }
        
        private void RefreshVariantsList()
        {
            variantsContainer.Clear();
            
            if (revisionBase.registeredVariants == null || revisionBase.registeredVariants.Count == 0)
            {
                variantsContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "No variants registered. Select a prefab in the scene and click 'Add Selected' to register it.",
                    YUCPUIToolkitHelper.MessageType.Info));
                return;
            }
            
            for (int i = 0; i < revisionBase.registeredVariants.Count; i++)
            {
                var variant = revisionBase.registeredVariants[i];
                var variantItem = CreateVariantItem(variant, i);
                variantsContainer.Add(variantItem);
            }
        }
        
        private VisualElement CreateVariantItem(GameObject variant, int index)
        {
            var item = new VisualElement();
            item.AddToClassList("yucp-card");
            item.style.flexDirection = FlexDirection.Row;
            item.style.justifyContent = Justify.SpaceBetween;
            item.style.alignItems = Align.Center;
            item.style.paddingLeft = 10;
            item.style.paddingRight = 10;
            item.style.paddingTop = 8;
            item.style.paddingBottom = 8;
            item.style.marginBottom = 5;
            
            var leftSide = new VisualElement();
            leftSide.style.flexDirection = FlexDirection.Row;
            leftSide.style.alignItems = Align.Center;
            
            // Status indicator
            var statusIndicator = new VisualElement();
            statusIndicator.style.width = 10;
            statusIndicator.style.height = 10;
            statusIndicator.style.borderTopLeftRadius = 5;
            statusIndicator.style.borderTopRightRadius = 5;
            statusIndicator.style.borderBottomLeftRadius = 5;
            statusIndicator.style.borderBottomRightRadius = 5;
            statusIndicator.style.marginRight = 10;
            
            string statusText = "";
            
            if (variant == null)
            {
                statusIndicator.style.backgroundColor = new Color(0.886f, 0.29f, 0.29f); // Red
                statusText = "Missing";
            }
            else
            {
                var variantComponent = variant.GetComponent<ModelRevisionVariant>();
                if (variantComponent == null)
                {
                    statusIndicator.style.backgroundColor = new Color(0.886f, 0.647f, 0.29f); // Orange
                    statusText = "No Component";
                }
                else
                {
                    switch (variantComponent.status)
                    {
                        case VariantStatus.Synced:
                            statusIndicator.style.backgroundColor = new Color(0.212f, 0.749f, 0.694f); // Teal
                            statusText = "Synced";
                            break;
                        case VariantStatus.HasOverrides:
                            statusIndicator.style.backgroundColor = new Color(0.4f, 0.7f, 1f); // Blue
                            statusText = "Overrides";
                            break;
                        case VariantStatus.HasConflicts:
                            statusIndicator.style.backgroundColor = new Color(0.886f, 0.29f, 0.29f); // Red
                            statusText = "Conflicts";
                            break;
                        case VariantStatus.OutOfSync:
                            statusIndicator.style.backgroundColor = new Color(0.886f, 0.647f, 0.29f); // Orange
                            statusText = "Out of Sync";
                            break;
                        default:
                            statusIndicator.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f); // Gray
                            statusText = "Unknown";
                            break;
                    }
                }
            }
            
            leftSide.Add(statusIndicator);
            
            // Info container
            var infoContainer = new VisualElement();
            
            var nameLabel = new Label(variant != null ? variant.name : "Missing Reference");
            nameLabel.style.fontSize = 12;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.color = variant != null ? Color.white : new Color(0.886f, 0.29f, 0.29f);
            infoContainer.Add(nameLabel);
            
            var statusLabel = new Label(statusText);
            statusLabel.style.fontSize = 10;
            statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            infoContainer.Add(statusLabel);
            
            leftSide.Add(infoContainer);
            item.Add(leftSide);
            
            // Right side buttons
            var rightSide = new VisualElement();
            rightSide.style.flexDirection = FlexDirection.Row;
            
            if (variant != null)
            {
                var editButton = new Button(() => EditVariant(variant)) { text = "Select" };
                editButton.style.height = 22;
                editButton.style.marginRight = 5;
                editButton.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
                editButton.style.color = Color.white;
                editButton.style.borderTopLeftRadius = 4;
                editButton.style.borderTopRightRadius = 4;
                editButton.style.borderBottomLeftRadius = 4;
                editButton.style.borderBottomRightRadius = 4;
                rightSide.Add(editButton);
            }
            
            var removeButton = new Button(() => RemoveVariant(index)) { text = "×" };
            removeButton.style.width = 24;
            removeButton.style.height = 22;
            removeButton.style.fontSize = 14;
            removeButton.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f);
            removeButton.style.color = Color.white;
            removeButton.style.borderTopLeftRadius = 4;
            removeButton.style.borderTopRightRadius = 4;
            removeButton.style.borderBottomLeftRadius = 4;
            removeButton.style.borderBottomRightRadius = 4;
            rightSide.Add(removeButton);
            
            item.Add(rightSide);
            
            return item;
        }
        
        private void RefreshStatusInfo()
        {
            statusContainer.Clear();
            
            var issues = revisionBase.ValidateConfiguration();
            
            if (issues.Count == 0)
            {
                statusContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Configuration is valid",
                    YUCPUIToolkitHelper.MessageType.Info));
            }
            else
            {
                foreach (var issue in issues)
                {
                    statusContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                        issue,
                        YUCPUIToolkitHelper.MessageType.Warning));
                }
            }
            
            // Last modified info
            if (!string.IsNullOrEmpty(revisionBase.lastModified))
            {
                var lastModifiedContainer = new VisualElement();
                lastModifiedContainer.style.marginTop = 10;
                
                var modifiedLabel = new Label($"Last modified: {revisionBase.lastModified}");
                modifiedLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                modifiedLabel.style.fontSize = 11;
                lastModifiedContainer.Add(modifiedLabel);
                
                statusContainer.Add(lastModifiedContainer);
            }
        }
        
        private void AddVariant()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("No Selection", "Please select a GameObject to add as a variant.", "OK");
                return;
            }
            
            Undo.RecordObject(revisionBase, "Add Variant");
            revisionBase.RegisterVariant(selected);
            RefreshVariantsList();
            RefreshStatusInfo();
            EditorUtility.SetDirty(revisionBase);
        }
        
        private void RemoveVariant(int index)
        {
            if (index >= 0 && index < revisionBase.registeredVariants.Count)
            {
                Undo.RecordObject(revisionBase, "Remove Variant");
                revisionBase.UnregisterVariant(revisionBase.registeredVariants[index]);
                RefreshVariantsList();
                RefreshStatusInfo();
                EditorUtility.SetDirty(revisionBase);
            }
        }
        
        private void EditVariant(GameObject variant)
        {
            Selection.activeGameObject = variant;
            EditorGUIUtility.PingObject(variant);
        }
        
        private void OpenWizard()
        {
            var wizard = ModelRevisionWizard.ShowWindow();
            wizard.SetTargetBase(revisionBase);
        }
        
        private void ValidateConfiguration()
        {
            RefreshStatusInfo();
            EditorUtility.SetDirty(revisionBase);
        }
        
        private void ResetMappings()
        {
            if (EditorUtility.DisplayDialog("Reset All Mappings",
                "Are you sure you want to reset all mappings? This will clear all blendshape, component, and bone mappings.",
                "Yes", "Cancel"))
            {
                Undo.RecordObject(revisionBase, "Reset Mappings");
                revisionBase.ResetMappings();
                EditorUtility.SetDirty(revisionBase);
            }
        }
    }
}
