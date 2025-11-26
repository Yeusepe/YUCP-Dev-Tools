using System;
using System.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.AvatarUploader.UI.Components
{
	/// <summary>
	/// Toast notification component for displaying temporary messages.
	/// Matches PackageExporter's toast design pattern.
	/// </summary>
	public class ToastNotification
	{
		private VisualElement _toast;
		private Label _titleLabel;
		private Label _messageLabel;
		private Button _closeButton;
		private VisualElement _root;
		private Coroutine _autoHideCoroutine;

		public enum ToastType
		{
			Info,
			Success,
			Warning,
			Error
		}

		public ToastNotification(VisualElement root)
		{
			_root = root;
		}

		/// <summary>
		/// Show a toast notification.
		/// </summary>
		public void Show(string message, ToastType type = ToastType.Info, string title = null, float duration = 3f, Action onClose = null)
		{
			Hide(); // Hide any existing toast

			_toast = new VisualElement();
			_toast.AddToClassList("yucp-toast");
			_toast.AddToClassList($"yucp-toast-{type.ToString().ToLower()}");

			// Position at top-right
			_toast.style.position = Position.Absolute;
			_toast.style.top = 20;
			_toast.style.right = 20;
			_toast.style.width = 380;
			_toast.style.maxWidth = Length.Percent(90);

			// Header row with title and close button
			if (!string.IsNullOrEmpty(title))
			{
				var headerRow = new VisualElement();
				headerRow.AddToClassList("yucp-toast-header");
				headerRow.style.flexDirection = FlexDirection.Row;
				headerRow.style.alignItems = Align.Center;
				headerRow.style.marginBottom = 8;

				_titleLabel = new Label(title);
				_titleLabel.AddToClassList("yucp-toast-title");
				_titleLabel.style.flexGrow = 1;
				headerRow.Add(_titleLabel);

				_closeButton = new Button()
				{
					text = "×"
				};
				_closeButton.AddToClassList("yucp-toast-close");
				_closeButton.clicked += () => { Hide(); };
				_closeButton.RegisterCallback<ClickEvent>(evt =>
				{
					evt.StopPropagation();
				});
				headerRow.Add(_closeButton);

				_toast.Add(headerRow);
			}
			else
			{
				// Close button in top-right corner if no title
				_closeButton = new Button()
				{
					text = "×"
				};
				_closeButton.AddToClassList("yucp-toast-close");
				_closeButton.style.position = Position.Absolute;
				_closeButton.style.top = 8;
				_closeButton.style.right = 8;
				_closeButton.clicked += () => { Hide(); };
				_closeButton.RegisterCallback<ClickEvent>(evt =>
				{
					evt.StopPropagation();
				});
				_toast.Add(_closeButton);
			}

			// Message text
			_messageLabel = new Label(message);
			_messageLabel.AddToClassList("yucp-toast-message");
			_messageLabel.style.whiteSpace = WhiteSpace.Normal;
			_toast.Add(_messageLabel);

			_root.Add(_toast);

			// Auto-hide after duration
			if (duration > 0)
			{
				EditorApplication.delayCall += () =>
				{
					if (_toast != null && _toast.parent != null)
					{
						EditorApplication.delayCall += () => Hide();
					}
				};
			}

			// Fade in animation
			_toast.style.opacity = 0;
			_toast.schedule.Execute(() =>
			{
				_toast.style.opacity = 1;
			}).StartingIn(0);
		}

		/// <summary>
		/// Hide the toast notification.
		/// </summary>
		public void Hide()
		{
			if (_toast != null && _toast.parent != null)
			{
				// Fade out animation
				_toast.style.opacity = 1;
				_toast.schedule.Execute(() =>
				{
					if (_toast != null)
					{
						_toast.style.opacity = 0;
						EditorApplication.delayCall += () =>
						{
							if (_toast != null && _toast.parent != null)
							{
								_toast.RemoveFromHierarchy();
							}
							_toast = null;
						};
					}
				}).StartingIn(0);
			}
		}

		/// <summary>
		/// Show a success toast.
		/// </summary>
		public void ShowSuccess(string message, string title = "Success", float duration = 3f)
		{
			Show(message, ToastType.Success, title, duration);
		}

		/// <summary>
		/// Show an error toast.
		/// </summary>
		public void ShowError(string message, string title = "Error", float duration = 5f)
		{
			Show(message, ToastType.Error, title, duration);
		}

		/// <summary>
		/// Show a warning toast.
		/// </summary>
		public void ShowWarning(string message, string title = "Warning", float duration = 4f)
		{
			Show(message, ToastType.Warning, title, duration);
		}

		/// <summary>
		/// Show an info toast.
		/// </summary>
		public void ShowInfo(string message, string title = null, float duration = 3f)
		{
			Show(message, ToastType.Info, title, duration);
		}
	}
}

