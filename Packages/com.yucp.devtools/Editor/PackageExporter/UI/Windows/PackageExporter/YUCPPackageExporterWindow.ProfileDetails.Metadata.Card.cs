using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using YUCP.DevTools.Components;
using YUCP.DevTools.Editor.PackageExporter.UI.Components;
using YUCP.Motion;
using YUCP.Motion.Core;

namespace YUCP.DevTools.Editor.PackageExporter
{
    public partial class YUCPPackageExporterWindow
    {
        private VisualElement CreateProductLinkCard(ExportProfile profile, ProductLink link)
        {
            var card = new VisualElement();
            card.AddToClassList("yucp-product-link-card");
            
            var cardContent = new VisualElement();
            cardContent.AddToClassList("yucp-product-link-content");
            cardContent.style.flexDirection = FlexDirection.Row;
            cardContent.style.alignItems = Align.Center;
            
            // Icon container - clickable to set custom icon
            var iconContainer = new VisualElement();
            iconContainer.AddToClassList("yucp-product-link-icon");
            iconContainer.tooltip = "Click to set custom icon";
            var iconImage = new Image();
            
            Texture2D displayIcon = link.GetDisplayIcon();
            if (displayIcon != null)
            {
                iconImage.image = displayIcon;
            }
            else
            {
                iconImage.image = GetPlaceholderTexture();
                // Fetch favicon if URL is provided and no custom icon
                if (!string.IsNullOrEmpty(link.url))
                {
                    FetchFavicon(profile, link, iconImage);
                }
            }
            
            iconContainer.Add(iconImage);
            
            // Add hover overlay button to browse for custom icon
            CreateHoverOverlayButton("+", () => BrowseForProductLinkIcon(profile, link, iconImage), iconContainer);
            
            cardContent.Add(iconContainer);
            
            // Link info container
            var infoContainer = new VisualElement();
            infoContainer.style.flexGrow = 1;
            infoContainer.style.marginLeft = 12;
            
            // Label field (optional)
            var labelRow = new VisualElement();
            labelRow.style.position = Position.Relative;
            var labelField = new TextField { value = link.label };
            labelField.AddToClassList("yucp-input");
            labelField.AddToClassList("yucp-product-link-label");
            
            Label labelPlaceholder = null;
            if (string.IsNullOrEmpty(link.label))
            {
                labelPlaceholder = new Label("Enter link name here (optional)");
                labelPlaceholder.AddToClassList("yucp-label-secondary");
                labelPlaceholder.style.position = Position.Absolute;
                labelPlaceholder.style.left = 8;
                labelPlaceholder.style.top = 4;
                labelPlaceholder.pickingMode = PickingMode.Ignore;
                labelRow.Add(labelPlaceholder);
            }
            labelField.tooltip = "Enter a display name for this link (e.g., 'Gumroad', 'Itch.io')";
            
            labelField.RegisterValueChangedCallback(evt =>
            {
                if (labelPlaceholder != null)
                {
                    if (string.IsNullOrEmpty(evt.newValue))
                    {
                        labelPlaceholder.style.display = DisplayStyle.Flex;
                        labelField.style.opacity = 0.7f;
                    }
                    else
                    {
                        labelPlaceholder.style.display = DisplayStyle.None;
                        labelField.style.opacity = 1f;
                    }
                }
                
                if (profile != null && link != null)
                {
                    Undo.RecordObject(profile, "Change Link Label");
                    link.label = evt.newValue;
                    EditorUtility.SetDirty(profile);
                }
            });
            labelRow.Add(labelField);
            infoContainer.Add(labelRow);
            
            // URL field
            var urlRow = new VisualElement();
            urlRow.style.position = Position.Relative;
            urlRow.style.marginTop = 6;
            var urlField = new TextField { value = link.url };
            urlField.AddToClassList("yucp-input");
            urlField.AddToClassList("yucp-product-link-url");
            
            Label urlPlaceholder = null;
            if (string.IsNullOrEmpty(link.url))
            {
                urlPlaceholder = new Label("Enter link URL here (e.g., https://example.com)");
                urlPlaceholder.AddToClassList("yucp-label-secondary");
                urlPlaceholder.style.position = Position.Absolute;
                urlPlaceholder.style.left = 8;
                urlPlaceholder.style.top = 4;
                urlPlaceholder.pickingMode = PickingMode.Ignore;
                urlRow.Add(urlPlaceholder);
            }
            urlField.tooltip = "Enter the full URL to the product page (e.g., https://example.gumroad.com/l/product)";
            
            urlField.RegisterValueChangedCallback(evt =>
            {
                if (urlPlaceholder != null)
                {
                    if (string.IsNullOrEmpty(evt.newValue))
                    {
                        urlPlaceholder.style.display = DisplayStyle.Flex;
                        urlField.style.opacity = 0.7f;
                    }
                    else
                    {
                        urlPlaceholder.style.display = DisplayStyle.None;
                        urlField.style.opacity = 1f;
                    }
                }
                
                if (profile != null && link != null)
                {
                    Undo.RecordObject(profile, "Change Link URL");
                    link.url = evt.newValue;
                    EditorUtility.SetDirty(profile);
                    
                    // Always fetch and save icon when URL is entered/changed
                    if (!string.IsNullOrEmpty(evt.newValue))
                    {
                        EditorApplication.delayCall += () =>
                        {
                            if (profile != null && link != null && !string.IsNullOrEmpty(link.url))
                            {
                                FetchFavicon(profile, link, iconImage);
                            }
                        };
                    }
                }
            });
            
            // Also fetch favicon when user finishes typing (on blur) as backup
            urlField.RegisterCallback<BlurEvent>(evt =>
            {
                Debug.Log($"[YUCP PackageExporter] URL field blur event triggered for URL: {link.url}");
                if (profile != null && link != null && !string.IsNullOrEmpty(link.url))
                {
                    Debug.Log($"[YUCP PackageExporter] Calling FetchFavicon from blur event");
                    FetchFavicon(profile, link, iconImage);
                }
                else
                {
                    Debug.LogWarning($"[YUCP PackageExporter] Blur event: profile or link is null, or URL is empty. Profile: {profile != null}, Link: {link != null}, URL: {link?.url ?? "null"}");
                }
            });
            urlRow.Add(urlField);
            infoContainer.Add(urlRow);
            
            cardContent.Add(infoContainer);
            
            // Action buttons
            var buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.marginLeft = 8;
            
            // Clear custom icon button (only show if custom icon is set)
            if (link.customIcon != null)
            {
                var clearIconButton = new Button(() =>
                {
                    if (profile != null && link != null)
                    {
                        Undo.RecordObject(profile, "Clear Custom Link Icon");
                        link.customIcon = null;
                        Texture2D displayIcon = link.GetDisplayIcon();
                        iconImage.image = displayIcon != null ? displayIcon : GetPlaceholderTexture();
                        EditorUtility.SetDirty(profile);
                    }
                });
                clearIconButton.text = "↺";
                clearIconButton.tooltip = "Clear custom icon (use auto-fetched)";
                clearIconButton.AddToClassList("yucp-button");
                clearIconButton.AddToClassList("yucp-button-icon");
                clearIconButton.style.width = 32;
                clearIconButton.style.height = 32;
                buttonContainer.Add(clearIconButton);
            }
            
            // Open in browser button
            var openButton = new Button(() =>
            {
                if (!string.IsNullOrEmpty(link.url))
                {
                    Application.OpenURL(link.url);
                }
            });
            openButton.text = "→";
            openButton.tooltip = "Open in browser";
            openButton.AddToClassList("yucp-button");
            openButton.AddToClassList("yucp-button-icon");
            openButton.style.width = 32;
            openButton.style.height = 32;
            if (link.customIcon != null)
            {
                openButton.style.marginLeft = 4;
            }
            buttonContainer.Add(openButton);
            
            // Remove button
            var removeButton = new Button(() =>
            {
                if (profile != null && profile.productLinks != null)
                {
                    Undo.RecordObject(profile, "Remove Product Link");
                    profile.productLinks.Remove(link);
                    card.RemoveFromHierarchy();
                    EditorUtility.SetDirty(profile);
                }
            });
            removeButton.text = "×";
            removeButton.tooltip = "Remove link";
            removeButton.AddToClassList("yucp-button");
            removeButton.AddToClassList("yucp-button-icon");
            removeButton.style.width = 32;
            removeButton.style.height = 32;
            removeButton.style.marginLeft = 4;
            buttonContainer.Add(removeButton);
            
            cardContent.Add(buttonContainer);
            card.Add(cardContent);
            
            return card;
        }

    }
}
