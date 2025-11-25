using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	/// <summary>
	/// UI component for displaying blueprint IDs (read-only, assigned by VRChat on upload).
	/// </summary>
	public static class BlueprintIdEditor
	{
		public static VisualElement Create(AvatarAsset config, AvatarCollection profile, System.Action onChanged)
		{
			var container = new VisualElement();

			var helpBox = new VisualElement();
			helpBox.AddToClassList("au-help-box");
			var helpText = new Label("Blueprint IDs are assigned by VRChat when you upload. They will appear here after your first successful upload.");
			helpText.AddToClassList("au-help-box-text");
			helpBox.Add(helpText);
			container.Add(helpBox);

				var pcRow = new VisualElement();
				pcRow.AddToClassList("au-form-row");
				var pcLabel = new Label("Blueprint ID (PC)");
				pcLabel.AddToClassList("au-form-label");
				pcRow.Add(pcLabel);
			
			var pcValue = string.IsNullOrEmpty(config.blueprintIdPC) ? "Unassigned" : config.blueprintIdPC;
			var pcField = new Label(pcValue);
				pcField.AddToClassList("au-form-field");
			if (string.IsNullOrEmpty(config.blueprintIdPC))
			{
				pcField.style.color = new Color(0.6f, 0.6f, 0.6f);
				pcField.style.unityFontStyleAndWeight = FontStyle.Italic;
			}
				pcRow.Add(pcField);
				container.Add(pcRow);

				var questRow = new VisualElement();
				questRow.AddToClassList("au-form-row");
				var questLabel = new Label("Blueprint ID (Quest)");
				questLabel.AddToClassList("au-form-label");
				questRow.Add(questLabel);
			
			var questValue = string.IsNullOrEmpty(config.blueprintIdQuest) ? "Unassigned" : config.blueprintIdQuest;
			var questField = new Label(questValue);
				questField.AddToClassList("au-form-field");
			if (string.IsNullOrEmpty(config.blueprintIdQuest))
			{
				questField.style.color = new Color(0.6f, 0.6f, 0.6f);
				questField.style.unityFontStyleAndWeight = FontStyle.Italic;
			}
				questRow.Add(questField);
				container.Add(questRow);

			return container;
		}
	}
}






