using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3A.Editor;
using VRC.SDK3A.Editor.Elements;
using VRC.SDKBase.Editor;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	internal static class ControlPanelDataBridge
	{
		private static readonly Dictionary<string, FieldInfo> _stateFields = new();
		private static readonly Dictionary<string, MethodInfo> _stateMethods = new();

		internal static bool TryGetAvatarName(IVRCSdkControlPanelBuilder builder, out string name)
		{
			name = null;
			if (builder == null)
			{
				Debug.LogWarning("[ControlPanelDataBridge] TryGetAvatarName: builder is null");
				return false;
			}

			try
			{
				var builderType = builder.GetType();
				
				// Get from _nameField.value (UI field)
				var nameFieldField = GetField(builderType, "_nameField");
				if (nameFieldField != null)
				{
					var nameFieldObj = nameFieldField.GetValue(builder);
					if (nameFieldObj != null)
					{
						var valueProperty = nameFieldObj.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (valueProperty != null)
						{
							name = valueProperty.GetValue(nameFieldObj) as string;
							if (!string.IsNullOrEmpty(name))
							{
								return true;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[ControlPanelDataBridge] Failed to get avatar name: {ex.Message}\n{ex.StackTrace}");
			}

			return false;
		}

		private static void InspectBuilderFields(IVRCSdkControlPanelBuilder builder)
		{
			try
			{
				// Inspection method - no logging to reduce console spam
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[ControlPanelDataBridge] Failed to inspect builder fields: {ex.Message}");
			}
		}

		private static void InspectStateFields(object state)
		{
			try
			{
				// Inspection method - no logging to reduce console spam
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[ControlPanelDataBridge] Failed to inspect state fields: {ex.Message}");
			}
		}

		internal static bool TrySetAvatarName(IVRCSdkControlPanelBuilder builder, string name, bool suppressEvents = false)
		{
			if (builder == null || string.IsNullOrEmpty(name))
				return false;

			try
			{
				var builderType = builder.GetType();
				var nameFieldField = GetField(builderType, "_nameField");
				if (nameFieldField != null)
				{
					var nameFieldObj = nameFieldField.GetValue(builder);
					if (nameFieldObj != null)
					{
						var valueProperty = nameFieldObj.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (valueProperty != null)
						{
							valueProperty.SetValue(nameFieldObj, name);
							if (!suppressEvents)
								builder.OnContentChanged?.Invoke(builder, EventArgs.Empty);
							return true;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to set avatar name: {ex.Message}");
			}

			return false;
		}

		internal static bool TryGetAvatarDescription(IVRCSdkControlPanelBuilder builder, out string description)
		{
			description = null;
			if (builder == null)
				return false;

			try
			{
				var builderType = builder.GetType();
				
				// Get from _descriptionField.value (UI field)
				var descFieldField = GetField(builderType, "_descriptionField");
				if (descFieldField != null)
				{
					var descFieldObj = descFieldField.GetValue(builder);
					if (descFieldObj != null)
					{
						var valueProperty = descFieldObj.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (valueProperty != null)
						{
							description = valueProperty.GetValue(descFieldObj) as string;
							if (description != null)
							{
								return true;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to get avatar description: {ex.Message}");
			}

			return false;
		}

		internal static bool TrySetAvatarDescription(IVRCSdkControlPanelBuilder builder, string description, bool suppressEvents = false)
		{
			if (builder == null)
				return false;

			try
			{
				var builderType = builder.GetType();
				var descFieldField = GetField(builderType, "_descriptionField");
				if (descFieldField != null)
				{
					var descFieldObj = descFieldField.GetValue(builder);
					if (descFieldObj != null)
					{
						var valueProperty = descFieldObj.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (valueProperty != null)
						{
							valueProperty.SetValue(descFieldObj, description ?? string.Empty);
							if (!suppressEvents)
								builder.OnContentChanged?.Invoke(builder, EventArgs.Empty);
							return true;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to set avatar description: {ex.Message}");
			}

			return false;
		}

		internal static bool TryGetAvatarTags(IVRCSdkControlPanelBuilder builder, out List<string> tags)
		{
			tags = null;
			if (builder == null)
				return false;

			try
			{
				var builderType = builder.GetType();
				
				// Get from _tagsField (UI field) - inspect all possible properties
				var tagsFieldFieldNames = new[] { "_tagsField", "_tagField", "tagsField", "tagField" };
				foreach (var fieldName in tagsFieldFieldNames)
				{
					var tagsFieldField = GetField(builderType, fieldName);
					if (tagsFieldField != null)
					{
						var tagsFieldObj = tagsFieldField.GetValue(builder);
						if (tagsFieldObj != null)
						{
							var tagsFieldType = tagsFieldObj.GetType();
							
							// Try common property names (but don't return early if empty - check all)
							var propertyNames = new[] { "value", "tags", "selectedTags", "Values", "Tags", "SelectedTags", "selectedValues", "SelectedValues" };
							List<string> bestTags = null;
							
							foreach (var propName in propertyNames)
							{
								var prop = tagsFieldType.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
								if (prop != null)
								{
									try
									{
										var tagsValue = prop.GetValue(tagsFieldObj);
										if (tagsValue != null)
										{
											if (tagsValue is List<string> tagList && tagList.Count > 0)
											{
												if (bestTags == null || tagList.Count > bestTags.Count)
												{
													bestTags = new List<string>(tagList);
												}
											}
											else if (tagsValue is System.Collections.IEnumerable enumerable)
											{
												var tempTags = new List<string>();
												foreach (var item in enumerable)
												{
													if (item is string tag)
														tempTags.Add(tag);
												}
												if (tempTags.Count > 0 && (bestTags == null || tempTags.Count > bestTags.Count))
												{
													bestTags = tempTags;
												}
											}
										}
									}
									catch
									{
										// Skip properties that can't be accessed
									}
								}
							}
							
							// Try methods like GetTags(), GetSelectedTags(), etc.
							var methodNames = new[] { "GetTags", "GetSelectedTags", "GetValue" };
							foreach (var methodName in methodNames)
							{
								var method = tagsFieldType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
								if (method != null)
								{
									try
									{
										var tagsValue = method.Invoke(tagsFieldObj, null);
										if (tagsValue != null)
										{
											if (tagsValue is List<string> tagList && tagList.Count > 0)
											{
												if (bestTags == null || tagList.Count > bestTags.Count)
												{
													bestTags = new List<string>(tagList);
												}
											}
											else if (tagsValue is System.Collections.IEnumerable enumerable)
											{
												var tempTags = new List<string>();
												foreach (var item in enumerable)
												{
													if (item is string tag)
														tempTags.Add(tag);
												}
												if (tempTags.Count > 0 && (bestTags == null || tempTags.Count > bestTags.Count))
												{
													bestTags = tempTags;
												}
											}
										}
									}
									catch
									{
										// Skip methods that can't be invoked
									}
								}
							}
							
							if (bestTags != null && bestTags.Count > 0)
							{
								tags = bestTags;
								return true;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[ControlPanelDataBridge] Failed to get avatar tags: {ex.Message}");
			}

			return false;
		}

		internal static bool TrySetAvatarTags(IVRCSdkControlPanelBuilder builder, List<string> tags, bool suppressEvents = false)
		{
			if (builder == null)
				return false;

			try
			{
				var builderType = builder.GetType();
				var tagsFieldFieldNames = new[] { "_tagsField", "_tagField", "tagsField", "tagField" };
				foreach (var fieldName in tagsFieldFieldNames)
				{
					var tagsFieldField = GetField(builderType, fieldName);
					if (tagsFieldField != null)
					{
						var tagsFieldObj = tagsFieldField.GetValue(builder);
						if (tagsFieldObj != null)
						{
							var valueProperty = tagsFieldObj.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							if (valueProperty != null)
							{
								valueProperty.SetValue(tagsFieldObj, tags ?? new List<string>());
								if (!suppressEvents)
									builder.OnContentChanged?.Invoke(builder, EventArgs.Empty);
								return true;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to set avatar tags: {ex.Message}");
			}

			return false;
		}

		internal static bool TryGetReleaseStatus(IVRCSdkControlPanelBuilder builder, out string status)
		{
			status = null;
			if (builder == null)
				return false;

			try
			{
				var builderType = builder.GetType();
				
				// Get from _visibilityPopup.value (UI field)
				var visibilityPopupField = GetField(builderType, "_visibilityPopup");
				if (visibilityPopupField != null)
				{
					var visibilityPopupObj = visibilityPopupField.GetValue(builder);
					if (visibilityPopupObj != null)
					{
						var valueProperty = visibilityPopupObj.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (valueProperty != null)
						{
							var popupValue = valueProperty.GetValue(visibilityPopupObj);
							if (popupValue != null)
							{
								status = popupValue.ToString().ToLowerInvariant();
								return true;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to get release status: {ex.Message}");
			}

			return false;
		}

		internal static bool TrySetReleaseStatus(IVRCSdkControlPanelBuilder builder, string status, bool suppressEvents = false)
		{
			if (builder == null || string.IsNullOrEmpty(status))
				return false;

			try
			{
				var builderType = builder.GetType();
				var visibilityPopupField = GetField(builderType, "_visibilityPopup");
				if (visibilityPopupField != null)
				{
					var visibilityPopupObj = visibilityPopupField.GetValue(builder);
					if (visibilityPopupObj != null)
					{
						var valueProperty = visibilityPopupObj.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (valueProperty != null)
						{
							valueProperty.SetValue(visibilityPopupObj, status);
							if (!suppressEvents)
								builder.OnContentChanged?.Invoke(builder, EventArgs.Empty);
							return true;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to set release status: {ex.Message}");
			}

			return false;
		}

		internal static bool TryCaptureThumbnail(IVRCSdkControlPanelBuilder builder)
		{
			if (builder == null)
				return false;

			try
			{
				var captureMethod = GetMethod(builder.GetType(), "CaptureThumbnail");
				if (captureMethod != null)
				{
					captureMethod.Invoke(builder, null);
					return true;
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to capture thumbnail: {ex.Message}");
			}

			return false;
		}

		internal static bool TryUploadThumbnail(IVRCSdkControlPanelBuilder builder, string filePath)
		{
			if (builder == null || string.IsNullOrEmpty(filePath))
				return false;

			try
			{
				var uploadMethod = GetMethod(builder.GetType(), "UploadThumbnail", new[] { typeof(string) });
				if (uploadMethod != null)
				{
					uploadMethod.Invoke(builder, new object[] { filePath });
					return true;
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to upload thumbnail: {ex.Message}");
			}

			return false;
		}

		internal static bool TryGetAvatarStyle(IVRCSdkControlPanelBuilder builder, bool primary, out string style)
		{
			style = null;
			if (builder == null)
				return false;

			try
			{
				var builderType = builder.GetType();
				var fieldName = primary ? "_primaryStyleField" : "_secondaryStyleField";
				var styleFieldField = GetField(builderType, fieldName);
				if (styleFieldField != null)
				{
					var styleFieldObj = styleFieldField.GetValue(builder);
					if (styleFieldObj != null)
					{
						var valueProperty = styleFieldObj.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (valueProperty != null)
						{
							style = valueProperty.GetValue(styleFieldObj) as string;
							return true;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to get avatar style: {ex.Message}");
			}

			return false;
		}

		internal static bool TrySetAvatarStyle(IVRCSdkControlPanelBuilder builder, bool primary, string style)
		{
			if (builder == null)
				return false;

			try
			{
				var builderType = builder.GetType();
				var fieldName = primary ? "_primaryStyleField" : "_secondaryStyleField";
				var styleFieldField = GetField(builderType, fieldName);
				if (styleFieldField != null)
				{
					var styleFieldObj = styleFieldField.GetValue(builder);
					if (styleFieldObj != null)
					{
						var valueProperty = styleFieldObj.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (valueProperty != null)
						{
							valueProperty.SetValue(styleFieldObj, style ?? string.Empty);
							builder.OnContentChanged?.Invoke(builder, EventArgs.Empty);
							return true;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to set avatar style: {ex.Message}");
			}

			return false;
		}

		internal static bool TryGetThumbnailBlock(IVRCSdkControlPanelBuilder builder, out object thumbnailBlock)
		{
			thumbnailBlock = null;
			if (builder == null)
				return false;

			try
			{
				var builderType = builder.GetType();
				
				// First try to get the thumbnail block field directly
				var thumbnailField = GetField(builderType, "_thumbnailBlock");
				if (thumbnailField != null)
				{
					thumbnailBlock = thumbnailField.GetValue(builder);
					if (thumbnailBlock != null)
						return true;
				}
				
				// If that fails, try to get the visual root and find thumbnail block
				var visualRootField = GetField(builderType, "_visualRoot");
				if (visualRootField != null)
				{
					var visualRoot = visualRootField.GetValue(builder) as VisualElement;
					if (visualRoot != null)
					{
						// Try to find thumbnail block in the visual root using reflection
						// ThumbnailBlock is in VRC.SDKBase.Editor.Elements namespace
						var thumbnailBlockType = Type.GetType("VRC.SDKBase.Editor.Elements.ThumbnailBlock, VRC.SDKBase.Editor");
						if (thumbnailBlockType == null)
						{
							// Fallback: try VRC.SDK3A namespace
							thumbnailBlockType = Type.GetType("VRC.SDK3A.Editor.Elements.ThumbnailBlock, VRC.SDK3A.Editor");
						}
						if (thumbnailBlockType != null)
						{
							var genericQ = typeof(VisualElement).GetMethods()
								.FirstOrDefault(m => m.Name == "Q" && m.IsGenericMethod && m.GetParameters().Length == 0);
							if (genericQ != null)
							{
								var method = genericQ.MakeGenericMethod(thumbnailBlockType);
								thumbnailBlock = method.Invoke(visualRoot, null);
								if (thumbnailBlock != null)
									return true;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[ControlPanelDataBridge] Failed to get thumbnail block: {ex.Message}");
			}

			return false;
		}

		internal static bool TryGetAvatarThumbnail(IVRCSdkControlPanelBuilder builder, out Texture2D thumbnail)
		{
			thumbnail = null;
			if (builder == null)
				return false;

			try
			{
				var builderType = builder.GetType();
				
				// Strategy 1: Try to get from UI field (like _thumbnailImageField.value)
				var thumbnailImageFieldNames = new[] { "_thumbnailImageField", "_thumbnailField", "_imageField", "thumbnailImageField", "thumbnailField" };
				foreach (var fieldName in thumbnailImageFieldNames)
				{
					var thumbnailImageField = GetField(builderType, fieldName);
					if (thumbnailImageField != null)
					{
						var thumbnailImageObj = thumbnailImageField.GetValue(builder);
						if (thumbnailImageObj != null)
						{
							// Try to get value property (for UI elements)
							var valueProperty = thumbnailImageObj.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							if (valueProperty != null)
							{
								var value = valueProperty.GetValue(thumbnailImageObj);
								if (value is Texture2D tex)
								{
									thumbnail = tex;
									return true;
								}
								if (value is UnityEngine.UIElements.Image img && img.image != null)
								{
									thumbnail = img.image as Texture2D;
									if (thumbnail != null)
										return true;
								}
							}
							
							// If it's already an Image element, get its image property
							if (thumbnailImageObj is UnityEngine.UIElements.Image imgElement && imgElement.image != null)
							{
								thumbnail = imgElement.image as Texture2D;
								if (thumbnail != null)
									return true;
							}
							
							// If it's already a Texture2D
							if (thumbnailImageObj is Texture2D tex2)
							{
								thumbnail = tex2;
								return true;
							}
						}
					}
				}
				
				// Strategy 2: Try to get thumbnail from ThumbnailBlock using public properties
				// ThumbnailBlock.Thumbnail.CurrentImageTexture is the path (from VRChat SDK source)
				if (TryGetThumbnailBlock(builder, out var thumbnailBlock))
				{
					if (thumbnailBlock != null)
					{
						// ThumbnailBlock has a public Thumbnail property (line 27 in ThumbnailBlock.cs)
						var thumbnailProperty = thumbnailBlock.GetType().GetProperty("Thumbnail", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (thumbnailProperty != null)
						{
							var thumbnailObj = thumbnailProperty.GetValue(thumbnailBlock);
							if (thumbnailObj != null)
							{
								// Thumbnail has a public CurrentImageTexture property (line 86 in Thumbnail.cs)
								// This returns the _imageTexture field which stores the actual Texture2D
								var currentImageTextureProperty = thumbnailObj.GetType().GetProperty("CurrentImageTexture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
								if (currentImageTextureProperty != null)
								{
									var texture = currentImageTextureProperty.GetValue(thumbnailObj) as Texture2D;
									if (texture != null)
									{
										thumbnail = texture;
										return true;
									}
									else
									{
										// Texture is null - might be loaded from URL instead
										// When loaded from URL via SetImageUrl, the texture is stored in _imageElement.style.backgroundImage
										// Try to get texture from the _imageElement's backgroundImage style
										var imageElementField = thumbnailObj.GetType().GetField("_imageElement", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
										if (imageElementField != null)
										{
											var imageElement = imageElementField.GetValue(thumbnailObj) as VisualElement;
											if (imageElement != null)
											{
												// backgroundImage is a StyleBackground, value is a Background struct with a texture property
												// When SetImageUrl is called, it sets _imageElement.style.backgroundImage = await VRCApi.GetImage(...)
												try
												{
													var backgroundImageStyle = imageElement.style.backgroundImage;
													if (backgroundImageStyle != null)
													{
														var background = backgroundImageStyle.value;
														if (background.texture != null)
														{
															thumbnail = background.texture as Texture2D;
															if (thumbnail != null)
																return true;
														}
													}
												}
												catch (Exception ex)
												{
													Debug.LogWarning($"[ControlPanelDataBridge] Failed to access backgroundImage style: {ex.Message}");
												}
											}
										}
										
										// If still no texture, try to load from URL if available
										var currentImageProperty = thumbnailObj.GetType().GetProperty("CurrentImage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
										if (currentImageProperty != null)
										{
											var imageUrl = currentImageProperty.GetValue(thumbnailObj) as string;
											if (!string.IsNullOrEmpty(imageUrl))
											{
												// Try to load the image from URL using VRCApi
												try
												{
													var apiType = Type.GetType("VRC.SDKBase.Editor.Api.VRCApi, VRCSDKBase");
													if (apiType != null)
													{
														var getImageMethod = apiType.GetMethod("GetImage", BindingFlags.Static | BindingFlags.Public);
														if (getImageMethod != null)
														{
															// GetImage is async, but we can't await here
															// Instead, we'll try to get it synchronously if it's already cached
															// Or we could use a Task.Run but that's complex
															// For now, just log that we need the URL
															Debug.LogWarning($"[ControlPanelDataBridge] Thumbnail has URL ({imageUrl}) but texture not in memory. Image may need to be loaded asynchronously.");
														}
													}
												}
												catch (Exception ex)
												{
													Debug.LogWarning($"[ControlPanelDataBridge] Failed to access VRCApi for loading thumbnail: {ex.Message}");
												}
											}
										}
									}
								}
								else
								{
									Debug.LogWarning($"[ControlPanelDataBridge] Thumbnail object found but CurrentImageTexture property not found. Type: {thumbnailObj.GetType().FullName}, Available properties: {string.Join(", ", thumbnailObj.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(p => p.Name))}");
								}
							}
							else
							{
								Debug.LogWarning("[ControlPanelDataBridge] ThumbnailBlock.Thumbnail property returned null");
							}
						}
						else
						{
							Debug.LogWarning($"[ControlPanelDataBridge] ThumbnailBlock found but Thumbnail property not found. Type: {thumbnailBlock.GetType().FullName}, Available properties: {string.Join(", ", thumbnailBlock.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(p => p.Name))}");
						}
					}
				}
				else
				{
					Debug.LogWarning("[ControlPanelDataBridge] ThumbnailBlock not found in builder");
				}
				
				// Strategy 3: Try to get from _thumbnail field on builder
				var thumbnailFieldNames = new[] { "_thumbnail", "_thumbnailTexture", "_avatarThumbnail", "thumbnail" };
				foreach (var fieldName in thumbnailFieldNames)
				{
					var thumbnailField = GetField(builderType, fieldName);
					if (thumbnailField != null)
					{
						var thumbnailValue = thumbnailField.GetValue(builder);
						if (thumbnailValue is Texture2D tex)
						{
							thumbnail = tex;
							return true;
						}
					}
				}
				
				// Strategy 3: Try to get from _avatarData.thumbnail or similar
				var avatarDataField = GetField(builderType, "_avatarData");
				if (avatarDataField != null)
				{
					var avatarData = avatarDataField.GetValue(builder);
					if (avatarData != null)
					{
						var thumbnailPropertyNames = new[] { "thumbnail", "thumbnailTexture", "image", "icon" };
						foreach (var propName in thumbnailPropertyNames)
						{
							var thumbnailProp = avatarData.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							if (thumbnailProp != null)
							{
								var thumbnailValue = thumbnailProp.GetValue(avatarData);
								if (thumbnailValue is Texture2D tex)
								{
									thumbnail = tex;
									return true;
								}
							}
						}
					}
				}
				
				// Strategy 4: Try to find thumbnail in visual root hierarchy
				var visualRootField = GetField(builderType, "_visualRoot");
				if (visualRootField != null)
				{
					var visualRoot = visualRootField.GetValue(builder) as VisualElement;
					if (visualRoot != null)
					{
						var imageType = typeof(UnityEngine.UIElements.Image);
						var qMethod = typeof(VisualElement).GetMethods()
							.FirstOrDefault(m => m.Name == "Q" && m.IsGenericMethod && m.GetParameters().Length == 0);
						if (qMethod != null)
						{
							var genericQ = qMethod.MakeGenericMethod(imageType);
							var imageElement = genericQ.Invoke(visualRoot, null) as UnityEngine.UIElements.Image;
							if (imageElement != null && imageElement.image != null)
							{
								thumbnail = imageElement.image as Texture2D;
								if (thumbnail != null)
									return true;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[ControlPanelDataBridge] Failed to get avatar thumbnail: {ex.Message}");
			}

			return false;
		}

		internal static bool TryGetValidationIssues(IVRCSdkControlPanelBuilder builder, out List<ValidationIssue> issues)
		{
			issues = new List<ValidationIssue>();
			if (builder == null)
				return false;

			try
			{
				var validateMethod = GetMethod(builder.GetType(), "Validate");
				if (validateMethod != null)
				{
					var result = validateMethod.Invoke(builder, null);
					if (result is System.Collections.IEnumerable enumerable)
					{
						foreach (var item in enumerable)
						{
							if (item != null)
							{
								var issue = new ValidationIssue
								{
									Message = item.ToString(),
									Severity = ValidationSeverity.Warning
								};

								var severityProp = item.GetType().GetProperty("Severity");
								if (severityProp != null)
								{
									var severityValue = severityProp.GetValue(item);
									if (severityValue != null)
									{
										var severityStr = severityValue.ToString().ToLowerInvariant();
										if (severityStr.Contains("error"))
											issue.Severity = ValidationSeverity.Error;
										else if (severityStr.Contains("warning"))
											issue.Severity = ValidationSeverity.Warning;
										else
											issue.Severity = ValidationSeverity.Info;
									}
								}

								var messageProp = item.GetType().GetProperty("Message");
								if (messageProp != null)
								{
									issue.Message = messageProp.GetValue(item)?.ToString() ?? issue.Message;
								}

								issues.Add(issue);
							}
						}
						return true;
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarUploader] Failed to get validation issues: {ex.Message}");
			}

			return false;
		}

		private static object GetBuilderState(IVRCSdkControlPanelBuilder builder)
		{
			if (builder == null)
			{
				Debug.LogWarning("[ControlPanelDataBridge] GetBuilderState: builder is null");
				return null;
			}

			try
			{
				var builderType = builder.GetType();

				var stateField = GetField(builderType, "_state");
				if (stateField != null)
				{
					var state = stateField.GetValue(builder);
					return state;
				}
				else
				{
					Debug.LogWarning("[ControlPanelDataBridge] _state field not found");
				}

				var contentField = GetField(builderType, "_content");
				if (contentField != null)
				{
					var content = contentField.GetValue(builder);
					return content;
				}
				else
				{
					Debug.LogWarning("[ControlPanelDataBridge] _content field not found");
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[ControlPanelDataBridge] Failed to get builder state: {ex.Message}\n{ex.StackTrace}");
			}

			return null;
		}

		private static FieldInfo GetField(Type type, string fieldName)
		{
			if (type == null || string.IsNullOrEmpty(fieldName))
				return null;

			var key = $"{type.FullName}.{fieldName}";
			if (_stateFields.TryGetValue(key, out var cached))
				return cached;

			var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (field != null)
			{
				_stateFields[key] = field;
			}

			return field;
		}

		private static MethodInfo GetMethod(Type type, string methodName, Type[] parameterTypes = null)
		{
			if (type == null || string.IsNullOrEmpty(methodName))
				return null;

			var paramKey = parameterTypes != null ? string.Join(",", parameterTypes.Select(t => t.FullName)) : "";
			var key = $"{type.FullName}.{methodName}({paramKey})";
			if (_stateMethods.TryGetValue(key, out var cached))
				return cached;

			MethodInfo method = null;
			if (parameterTypes != null)
			{
				method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, parameterTypes, null);
			}
			else
			{
				method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			}

			if (method != null)
			{
				_stateMethods[key] = method;
			}

			return method;
		}

		internal enum ValidationSeverity
		{
			Info,
			Warning,
			Error
		}

		internal class ValidationIssue
		{
			public string Message;
			public ValidationSeverity Severity;
		}
	}
}

