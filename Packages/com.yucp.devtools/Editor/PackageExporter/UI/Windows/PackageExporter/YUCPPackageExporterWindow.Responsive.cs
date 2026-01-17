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
        private void OnGUI()
        {
            // Handle cursor changes for resize handle
            // Use the stored rect instead of worldBound to avoid coordinate issues
            if (!currentResizeRect.Equals(Rect.zero) && !isResizingInspector)
            {
                EditorGUIUtility.AddCursorRect(currentResizeRect, MouseCursor.ResizeVertical);
            }
            
            // Handle cursor for left pane resize handle
            if (!currentLeftPaneResizeRect.Equals(Rect.zero) && !isResizingLeftPane)
            {
                EditorGUIUtility.AddCursorRect(currentLeftPaneResizeRect, MouseCursor.ResizeHorizontal);
            }
        }

        private void EndResize()
        {
            if (!isResizingLeftPane) return;
            
            isResizingLeftPane = false;
            if (_resizeHandle != null && _resizeHandle.HasMouseCapture())
            {
                _resizeHandle.ReleaseMouse();
            }
            
            // Save the new width to EditorPrefs
            if (_leftPane != null)
            {
                float finalWidth = _leftPane.resolvedStyle.width;
                if (finalWidth <= 0)
                {
                    finalWidth = _leftPane.layout.width;
                }
                if (finalWidth > 0)
                {
                    EditorPrefs.SetFloat(LeftPaneWidthKey, finalWidth);
                }
            }
            
            currentLeftPaneResizeRect = Rect.zero;
        }

        private void SetupLeftPaneResizeHandle()
        {
            if (_resizeHandle == null || _leftPane == null) return;
            
            // Update resize rect for cursor when mouse moves over handle
            _resizeHandle.RegisterCallback<MouseEnterEvent>(evt =>
            {
                if (!isResizingLeftPane)
                {
                    var worldBounds = _resizeHandle.worldBound;
                    currentLeftPaneResizeRect = worldBounds;
                }
            });
            
            _resizeHandle.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                if (!isResizingLeftPane)
                {
                    currentLeftPaneResizeRect = Rect.zero;
                }
            });
            
            // Start resizing on mouse down
            _resizeHandle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0) // Left mouse button
                {
                    isResizingLeftPane = true;
                    // Use world position for consistent tracking
                    var paneWorldBound = _leftPane.worldBound;
                    resizeStartX = evt.mousePosition.x;
                    float currentWidth = _leftPane.resolvedStyle.width;
                    if (currentWidth <= 0)
                    {
                        currentWidth = _leftPane.layout.width;
                    }
                    if (currentWidth <= 0 && paneWorldBound.width > 0)
                    {
                        currentWidth = paneWorldBound.width;
                    }
                    resizeStartWidth = currentWidth;
                    _resizeHandle.CaptureMouse();
                    evt.StopPropagation();
                }
            });
            
            // Update resize width - helper method for responsiveness
            System.Action<Vector2> updateResizeWidth = (mousePosition) =>
            {
                if (!isResizingLeftPane || !_resizeHandle.HasMouseCapture()) return;
                
                float deltaX = mousePosition.x - resizeStartX;
                float newWidth = resizeStartWidth + deltaX;
                newWidth = Mathf.Clamp(newWidth, MinLeftPaneWidth, MaxLeftPaneWidth);
                
                // Update left pane width immediately
                _leftPane.style.width = new Length(newWidth, LengthUnit.Pixel);
                _leftPane.style.flexShrink = 0;
                _leftPane.style.flexGrow = 0;
                
                // Force immediate repaint updates (layout will be recalculated automatically)
                _leftPane.MarkDirtyRepaint();
                
                // Also mark parent container for repaint
                if (_contentContainer != null)
                {
                    _contentContainer.MarkDirtyRepaint();
                }
                
                // Force immediate repaint of the window
                Repaint();
            };
            
            // Handle mouse move during resize - using both local and capture events for best tracking
            _resizeHandle.RegisterCallback<MouseMoveEvent>(evt =>
            {
                if (isResizingLeftPane && _resizeHandle.HasMouseCapture())
                {
                    updateResizeWidth(evt.mousePosition);
                    evt.StopPropagation();
                }
                else if (!isResizingLeftPane)
                {
                    // Update resize rect when hovering
                    var worldBounds = _resizeHandle.worldBound;
                    if (!worldBounds.Equals(Rect.zero))
                    {
                        currentLeftPaneResizeRect = worldBounds;
                    }
                }
            });
            
            // Also register on root for better tracking when mouse leaves the handle
            if (rootVisualElement != null)
            {
                rootVisualElement.RegisterCallback<MouseMoveEvent>(evt =>
                {
                    if (isResizingLeftPane && _resizeHandle.HasMouseCapture())
                    {
                        updateResizeWidth(evt.mousePosition);
                        evt.StopPropagation();
                    }
                }, TrickleDown.TrickleDown);
            }
            
            // End resizing on mouse up
            _resizeHandle.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (evt.button == 0 && isResizingLeftPane)
                {
                    EndResize();
                    evt.StopPropagation();
                }
            });
            
            // Also handle mouse up on the root to ensure we release mouse capture even if mouse leaves handle
            if (rootVisualElement != null)
            {
                rootVisualElement.RegisterCallback<MouseUpEvent>(evt =>
                {
                    if (evt.button == 0 && isResizingLeftPane)
                    {
                        EndResize();
                    }
                });
            }
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            UpdateResponsiveLayout(evt.newRect.width);
        }

        private void UpdateResponsiveLayout(float width)
        {
            var root = rootVisualElement;
            
            // Remove all responsive classes first
            root.RemoveFromClassList("yucp-window-narrow");
            root.RemoveFromClassList("yucp-window-medium");
            root.RemoveFromClassList("yucp-window-wide");
            
            // Apply appropriate class
            if (width < 700f)
            {
                root.AddToClassList("yucp-window-narrow");
            }
            else if (width < 900f)
            {
                root.AddToClassList("yucp-window-medium");
            }
            else
            {
                root.AddToClassList("yucp-window-wide");
            }
            
            // Close overlay if window is wide enough
            if (width >= 700f && _isOverlayOpen)
            {
                CloseOverlay();
            }
        }

        private void ToggleOverlay()
        {
            if (_isOverlayOpen)
            {
                CloseOverlay();
            }
            else
            {
                OpenOverlay();
            }
        }

        private void OpenOverlay()
        {
            _isOverlayOpen = true;
            
            // Show backdrop first
            if (_overlayBackdrop != null)
            {
                _overlayBackdrop.style.display = DisplayStyle.Flex;
                _overlayBackdrop.style.visibility = Visibility.Visible;
                _overlayBackdrop.style.position = Position.Absolute;
                _overlayBackdrop.style.left = 0;
                _overlayBackdrop.style.right = 0;
                _overlayBackdrop.style.top = 0;
                _overlayBackdrop.style.bottom = 0;
                _overlayBackdrop.style.opacity = 0;
                _overlayBackdrop.BringToFront();
                
                // Fade in backdrop
                _overlayBackdrop.schedule.Execute(() => 
                {
                    if (_overlayBackdrop != null)
                    {
                        _overlayBackdrop.style.opacity = 1;
                    }
                }).StartingIn(10);
            }
            
            // Show overlay
            if (_leftPaneOverlay != null)
            {
                // Force dimensions and positioning with inline styles
                _leftPaneOverlay.style.display = DisplayStyle.Flex;
                _leftPaneOverlay.style.visibility = Visibility.Visible;
                _leftPaneOverlay.style.position = Position.Absolute;
                _leftPaneOverlay.style.width = 270;
                _leftPaneOverlay.style.top = 0;
                _leftPaneOverlay.style.bottom = 0;
                _leftPaneOverlay.style.left = -270;
                _leftPaneOverlay.style.opacity = 0;
                
                // Ensure it's in front
                _leftPaneOverlay.BringToFront();
                
                // Animate to visible position
                _leftPaneOverlay.schedule.Execute(() => 
                {
                    if (_leftPaneOverlay != null)
                    {
                        _leftPaneOverlay.style.left = 0;
                        _leftPaneOverlay.style.opacity = 1;
                    }
                }).StartingIn(10);
            }
        }

        private void CloseOverlay()
        {
            _isOverlayOpen = false;
            
            // Animate overlay out
            if (_leftPaneOverlay != null)
            {
                _leftPaneOverlay.style.left = -270;
                _leftPaneOverlay.style.opacity = 0;
            }
            
            // Fade out backdrop
            if (_overlayBackdrop != null)
            {
                _overlayBackdrop.style.opacity = 0;
            }
            
            // Hide after animation completes (300ms)
            rootVisualElement.schedule.Execute(() => 
            {
                if (_leftPaneOverlay != null && !_isOverlayOpen)
                {
                    _leftPaneOverlay.style.display = DisplayStyle.None;
                    _leftPaneOverlay.style.visibility = Visibility.Hidden;
                }
                if (_overlayBackdrop != null && !_isOverlayOpen)
                {
                    _overlayBackdrop.style.display = DisplayStyle.None;
                    _overlayBackdrop.style.visibility = Visibility.Hidden;
                }
            }).StartingIn(300);
        }

    }
}
