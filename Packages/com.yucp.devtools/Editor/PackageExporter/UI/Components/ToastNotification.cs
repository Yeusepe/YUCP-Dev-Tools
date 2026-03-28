using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.PackageExporter.UI.Components
{
    /// <summary>
    /// Toast notification component for displaying temporary, non-blocking messages.
    /// </summary>
    public class ToastNotification
    {
        private VisualElement _toast;
        private Label _titleLabel;
        private Label _messageLabel;
        private Button _closeButton;
        private readonly VisualElement _root;

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

        public void Show(string message, ToastType type = ToastType.Info, string title = null, float duration = 3f, Action onClose = null)
        {
            if (_toast != null && _toast.parent != null)
                _toast.RemoveFromHierarchy();
            _toast = null;

            var toast = new VisualElement();
            toast.AddToClassList("yucp-toast");
            toast.AddToClassList($"yucp-toast-{type.ToString().ToLower()}");

            // Body row: icon | content | close
            var body = new VisualElement();
            body.AddToClassList("yucp-toast-body");

            var icon = new VisualElement();
            icon.AddToClassList("yucp-toast-icon");
            icon.AddToClassList($"yucp-toast-icon--{type.ToString().ToLower()}");
            body.Add(icon);

            var content = new VisualElement();
            content.AddToClassList("yucp-toast-content");

            if (!string.IsNullOrEmpty(title))
            {
                _titleLabel = new Label(title);
                _titleLabel.AddToClassList("yucp-toast-title");
                content.Add(_titleLabel);
            }

            _messageLabel = new Label(message);
            _messageLabel.AddToClassList("yucp-toast-message");
            content.Add(_messageLabel);

            body.Add(content);

            _closeButton = new Button { text = "×" };
            _closeButton.AddToClassList("yucp-toast-close");
            _closeButton.clicked += () => { onClose?.Invoke(); Hide(); };
            _closeButton.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            body.Add(_closeButton);

            toast.Add(body);

            // Auto-dismiss progress bar
            VisualElement progressFill = null;
            if (duration > 0)
            {
                var progressBg = new VisualElement();
                progressBg.AddToClassList("yucp-toast-progress");

                progressFill = new VisualElement();
                progressFill.AddToClassList("yucp-toast-progress-fill");
                progressFill.AddToClassList($"yucp-toast-progress-fill--{type.ToString().ToLower()}");
                progressBg.Add(progressFill);
                toast.Add(progressBg);
            }

            _root.Add(toast);
            _toast = toast;

            // Slide + fade in
            toast.schedule.Execute(() =>
            {
                if (toast.parent != null)
                    toast.AddToClassList("yucp-toast--visible");
            }).StartingIn(30);

            // Drain progress bar
            if (progressFill != null)
            {
                var fill = progressFill;
                toast.schedule.Execute(() =>
                {
                    if (fill.parent == null) return;
                    fill.style.transitionDuration = new List<TimeValue> { new TimeValue(duration, TimeUnit.Second) };
                    fill.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("width") };
                    fill.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.Linear) };
                    fill.style.width = Length.Percent(0);
                }).StartingIn(60);
            }

            // Auto-hide after duration
            if (duration > 0)
            {
                var toastRef = toast;
                toast.schedule.Execute(() =>
                {
                    if (_toast == toastRef)
                        Hide();
                }).StartingIn((long)(duration * 1000));
            }
        }

        public void Hide()
        {
            if (_toast == null || _toast.parent == null)
                return;

            var toastToRemove = _toast;
            _toast = null;

            toastToRemove.RemoveFromClassList("yucp-toast--visible");
            toastToRemove.schedule.Execute(() =>
            {
                if (toastToRemove.parent != null)
                    toastToRemove.RemoveFromHierarchy();
            }).StartingIn(300);
        }

        public void ShowSuccess(string message, string title = "Success", float duration = 3f) =>
            Show(message, ToastType.Success, title, duration);

        public void ShowError(string message, string title = "Error", float duration = 5f) =>
            Show(message, ToastType.Error, title, duration);

        public void ShowWarning(string message, string title = "Warning", float duration = 4f) =>
            Show(message, ToastType.Warning, title, duration);

        public void ShowInfo(string message, string title = null, float duration = 3f) =>
            Show(message, ToastType.Info, title, duration);
    }
}
