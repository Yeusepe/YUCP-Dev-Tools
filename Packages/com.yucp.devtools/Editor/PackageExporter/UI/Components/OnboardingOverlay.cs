using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.PackageExporter.UI.Components
{
    public class OnboardingOverlay : VisualElement
    {
        private List<OnboardingStep> _steps;
        private int _currentStepIndex = -1;
        private VisualElement _dimmerTop;
        private VisualElement _dimmerBottom;
        private VisualElement _dimmerLeft;
        private VisualElement _dimmerRight;
        private VisualElement _spotlightBorder;
        private VisualElement _contentCard;
        private Label _stepTitle;
        private Label _stepDescription;
        private Label _stepCounter;
        private Button _nextButton;
        private Button _prevButton;
        private Button _skipButton;
        private VisualElement _rootContainer;
        private Action _onComplete;
        
        // Animation state
        private Rect _currentSpotlightRect;
        private Rect _targetSpotlightRect;
        private bool _isAnimating;
        private float _animationProgress;
        private double _lastTime;
        private const float ANIMATION_DURATION = 0.4f; // Seconds
        
        public OnboardingOverlay(VisualElement rootContainer, Action onComplete)
        {
            _rootContainer = rootContainer;
            _onComplete = onComplete;
            
            AddToClassList("yucp-onboarding-overlay");
            pickingMode = PickingMode.Ignore; // Let clicks pass through to dimmer parts
            
            // Create dimmer elements (4 rectangles around the spotlight)
            _dimmerTop = CreateDimmer();
            _dimmerBottom = CreateDimmer();
            _dimmerLeft = CreateDimmer();
            _dimmerRight = CreateDimmer();
            
            Add(_dimmerTop);
            Add(_dimmerBottom);
            Add(_dimmerLeft);
            Add(_dimmerRight);
            
            // Spotlight border (visual ring)
            _spotlightBorder = new VisualElement();
            _spotlightBorder.AddToClassList("yucp-onboarding-spotlight-border");
            _spotlightBorder.pickingMode = PickingMode.Ignore;
            Add(_spotlightBorder);
            
            // Content Card
            _contentCard = CreateContentCard();
            Add(_contentCard);
            
            // Register update loop for animations
            schedule.Execute(UpdateAnimation).Every(10);
            
            // Register geometry change to handle window resizing
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }
        
        private VisualElement CreateDimmer()
        {
            var el = new VisualElement();
            el.AddToClassList("yucp-onboarding-dimmer");
            // Block clicks on the dimmed areas
            el.pickingMode = PickingMode.Position; 
            // Optional: Close on click background? For now, no.
            return el;
        }
        
        private VisualElement CreateContentCard()
        {
            var card = new VisualElement();
            card.AddToClassList("yucp-onboarding-card");
            
            // Header
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom = 10;
            
            _stepTitle = new Label();
            _stepTitle.AddToClassList("yucp-onboarding-title");
            header.Add(_stepTitle);
            
            _stepCounter = new Label();
            _stepCounter.AddToClassList("yucp-onboarding-counter");
            header.Add(_stepCounter);
            
            card.Add(header);
            
            // Body
            _stepDescription = new Label();
            _stepDescription.AddToClassList("yucp-onboarding-description");
            _stepDescription.style.whiteSpace = WhiteSpace.Normal;
            card.Add(_stepDescription);
            
            // Footer (Buttons)
            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.FlexEnd;
            footer.style.marginTop = 15;
            
            _skipButton = new Button(EndTour);
            _skipButton.text = "Skip";
            _skipButton.AddToClassList("yucp-button-text");
            _skipButton.style.marginRight = Length.Auto(); // Push to left
            footer.Add(_skipButton);
            
            _prevButton = new Button(PreviousStep);
            _prevButton.text = "Back";
            _prevButton.AddToClassList("yucp-button");
            _prevButton.AddToClassList("yucp-button-small");
            _prevButton.style.marginRight = 5;
            footer.Add(_prevButton);
            
            _nextButton = new Button(NextStep);
            _nextButton.text = "Next";
            _nextButton.AddToClassList("yucp-button");
            _nextButton.AddToClassList("yucp-button-primary");
            footer.Add(_nextButton);
            
            card.Add(footer);
            
            return card;
        }
        
        public void Start(List<OnboardingStep> steps)
        {
            _steps = steps;
            _currentStepIndex = 0;
            _currentSpotlightRect = new Rect(0, 0, _rootContainer.resolvedStyle.width, _rootContainer.resolvedStyle.height);
            ShowStep(_currentStepIndex);
        }
        
        private void ShowStep(int index)
        {
            if (index < 0 || index >= _steps.Count) return;
            
            var step = _steps[index];
            
            // Invoke OnStepShown callback (e.g., to open sidebar in compact mode)
            step.OnStepShown?.Invoke();
            
            _stepTitle.text = ParseRichText(step.Title);
            _stepDescription.text = ParseRichText(step.Description);
            _stepCounter.text = $"{index + 1} / {_steps.Count}";
            
            _prevButton.SetEnabled(index > 0);
            _nextButton.text = (index == _steps.Count - 1) ? "Finish" : "Next";
            
            // Enable rich text for labels
            _stepTitle.enableRichText = true;
            _stepDescription.enableRichText = true;
            
            // If this step requires layout delay (e.g., sidebar opening animation),
            // wait before trying to resolve the target element
            int initialDelay = step.RequiresLayoutDelay ? 350 : 0;
            
            schedule.Execute(() => ResolveAndShowTarget(step, index)).ExecuteLater(initialDelay);
        }
        
        private void ResolveAndShowTarget(OnboardingStep step, int index)
        {
            // Find target
            Rect targetWorldRect = Rect.zero;
            var targetElement = ResolveTargetElement(step);
            
            if (targetElement != null)
            {
                // Delay scrolling to ensure layout is complete, especially for steps 5-9 which are in scrollable sections
                // Use a longer delay for later steps that are deeper in the UI hierarchy
                int delayMs = index >= 5 ? 200 : 100;
                
                schedule.Execute(() =>
                {
                    // Retry resolving the element in case it wasn't ready before
                    if (!IsUsableTarget(targetElement))
                    {
                        targetElement = ResolveTargetElement(step);
                    }
                    
                    if (targetElement != null && IsUsableTarget(targetElement))
                    {
                        EnsureElementVisible(targetElement);
                        ScheduleSpotlightUpdate(targetElement, step);
                    }
                    else
                    {
                        // Retry once more if element still not found
                        schedule.Execute(() =>
                        {
                            targetElement = ResolveTargetElement(step);
                            if (targetElement != null && IsUsableTarget(targetElement))
                            {
                                EnsureElementVisible(targetElement);
                                ScheduleSpotlightUpdate(targetElement, step);
                            }
                        }).ExecuteLater(200);
                    }
                }).ExecuteLater(delayMs);
                
                return;
            }

            if (targetWorldRect.width == 0 || targetWorldRect.height == 0)
            {
                // Center screen fallback if no target
                float w = 400;
                float h = 200;
                float x = (_rootContainer.resolvedStyle.width - w) / 2;
                float y = (_rootContainer.resolvedStyle.height - h) / 2;
                targetWorldRect = new Rect(x, y, w, h);
            }
            else
            {                
                // Add padding
                targetWorldRect.x -= step.SpotlightPadding.x;
                targetWorldRect.y -= step.SpotlightPadding.y;
                targetWorldRect.width += step.SpotlightPadding.x + step.SpotlightPadding.z;
                targetWorldRect.height += step.SpotlightPadding.y + step.SpotlightPadding.w;
            }
            
            // Handle specific "no spotlight" case (e.g. welcome screen)
            if (string.IsNullOrEmpty(step.TargetElementName) && step.TargetElement == null)
            {
               targetWorldRect = Rect.zero; 
            }
            
            _targetSpotlightRect = targetWorldRect;
            
            StartAnimation(targetWorldRect);
        }
        
        private void StartAnimation(Rect target)
        {
            _isAnimating = true;
            _animationProgress = 0f;
            _lastTime = EditorApplication.timeSinceStartup;
        }
        
        private void UpdateAnimation()
        {
            if (!_isAnimating) 
            {
                 // Even if not animating, ensure layout is correct in case of resize
                 UpdateLayout(_targetSpotlightRect);
                 return;
            }
            
            double currentTime = EditorApplication.timeSinceStartup;
            float dt = (float)(currentTime - _lastTime);
            _lastTime = currentTime;
            
            _animationProgress += dt / ANIMATION_DURATION;
            
            if (_animationProgress >= 1f)
            {
                _animationProgress = 1f;
                _isAnimating = false;
                _currentSpotlightRect = _targetSpotlightRect;
            }
            else
            {
                // Lerp rect
                float t = Mathf.SmoothStep(0, 1, _animationProgress);
                _currentSpotlightRect = new Rect(
                    Mathf.Lerp(_currentSpotlightRect.x, _targetSpotlightRect.x, t),
                    Mathf.Lerp(_currentSpotlightRect.y, _targetSpotlightRect.y, t),
                    Mathf.Lerp(_currentSpotlightRect.width, _targetSpotlightRect.width, t),
                    Mathf.Lerp(_currentSpotlightRect.height, _targetSpotlightRect.height, t)
                );
            }
            
            UpdateLayout(_currentSpotlightRect);
        }
        
        private void UpdateLayout(Rect spotlight)
        {
            float w = resolvedStyle.width;
            float h = resolvedStyle.height;
            
            if (float.IsNaN(w) || w <= 0) w = 800; // Fallback
            if (float.IsNaN(h) || h <= 0) h = 600;
            
            if (spotlight.width <= 1 || spotlight.height <= 1)
            {
                // Full dim mode (no spotlight)
                _dimmerTop.style.top = 0;
                _dimmerTop.style.left = 0;
                _dimmerTop.style.width = w;
                _dimmerTop.style.height = h;
                
                _dimmerBottom.style.display = DisplayStyle.None;
                _dimmerLeft.style.display = DisplayStyle.None;
                _dimmerRight.style.display = DisplayStyle.None;
                _spotlightBorder.style.display = DisplayStyle.None;
                
                // Center card
                _contentCard.style.left = (w - _contentCard.resolvedStyle.width) / 2;
                _contentCard.style.top = (h - _contentCard.resolvedStyle.height) / 2;
                
                return;
            }
            
            _dimmerBottom.style.display = DisplayStyle.Flex;
            _dimmerLeft.style.display = DisplayStyle.Flex;
            _dimmerRight.style.display = DisplayStyle.Flex;
            _spotlightBorder.style.display = DisplayStyle.Flex;
            
            // Top rect
            _dimmerTop.style.top = 0;
            _dimmerTop.style.left = 0;
            _dimmerTop.style.width = w;
            _dimmerTop.style.height = spotlight.y;
            
            // Bottom rect
            _dimmerBottom.style.top = spotlight.yMax;
            _dimmerBottom.style.left = 0;
            _dimmerBottom.style.width = w;
            _dimmerBottom.style.height = h - spotlight.yMax;
            
            // Left rect
            _dimmerLeft.style.top = spotlight.y;
            _dimmerLeft.style.left = 0;
            _dimmerLeft.style.width = spotlight.x;
            _dimmerLeft.style.height = spotlight.height;
            
            // Right rect
            _dimmerRight.style.top = spotlight.y;
            _dimmerRight.style.left = spotlight.xMax;
            _dimmerRight.style.width = w - spotlight.xMax;
            _dimmerRight.style.height = spotlight.height;
            
            // Spotlight border
            _spotlightBorder.style.top = spotlight.y;
            _spotlightBorder.style.left = spotlight.x;
            _spotlightBorder.style.width = spotlight.width;
            _spotlightBorder.style.height = spotlight.height;
            
            // Position Content Card - Smart Positioning            
            float cardW = _contentCard.resolvedStyle.width;
            float cardH = _contentCard.resolvedStyle.height;
            if (float.IsNaN(cardW)) cardW = 350; // layout default
            if (float.IsNaN(cardH)) cardH = 150;
            
            float gap = 20;
            
            // Try Right
            bool fitsRight = (spotlight.xMax + gap + cardW) < w;
            bool fitsLeft = (spotlight.x - gap - cardW) > 0;
            bool fitsBottom = (spotlight.yMax + gap + cardH) < h;
            bool fitsTop = (spotlight.y - gap - cardH) > 0;
            
            if (fitsRight)
            {
                _contentCard.style.left = spotlight.xMax + gap;
                _contentCard.style.top = spotlight.center.y - (cardH / 2);
            }
            else if (fitsLeft)
            {
                _contentCard.style.left = spotlight.x - gap - cardW;
                _contentCard.style.top = spotlight.center.y - (cardH / 2);
            }
            else if (fitsBottom)
            {
                _contentCard.style.left = spotlight.center.x - (cardW / 2);
                _contentCard.style.top = spotlight.yMax + gap;
            }
            else if (fitsTop)
            {
                _contentCard.style.left = spotlight.center.x - (cardW / 2);
                _contentCard.style.top = spotlight.y - gap - cardH;
            }
            else
            {
                // Center screen if no fit
                _contentCard.style.left = (w - cardW) / 2;
                _contentCard.style.top = (h - cardH) / 2;
            }
            
            // Clamp to screen bounds
            float currentLeft = _contentCard.style.left.value.value;
            float currentTop = _contentCard.style.top.value.value;
            
            if (currentLeft < 10) _contentCard.style.left = 10;
            if (currentTop < 10) _contentCard.style.top = 10;
            if (currentLeft + cardW > w - 10) _contentCard.style.left = w - 10 - cardW;
            if (currentTop + cardH > h - 10) _contentCard.style.top = h - 10 - cardH;
        }
        
        private void NextStep()
        {
            // Hide current step (invoke OnStepHidden to close sidebar if needed)
            if (_currentStepIndex >= 0 && _currentStepIndex < _steps.Count)
            {
                _steps[_currentStepIndex].OnStepHidden?.Invoke();
            }
            
            if (_currentStepIndex < _steps.Count - 1)
            {
                _currentStepIndex++;
                ShowStep(_currentStepIndex);
            }
            else
            {
                EndTour();
            }
        }
        
        private void PreviousStep()
        {
            // Hide current step (invoke OnStepHidden to close sidebar if needed)
            if (_currentStepIndex >= 0 && _currentStepIndex < _steps.Count)
            {
                _steps[_currentStepIndex].OnStepHidden?.Invoke();
            }
            
            if (_currentStepIndex > 0)
            {
                _currentStepIndex--;
                ShowStep(_currentStepIndex);
            }
        }
        
        private void EndTour()
        {
            // Hide current step (invoke OnStepHidden to close sidebar if needed)
            if (_currentStepIndex >= 0 && _currentStepIndex < _steps.Count)
            {
                _steps[_currentStepIndex].OnStepHidden?.Invoke();
            }
            
            // Update EditorPrefs
            EditorPrefs.SetBool("com.yucp.devtools.packageexporter.onboarding.shown", true);
            
            // Fade out
            this.style.opacity = 0;
            this.schedule.Execute(() => 
            {
                RemoveFromHierarchy();
                _onComplete?.Invoke();
            }).StartingIn(300);
        }
        
        private int _scrollRetryCount = 0;
        private const int MaxScrollRetries = 10;
        
        private void EnsureElementVisible(VisualElement element)
        {
            if (element == null) return;
            
            _scrollRetryCount = 0; // Reset retry count for new element
            
            EnsureElementVisibleInternal(element);
        }
        
        private void EnsureElementVisibleInternal(VisualElement element)
        {
            if (element == null) return;
            
            _scrollRetryCount++;
            
            // Try to expand collapsible sections that might contain this element
            ExpandCollapsibleSections(element);
                        
            if (float.IsNaN(element.layout.height) || element.layout.height < 1 || 
                float.IsNaN(element.worldBound.height) || element.worldBound.height < 1)
            {
                if (_scrollRetryCount < MaxScrollRetries)
                {
                    schedule.Execute(() => EnsureElementVisibleInternal(element)).ExecuteLater(100);
                }
                return;
            }

            var scrollView = FindClosestScrollView(element);
            if (scrollView == null)
            {
                if (_scrollRetryCount < MaxScrollRetries)
                {
                    schedule.Execute(() => EnsureElementVisibleInternal(element)).ExecuteLater(100);
                }
                return;
            }
            
            if (scrollView.resolvedStyle.display == DisplayStyle.None || scrollView.style.visibility == Visibility.Hidden)
                return;
            
            // Ensure scroll view layout is valid
            if (float.IsNaN(scrollView.layout.height) || scrollView.layout.height < 1)
            {
                if (_scrollRetryCount < MaxScrollRetries)
                {
                    schedule.Execute(() => EnsureElementVisibleInternal(element)).ExecuteLater(100);
                }
                return;
            }
            
            // Ensure content container is ready
            var content = scrollView.contentContainer;
            if (content == null || float.IsNaN(content.layout.height))
            {
                if (_scrollRetryCount < MaxScrollRetries)
                {
                    schedule.Execute(() => EnsureElementVisibleInternal(element)).ExecuteLater(100);
                }
                return;
            }
            
            float targetOffset = GetScrollOffsetForElement(scrollView, element);
            
            // Try immediate scroll first as a test
            if (!float.IsNaN(targetOffset))
            {
                scrollView.verticalScroller.value = targetOffset;
            }
            
            // Then do smooth scroll
            SmoothScroll(scrollView, targetOffset);
        }
        
        private void ExpandCollapsibleSections(VisualElement element)
        {
            // Look for collapsible headers (buttons with collapse/expand indicators) near the element
            // Find parent section and check if it has a collapsible header
            var current = element;
            while (current != null && current != _rootContainer)
            {
                // Look for section containers
                if (current.ClassListContains("yucp-section"))
                {
                    // Find toggle button in this section - look in header
                    var header = current.Q(className: "yucp-section-header");
                    if (header == null)
                    {
                        // Try finding any button in the section
                        header = current;
                    }
                    
                    var toggleButton = header.Q<Button>();
                    if (toggleButton != null && toggleButton.text != null)
                    {
                        // If button shows "▶" (collapsed), click it to expand
                        string buttonText = toggleButton.text;
                        if (buttonText.Contains("▶") || buttonText == "▶")
                        {
                            // Click to expand - simulate a click event
                            using (var clickEvent = ClickEvent.GetPooled())
                            {
                                clickEvent.target = toggleButton;
                                toggleButton.SendEvent(clickEvent);
                            }
                            // Force a layout update
                            current.MarkDirtyRepaint();
                            schedule.Execute(() => 
                            {
                                // Wait for layout to update after expansion
                                current.MarkDirtyRepaint();
                            }).ExecuteLater(100);
                        }
                    }
                    break;
                }
                current = current.hierarchy.parent;
            }
        }

        private float GetScrollOffsetForElement(ScrollView scrollView, VisualElement element)
        {
            var content = scrollView.contentContainer;
            
            // Use WorldToLocal to get element's position in content container's local space
            Vector2 elementLocalPos;
            try
            {
                elementLocalPos = content.WorldToLocal(element.worldBound.position);
            }
            catch
            {
                // Fallback: calculate manually
                Rect elementWorld = element.worldBound;
                Rect contentWorld = content.worldBound;
                elementLocalPos = new Vector2(
                    elementWorld.x - contentWorld.x,
                    elementWorld.y - contentWorld.y
                );
            }
            
            // elementLocalPos.y is the element's Y position within the content container
            // This is what we need for scrolling
            float elementYInContent = elementLocalPos.y;
            
            // If the calculation seems wrong (negative or way too large), try alternative
            if (elementYInContent < 0 || elementYInContent > content.layout.height * 2)
            {
                // Alternative: traverse up the hierarchy and sum layout.y
                float sumY = 0f;
                VisualElement current = element;
                while (current != null && current != content)
                {
                    if (!float.IsNaN(current.layout.y))
                    {
                        sumY += current.layout.y;
                    }
                    current = current.hierarchy.parent;
                }
                if (sumY > 0)
                {
                    elementYInContent = sumY;
                }
            }
            
            float viewportHeight = scrollView.layout.height;
            float elementHeight = element.layout.height;
            
            // Try to center the element, but with some padding from top
            // This ensures the element is visible and not cut off
            float padding = 20f; // Padding from top of viewport
            float target = elementYInContent - padding;
            
            // If element is tall, try to show its top
            if (elementHeight > viewportHeight * 0.8f)
            {
                target = elementYInContent;
            }
            else
            {
                // Center smaller elements
                float elementCenterY = elementYInContent + (elementHeight / 2f);
                float viewportCenterY = viewportHeight / 2f;
                target = elementCenterY - viewportCenterY;
            }
            
            // Clamp to valid scroll range
            float maxScroll = scrollView.verticalScroller.highValue;
            if (float.IsNaN(target) || target < 0) target = 0f;
            if (target > maxScroll) target = maxScroll;
            
            return target;
        }

        private void SmoothScroll(ScrollView scrollView, float targetOffset)
        {
            if (scrollView == null) return;
            
            float startOffset = scrollView.verticalScroller.value;
            
            // Ensure target is valid
            if (float.IsNaN(targetOffset)) return;
            
            // If the difference is very small, just set it directly
            if (Mathf.Abs(targetOffset - startOffset) < 1f)
            {
                scrollView.verticalScroller.value = targetOffset;
                return;
            }
            
            float duration = 0.4f;
            double startTime = EditorApplication.timeSinceStartup;
            
            IVisualElementScheduledItem anim = null;
            anim = scrollView.schedule.Execute(() =>
            {
                if (scrollView == null || scrollView.verticalScroller == null) return;
                
                float t = (float)(EditorApplication.timeSinceStartup - startTime) / duration;
                if (t >= 1f)
                {
                    scrollView.verticalScroller.value = targetOffset;
                    return; // Done
                }
                
                float smoothT = Mathf.SmoothStep(0, 1, t);
                float currentValue = Mathf.Lerp(startOffset, targetOffset, smoothT);
                scrollView.verticalScroller.value = currentValue;
                
                // Continue animation
                anim.ExecuteLater(10);
            });
            
            // Also set directly after a short delay as a fallback
            scrollView.schedule.Execute(() =>
            {
                if (scrollView != null && scrollView.verticalScroller != null)
                {
                    scrollView.verticalScroller.value = targetOffset;
                }
            }).ExecuteLater((int)(duration * 1000) + 50);
        }

        private VisualElement ResolveTargetElement(OnboardingStep step)
        {
            if (step.TargetElement != null && IsUsableTarget(step.TargetElement))
                return step.TargetElement;
            
            if (string.IsNullOrEmpty(step.TargetElementName))
                return null;
            
            var matches = _rootContainer.Query<VisualElement>().Where(e => e.name == step.TargetElementName).ToList();
            if (matches.Count == 0) return null;
            
            VisualElement best = null;
            float bestArea = 0f;
            foreach (var el in matches)
            {
                if (!IsUsableTarget(el)) continue;
                float area = el.worldBound.width * el.worldBound.height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = el;
                }
            }
            
            return best ?? matches.FirstOrDefault(IsUsableTarget);
        }

        private bool IsUsableTarget(VisualElement element)
        {
            if (element == null || element.panel == null) return false;
            if (element.resolvedStyle.display == DisplayStyle.None) return false;
            if (element.resolvedStyle.visibility == Visibility.Hidden) return false;
            if (element.worldBound.width < 2 || element.worldBound.height < 2) return false;
            return true;
        }

        private ScrollView FindClosestScrollView(VisualElement element)
        {
            // First, try to find the right pane scroll view directly (most reliable for steps 5-9)
            var rightPaneScrollView = _rootContainer.Q<ScrollView>(className: "yucp-scrollview");
            if (rightPaneScrollView != null)
            {
                // Verify the element is actually inside this scroll view
                if (IsElementInScrollView(element, rightPaneScrollView))
                {
                    return rightPaneScrollView;
                }
            }
            
            // Fallback: find closest scroll view by traversing up
            var parent = element.hierarchy.parent;
            while (parent != null)
            {
                if (parent is ScrollView scrollView)
                    return scrollView;
                parent = parent.hierarchy.parent;
            }
            
            return null;
        }
        
        private bool IsElementInScrollView(VisualElement element, ScrollView scrollView)
        {
            if (element == null || scrollView == null) return false;
            
            var content = scrollView.contentContainer;
            var current = element;
            while (current != null)
            {
                if (current == content || current == scrollView)
                    return true;
                current = current.hierarchy.parent;
            }
            return false;
        }

        private void ScheduleSpotlightUpdate(VisualElement targetElement, OnboardingStep step)
        {
            if (targetElement == null) return;
            
            schedule.Execute(() =>
            {
                if (!IsUsableTarget(targetElement))
                {
                    ScheduleSpotlightUpdate(targetElement, step);
                    return;
                }
                
                Rect r = targetElement.worldBound;
                
                Rect overlayWorldBound = this.worldBound;
                r.x -= overlayWorldBound.x;
                r.y -= overlayWorldBound.y;
                
                r.x -= step.SpotlightPadding.x;
                r.y -= step.SpotlightPadding.y;
                r.width += step.SpotlightPadding.x + step.SpotlightPadding.z;
                r.height += step.SpotlightPadding.y + step.SpotlightPadding.w;
                
                _targetSpotlightRect = r;
                StartAnimation(r);
            }).ExecuteLater(160);
        }

        private string ParseRichText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // Replace **bold** with <b>bold</b>
            return System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.*?)\*\*", "<b>$1</b>");
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // Force update layout on resize
            UpdateLayout(_isAnimating ? _currentSpotlightRect : _targetSpotlightRect);
        }
    }
}
