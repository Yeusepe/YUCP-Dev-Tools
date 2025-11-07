using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	/// <summary>
	/// UI component for editing blueprint IDs.
	/// </summary>
	public static class BlueprintIdEditor
	{
		public static VisualElement Create(AvatarBuildConfig config, AvatarUploadProfile profile, System.Action onChanged)
		{
			var container = new VisualElement();

			// Use Same Blueprint ID toggle
			var sameIdRow = new VisualElement();
			sameIdRow.AddToClassList("au-form-row");
			var sameIdLabel = new Label("Use Same Blueprint ID");
			sameIdLabel.AddToClassList("au-form-label");
			sameIdRow.Add(sameIdLabel);
			var sameIdToggle = new Toggle { value = config.useSameBlueprintId };
			sameIdToggle.AddToClassList("au-toggle");
			sameIdToggle.AddToClassList("au-form-field");
			sameIdToggle.RegisterValueChangedCallback(evt =>
			{
				config.useSameBlueprintId = evt.newValue;
				if (evt.newValue && !string.IsNullOrEmpty(config.blueprintIdPC))
				{
					config.blueprintIdQuest = config.blueprintIdPC;
				}
				onChanged?.Invoke();
			});
			sameIdRow.Add(sameIdToggle);
			container.Add(sameIdRow);

			if (config.useSameBlueprintId)
			{
				var sharedRow = new VisualElement();
				sharedRow.AddToClassList("au-form-row");
				var sharedLabel = new Label("Blueprint ID (Shared)");
				sharedLabel.AddToClassList("au-form-label");
				sharedRow.Add(sharedLabel);
				var sharedField = new TextField { value = string.IsNullOrEmpty(config.blueprintIdPC) ? config.blueprintIdQuest : config.blueprintIdPC };
				sharedField.AddToClassList("au-input");
				sharedField.AddToClassList("au-form-field");
				sharedField.RegisterValueChangedCallback(evt =>
				{
					config.blueprintIdPC = evt.newValue;
					config.blueprintIdQuest = evt.newValue;
					onChanged?.Invoke();
				});
				sharedRow.Add(sharedField);

				var generateButton = new Button(() =>
				{
					var newId = BlueprintManager.GenerateNewBlueprintId();
					config.blueprintIdPC = newId;
					config.blueprintIdQuest = newId;
					sharedField.value = newId;
					onChanged?.Invoke();
				}) { text = "Generate" };
				generateButton.AddToClassList("au-button");
				generateButton.AddToClassList("au-button-small");
				sharedRow.Add(generateButton);
				container.Add(sharedRow);
			}
			else
			{
				var pcRow = new VisualElement();
				pcRow.AddToClassList("au-form-row");
				var pcLabel = new Label("Blueprint ID (PC)");
				pcLabel.AddToClassList("au-form-label");
				pcRow.Add(pcLabel);
				var pcField = new TextField { value = config.blueprintIdPC };
				pcField.AddToClassList("au-input");
				pcField.AddToClassList("au-form-field");
				pcField.RegisterValueChangedCallback(evt =>
				{
					config.blueprintIdPC = evt.newValue;
					onChanged?.Invoke();
				});
				pcRow.Add(pcField);

				var generatePcButton = new Button(() =>
				{
					config.blueprintIdPC = BlueprintManager.GenerateNewBlueprintId();
					pcField.value = config.blueprintIdPC;
					onChanged?.Invoke();
				}) { text = "Generate" };
				generatePcButton.AddToClassList("au-button");
				generatePcButton.AddToClassList("au-button-small");
				pcRow.Add(generatePcButton);
				container.Add(pcRow);

				var questRow = new VisualElement();
				questRow.AddToClassList("au-form-row");
				var questLabel = new Label("Blueprint ID (Quest)");
				questLabel.AddToClassList("au-form-label");
				questRow.Add(questLabel);
				var questField = new TextField { value = config.blueprintIdQuest };
				questField.AddToClassList("au-input");
				questField.AddToClassList("au-form-field");
				questField.RegisterValueChangedCallback(evt =>
				{
					config.blueprintIdQuest = evt.newValue;
					onChanged?.Invoke();
				});
				questRow.Add(questField);

				var generateQuestButton = new Button(() =>
				{
					config.blueprintIdQuest = BlueprintManager.GenerateNewBlueprintId();
					questField.value = config.blueprintIdQuest;
					onChanged?.Invoke();
				}) { text = "Generate" };
				generateQuestButton.AddToClassList("au-button");
				generateQuestButton.AddToClassList("au-button-small");
				questRow.Add(generateQuestButton);
				container.Add(questRow);
			}

			return container;
		}
	}
}

