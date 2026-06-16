using UnityEngine;
using UnityEngine.UIElements;
using YUCP.DevTools.Editor.PackageExporter.UI.Components;

namespace YUCP.DevTools.Editor.PackageExporter
{
    public partial class YUCPPackageExporterWindow
    {
        private VisualElement CreateCompanionTutorialSection(ExportProfile profile)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.name = "section-companion-tutorial";

            var header = CreateCollapsibleHeader("Companion Tutorial",
                () => showCompanionTutorial,
                value => { showCompanionTutorial = value; },
                () => UpdateProfileDetails());
            section.Add(header);

            if (!showCompanionTutorial)
                return section;

            section.Add(CreateBetaBanner());

            var helpBox = new VisualElement();
            helpBox.AddToClassList("yucp-help-box");
            var helpText = new Label("An optional overlay walkthrough that auto-plays once after a buyer imports the package. It's Windows-only and click-through — each step points at part of the Unity UI and advances from the condition you choose. Drag the handle on any step to reorder it.");
            helpText.AddToClassList("yucp-help-box-text");
            helpBox.Add(helpText);
            section.Add(helpBox);

            if (profile != null && profile.companionTutorial != null)
                section.Add(new CompanionTutorialEditor(profile.companionTutorial, profile));

            return section;
        }

        // Mirrors the beta banner used by the custom Update Steps section so both experimental
        // features carry the same warning and "Report Issues" entry point.
        private VisualElement CreateBetaBanner()
        {
            var betaBanner = new VisualElement();
            betaBanner.style.flexDirection = FlexDirection.Row;
            betaBanner.style.alignItems = Align.Center;
            betaBanner.style.marginTop = 4;
            betaBanner.style.marginBottom = 12;
            betaBanner.style.paddingTop = 8;
            betaBanner.style.paddingBottom = 8;
            betaBanner.style.paddingLeft = 8;
            betaBanner.style.paddingRight = 8;
            betaBanner.style.backgroundColor = new Color(0, 0, 0, 0.1f);
            betaBanner.style.borderTopWidth = 1;
            betaBanner.style.borderBottomWidth = 1;
            betaBanner.style.borderLeftWidth = 1;
            betaBanner.style.borderRightWidth = 1;
            var borderColor = new Color(1, 1, 1, 0.1f);
            betaBanner.style.borderTopColor = borderColor;
            betaBanner.style.borderBottomColor = borderColor;
            betaBanner.style.borderLeftColor = borderColor;
            betaBanner.style.borderRightColor = borderColor;
            betaBanner.style.borderTopLeftRadius = 8;
            betaBanner.style.borderTopRightRadius = 8;
            betaBanner.style.borderBottomLeftRadius = 8;
            betaBanner.style.borderBottomRightRadius = 8;

            var betaIcon = new Label("BETA");
            betaIcon.style.fontSize = 9;
            betaIcon.style.unityFontStyleAndWeight = FontStyle.Bold;
            betaIcon.style.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            betaIcon.style.backgroundColor = new Color(0.98f, 0.57f, 0.24f, 1f);
            betaIcon.style.paddingTop = 2;
            betaIcon.style.paddingBottom = 2;
            betaIcon.style.paddingLeft = 6;
            betaIcon.style.paddingRight = 6;
            betaIcon.style.borderTopLeftRadius = 4;
            betaIcon.style.borderTopRightRadius = 4;
            betaIcon.style.borderBottomLeftRadius = 4;
            betaIcon.style.borderBottomRightRadius = 4;
            betaIcon.style.marginRight = 10;
            betaBanner.Add(betaIcon);

            var textContainer = new VisualElement();
            textContainer.style.flexGrow = 1;
            textContainer.style.flexShrink = 1;

            var betaTitle = new Label("Feature In Beta Testing");
            betaTitle.style.fontSize = 12;
            betaTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            betaTitle.style.color = new Color(1f, 1f, 1f, 0.9f);
            betaTitle.style.marginBottom = 1;
            textContainer.Add(betaTitle);

            var betaDesc = new Label("This feature is experimental. Do not use in final products.");
            betaDesc.style.fontSize = 10;
            betaDesc.style.whiteSpace = WhiteSpace.Normal;
            betaDesc.style.opacity = 0.7f;
            textContainer.Add(betaDesc);

            betaBanner.Add(textContainer);

            var discordBtn = new Button(() => Application.OpenURL("https://discord.gg/dATbJcgMgw"));
            discordBtn.text = "Report Issues";
            discordBtn.AddToClassList("yucp-button");
            discordBtn.style.height = 24;
            discordBtn.style.paddingLeft = 10;
            discordBtn.style.paddingRight = 10;
            discordBtn.style.backgroundColor = new Color(1, 1, 1, 0.1f);
            discordBtn.style.color = new Color(1, 1, 1, 0.9f);
            discordBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            betaBanner.Add(discordBtn);

            return betaBanner;
        }
    }
}
