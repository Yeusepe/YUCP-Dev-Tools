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
    /// Custom inspector for ModelRevisionVariant component
    /// Shows base reference, override list, and wizard launch button
    /// </summary>
    [CustomEditor(typeof(ModelRevisionVariant))]
    public class ModelRevisionVariantEditor : UnityEditor.Editor
    {
        private ModelRevisionVariant variant;
        private VisualElement rootContainer;
        private VisualElement overridesContainer;
        private VisualElement statusContainer;
        
        // Track previous values for dynamic updates
        private VariantStatus previousStatus;
        private int previousOverrideCount;
        
        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            
            variant = (ModelRevisionVariant)target;
            previousStatus = variant.status;
            previousOverrideCount = variant.componentOverrides?.Count ?? 0;
            
            // Create root container
            rootContainer = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(rootContainer);
            rootContainer.Add(YUCPComponentHeader.CreateHeaderOverlay("Model Revision Variant"));
            
            // Base configuration card
            var baseCard = YUCPUIToolkitHelper.CreateCard("Base Configuration", "Link to the Model Revision Base asset");
            var baseContent = YUCPUIToolkitHelper.GetCardContent(baseCard);
            baseContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("revisionBase"), "Revision Base"));
            baseContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("receiveTransfers"), "Receive Transfers"));
            baseContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("sendTransfers"), "Send Transfers"));
            rootContainer.Add(baseCard);
            
            // Component overrides card
            var overridesCard = YUCPUIToolkitHelper.CreateCard("Component Overrides", "Local overrides for this variant");
            var overridesCardContent = YUCPUIToolkitHelper.GetCardContent(overridesCard);
            
            var overridesHeader = new VisualElement();
            overridesHeader.style.flexDirection = FlexDirection.Row;
            overridesHeader.style.justifyContent = Justify.FlexEnd;
            overridesHeader.style.marginBottom = 8;
            
            var clearButton = YUCPUIToolkitHelper.CreateButton("Clear All", () => ClearOverrides(), YUCPUIToolkitHelper.ButtonVariant.Danger);
            clearButton.style.height = 24;
            overridesHeader.Add(clearButton);
            overridesCardContent.Add(overridesHeader);
            
            overridesContainer = new VisualElement();
            overridesContainer.name = "overrides-container";
            RefreshOverridesList();
            overridesCardContent.Add(overridesContainer);
            rootContainer.Add(overridesCard);
            
            // Status card
            var statusCard = YUCPUIToolkitHelper.CreateCard("Status & Validation", "Current variant status");
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
            
            var validateButton = YUCPUIToolkitHelper.CreateButton("Validate", () => ValidateVariant(), YUCPUIToolkitHelper.ButtonVariant.Secondary);
            validateButton.style.flexGrow = 1;
            validateButton.style.marginLeft = 5;
            validateButton.style.height = 32;
            buttonContainer.Add(validateButton);
            
            actionsContent.Add(buttonContainer);
            rootContainer.Add(actionsCard);
            
            // Dynamic updates
            rootContainer.schedule.Execute(() =>
            {
                serializedObject.Update();
                
                // Check if status changed
                if (variant.status != previousStatus)
                {
                    RefreshStatusInfo();
                    previousStatus = variant.status;
                }
                
                // Check if overrides changed
                int currentOverrideCount = variant.componentOverrides?.Count ?? 0;
                if (currentOverrideCount != previousOverrideCount)
                {
                    RefreshOverridesList();
                    previousOverrideCount = currentOverrideCount;
                }
            }).Every(500);
            
            rootContainer.Bind(serializedObject);
            return rootContainer;
        }
        
        private void RefreshOverridesList()
        {
            overridesContainer.Clear();
            
            if (variant.componentOverrides == null || variant.componentOverrides.Count == 0)
            {
                var noOverridesBox = YUCPUIToolkitHelper.CreateHelpBox(
                    "No component overrides defined. Overrides allow this variant to use different settings than the base.",
                    YUCPUIToolkitHelper.MessageType.Info);
                overridesContainer.Add(noOverridesBox);
                return;
            }
            
            for (int i = 0; i < variant.componentOverrides.Count; i++)
            {
                var overrideData = variant.componentOverrides[i];
                var overrideItem = CreateOverrideItem(overrideData, i);
                overridesContainer.Add(overrideItem);
            }
        }
        
        private VisualElement CreateOverrideItem(ComponentOverride overrideData, int index)
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
            
            // Override indicator (teal dot)
            var overrideIndicator = new VisualElement();
            overrideIndicator.style.width = 10;
            overrideIndicator.style.height = 10;
            overrideIndicator.style.borderTopLeftRadius = 5;
            overrideIndicator.style.borderTopRightRadius = 5;
            overrideIndicator.style.borderBottomLeftRadius = 5;
            overrideIndicator.style.borderBottomRightRadius = 5;
            overrideIndicator.style.marginRight = 10;
            overrideIndicator.style.backgroundColor = new Color(0.212f, 0.749f, 0.694f); // YUCP teal
            leftSide.Add(overrideIndicator);
            
            // Info container
            var infoContainer = new VisualElement();
            
            var typeLabel = new Label(overrideData.componentType);
            typeLabel.style.fontSize = 12;
            typeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            typeLabel.style.color = Color.white;
            infoContainer.Add(typeLabel);
            
            var pathLabel = new Label($"on {overrideData.bonePath}");
            pathLabel.style.fontSize = 11;
            pathLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            infoContainer.Add(pathLabel);
            
            leftSide.Add(infoContainer);
            item.Add(leftSide);
            
            // Remove button
            var removeButton = new Button(() => RemoveOverride(index)) { text = "×" };
            removeButton.style.width = 24;
            removeButton.style.height = 24;
            removeButton.style.fontSize = 14;
            removeButton.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f);
            removeButton.style.color = Color.white;
            removeButton.style.borderTopLeftRadius = 4;
            removeButton.style.borderTopRightRadius = 4;
            removeButton.style.borderBottomLeftRadius = 4;
            removeButton.style.borderBottomRightRadius = 4;
            item.Add(removeButton);
            
            return item;
        }
        
        private void RefreshStatusInfo()
        {
            statusContainer.Clear();
            
            var issues = variant.ValidateVariant();
            
            if (issues.Count == 0)
            {
                statusContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Variant configuration is valid",
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
            
            // Status badge row
            var statusRow = new VisualElement();
            statusRow.style.flexDirection = FlexDirection.Row;
            statusRow.style.alignItems = Align.Center;
            statusRow.style.marginTop = 10;
            
            var statusLabel = new Label("Status: ");
            statusLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            statusRow.Add(statusLabel);
            
            var statusBadge = new Label(variant.status.ToString());
            statusBadge.style.color = GetStatusColor(variant.status);
            statusBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            statusRow.Add(statusBadge);
            
            statusContainer.Add(statusRow);
            
            // Last sync info
            if (!string.IsNullOrEmpty(variant.lastSyncTimestamp))
            {
                var syncLabel = new Label($"Last sync: {variant.lastSyncTimestamp}");
                syncLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                syncLabel.style.fontSize = 11;
                syncLabel.style.marginTop = 5;
                statusContainer.Add(syncLabel);
            }
        }
        
        private Color GetStatusColor(VariantStatus status)
        {
            switch (status)
            {
                case VariantStatus.Synced:
                    return new Color(0.212f, 0.749f, 0.694f); // YUCP teal
                case VariantStatus.HasOverrides:
                    return new Color(0.4f, 0.7f, 1f); // Blue
                case VariantStatus.HasConflicts:
                    return new Color(0.886f, 0.29f, 0.29f); // Red
                case VariantStatus.OutOfSync:
                    return new Color(0.886f, 0.647f, 0.29f); // Orange
                default:
                    return new Color(0.5f, 0.5f, 0.5f); // Gray
            }
        }
        
        private void ClearOverrides()
        {
            if (EditorUtility.DisplayDialog("Clear Overrides", 
                "Are you sure you want to clear all component overrides? This action cannot be undone.", 
                "Yes", "Cancel"))
            {
                Undo.RecordObject(variant, "Clear Overrides");
                variant.ClearVariantData();
                RefreshOverridesList();
                RefreshStatusInfo();
                EditorUtility.SetDirty(variant);
            }
        }
        
        private void RemoveOverride(int index)
        {
            if (index >= 0 && index < variant.componentOverrides.Count)
            {
                Undo.RecordObject(variant, "Remove Override");
                variant.componentOverrides.RemoveAt(index);
                RefreshOverridesList();
                RefreshStatusInfo();
                EditorUtility.SetDirty(variant);
            }
        }
        
        private void OpenWizard()
        {
            var wizard = ModelRevisionWizard.ShowWindow();
            wizard.SetTargetVariant(variant);
        }
        
        private void ValidateVariant()
        {
            RefreshStatusInfo();
            EditorUtility.SetDirty(variant);
        }
    }
}
