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
        private void StartGifAnimation(VisualElement element, string gifPath)
        {
            if (string.IsNullOrEmpty(gifPath) || element == null)
                return;
                
            // Stop any existing animation for this element
            if (animatedGifs.ContainsKey(gifPath))
            {
                var existing = animatedGifs[gifPath];
                if (existing.targetElement == element && existing.isAnimating)
                {
                    return; // Already animating
                }
                else
                {
                    // Clean up old animation
                    existing.isAnimating = false;
                    animatedGifs.Remove(gifPath);
                }
            }
            
            // Try to extract frames from GIF file
            // Note: Unity's Texture2D.LoadImage only loads the first frame
            // For full animation, we need to parse the GIF file manually
            try
            {
                byte[] gifData = File.ReadAllBytes(gifPath);
                var frames = ExtractGifFrames(gifData);
                
                if (frames != null && frames.Count > 1)
                {
                    var gifData_obj = new AnimatedGifData
                    {
                        frames = frames,
                        frameDelays = ExtractGifDelays(gifData),
                        currentFrame = 0,
                        timeSinceLastFrame = 0f,
                        targetElement = element,
                        gifPath = gifPath,
                        isAnimating = true
                    };
                    
                    // Default delay if extraction fails
                    if (gifData_obj.frameDelays.Count == 0)
                    {
                        for (int i = 0; i < frames.Count; i++)
                        {
                            gifData_obj.frameDelays.Add(0.1f); // 100ms default
                        }
                    }
                    
                    animatedGifs[gifPath] = gifData_obj;
                    element.style.backgroundImage = new StyleBackground(frames[0]);
                    
                    // Start animation update loop
                    if (!EditorApplication.update.GetInvocationList().Contains(new System.Action(UpdateGifAnimations)))
                    {
                        EditorApplication.update += UpdateGifAnimations;
                    }
                }
                else
                {
                    // Single frame or extraction failed, just display first frame
                    Texture2D firstFrame = AssetDatabase.LoadAssetAtPath<Texture2D>(gifPath);
                    if (firstFrame != null)
                    {
                        element.style.backgroundImage = new StyleBackground(firstFrame);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Package Exporter] Failed to load animated GIF at {gifPath}: {ex.Message}");
                // Fallback to first frame
                Texture2D firstFrame = AssetDatabase.LoadAssetAtPath<Texture2D>(gifPath);
                if (firstFrame != null)
                {
                    element.style.backgroundImage = new StyleBackground(firstFrame);
                }
            }
        }

        private void UpdateGifAnimations()
        {
            if (animatedGifs.Count == 0)
            {
                EditorApplication.update -= UpdateGifAnimations;
                return;
            }
            
            float currentTime = (float)EditorApplication.timeSinceStartup;
            float deltaTime = currentTime - (lastGifUpdateTime > 0 ? lastGifUpdateTime : currentTime);
            lastGifUpdateTime = currentTime;
            
            var keysToRemove = new List<string>();
            foreach (var kvp in animatedGifs.ToList())
            {
                var gif = kvp.Value;
                if (!gif.isAnimating || gif.targetElement == null || gif.frames.Count == 0)
                {
                    keysToRemove.Add(kvp.Key);
                    continue;
                }
                
                gif.timeSinceLastFrame += deltaTime;
                
                if (gif.timeSinceLastFrame >= gif.frameDelays[gif.currentFrame])
                {
                    gif.currentFrame = (gif.currentFrame + 1) % gif.frames.Count;
                    gif.timeSinceLastFrame = 0f;
                    
                    if (gif.targetElement != null && gif.frames[gif.currentFrame] != null)
                    {
                        gif.targetElement.style.backgroundImage = new StyleBackground(gif.frames[gif.currentFrame]);
                    }
                }
            }
            
            foreach (var key in keysToRemove)
            {
                animatedGifs.Remove(key);
            }
        }

        private List<Texture2D> ExtractGifFrames(byte[] gifData)
        {
            var frames = new List<Texture2D>();
            
            // Unity's Texture2D.LoadImage can only load the first frame
            // For full GIF support, you would need a proper GIF decoder library
            // This is a basic implementation that attempts to extract frames
            
            try
            {
                // Try to load first frame using Unity's built-in loader
                Texture2D firstFrame = new Texture2D(2, 2);
                if (firstFrame.LoadImage(gifData))
                {
                    frames.Add(firstFrame);
                }
                
                // For additional frames, we would need to parse the GIF file structure
                // This requires implementing a GIF decoder or using a library
                // For now, we'll return just the first frame
                // A proper implementation would:
                // 1. Parse GIF header
                // 2. Extract each frame's image data
                // 3. Decompress using LZW
                // 4. Convert to Texture2D
                
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Package Exporter] Failed to extract GIF frames: {ex.Message}");
            }
            
            return frames;
        }

        private List<float> ExtractGifDelays(byte[] gifData)
        {
            var delays = new List<float>();
            
            // Extract frame delays from GIF file
            // This requires parsing the GIF file structure
            // For now, return empty list (will use default delays)
            
            return delays;
        }

        private class AnimatedGifData
        {
            public List<Texture2D> frames = new List<Texture2D>();
            public List<float> frameDelays = new List<float>(); // Delay in seconds
            public int currentFrame = 0;
            public float timeSinceLastFrame = 0f;
            public VisualElement targetElement;
            public string gifPath;
            public bool isAnimating = false;
        }

    }
}
