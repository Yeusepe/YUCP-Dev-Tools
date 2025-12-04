using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace YUCP.DevTools.Editor.TextureArrays
{
    public sealed class TextureArrayBuilderWindow : EditorWindow
    {
        private enum SourceMode
        {
            Folder,
            ManualList,
            AnimatedGif,
            VideoClip
        }

        private static readonly TextureFormat[] SupportedFormats =
        {
            TextureFormat.DXT1,
            TextureFormat.DXT5,
            TextureFormat.BC7,
            TextureFormat.ETC2_RGBA8,
            TextureFormat.ASTC_6x6,
            TextureFormat.PVRTC_RGBA4,
            TextureFormat.RGBA32
        };

        private static readonly GUIContent[] SupportedFormatLabels =
        {
            new GUIContent("DXT1 (BC1) - 4bpp RGB, desktop, no alpha"),
            new GUIContent("DXT5 (BC3) - 8bpp RGBA, desktop"),
            new GUIContent("BC7 - 8bpp RGBA, high quality desktop"),
            new GUIContent("ETC2 RGBA8 - 4bpp RGBA, modern mobile"),
            new GUIContent("ASTC 6x6 - ~3.6bpp RGBA, high-end mobile"),
            new GUIContent("PVRTC RGBA4 - 4bpp RGBA, iOS"),
            new GUIContent("RGBA32 - 32bpp, uncompressed fallback")
        };

        private readonly List<Texture2D> _manualTextures = new List<Texture2D>();
        private readonly List<Texture2D> _previewTextures = new List<Texture2D>();
        private SourceMode _mode = SourceMode.Folder;
        private DefaultAsset _folderAsset;
        private Texture2D _gifAsset;
        private VideoClip _videoClip;
        private string _outputFolder = "Assets";
        private string _outputName = "TextureArray";
        private int _frameStep = 1;
        private int _maxFrames = 256;
        private int _targetWidth = 256;
        private int _targetHeight = 256;
        private bool _preserveAspect = true;
        private bool _generateMipMaps;
        private bool _linearColor;
        private int _formatIndex = 1; // Default to DXT5
        private Vector2 _scroll;

        [MenuItem("Tools/YUCP/Others/Development/Texture Array Builder")]
        private static void ShowWindow()
        {
            GetWindow<TextureArrayBuilderWindow>("Texture Array Builder").minSize = new Vector2(560, 440);
        }

        private void OnEnable()
        {
            Texture icon = ResolveWindowIcon();
            titleContent = icon != null
                ? new GUIContent("Texture Array Builder", icon)
                : new GUIContent("Texture Array Builder");
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            _mode = (SourceMode)EditorGUILayout.EnumPopup("Source Type", _mode);
            EditorGUILayout.Space();

            switch (_mode)
            {
                case SourceMode.Folder:
                    DrawFolderInput();
                    break;
                case SourceMode.ManualList:
                    DrawManualInput();
                    break;
                case SourceMode.AnimatedGif:
                    DrawGifInput();
                    break;
                case SourceMode.VideoClip:
                    DrawVideoInput();
                    break;
            }

            EditorGUILayout.Space();
            DrawProcessingSettings();
            EditorGUILayout.Space();
            DrawOutputSettings();
            EditorGUILayout.Space();
            DrawPreview();
            EditorGUILayout.Space();
            DrawBuildButton();
        }

        private void DrawFolderInput()
        {
            EditorGUILayout.LabelField("Image Sequence", EditorStyles.boldLabel);
            _folderAsset = (DefaultAsset)EditorGUILayout.ObjectField("Folder", _folderAsset, typeof(DefaultAsset), false);
            if (_folderAsset == null)
            {
                EditorGUILayout.HelpBox("Assign a folder containing PNG/JPG/Webp* frames. Files will be sorted lexicographically.", MessageType.Info);
            }
            else
            {
                if (GUILayout.Button("Scan Folder"))
                {
                    _manualTextures.Clear();
                    string path = AssetDatabase.GetAssetPath(_folderAsset);
                    string full = Path.GetFullPath(path);
                    var files = Directory.GetFiles(full)
                        .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => f)
                        .ToArray();
                    foreach (var file in files)
                    {
                        string assetPath = path + "/" + Path.GetFileName(file);
                        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                        if (tex != null)
                        {
                            _manualTextures.Add(tex);
                        }
                    }
                    RefreshPreview();
                }
                EditorGUILayout.LabelField($"Detected frames: {_manualTextures.Count}");
            }
        }

        private void DrawManualInput()
        {
            EditorGUILayout.LabelField("Manual Textures", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Drag Texture2D assets into the list below. Use reorderable list semantics (shift-click to remove).", MessageType.Info);
            int removeAt = -1;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(120));
            for (int i = 0; i < _manualTextures.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                _manualTextures[i] = (Texture2D)EditorGUILayout.ObjectField(_manualTextures[i], typeof(Texture2D), false);
                if (GUILayout.Button("Remove", GUILayout.Width(64)))
                    removeAt = i;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            if (removeAt >= 0)
            {
                _manualTextures.RemoveAt(removeAt);
            }

            if (GUILayout.Button("Add Texture"))
            {
                _manualTextures.Add(null);
            }

            if (GUILayout.Button("Refresh Preview"))
            {
                RefreshPreview();
            }

            EditorGUILayout.LabelField($"Frames: {_manualTextures.Count}");
        }

        private void DrawGifInput()
        {
            EditorGUILayout.LabelField("Animated GIF", EditorStyles.boldLabel);
            _gifAsset = (Texture2D)EditorGUILayout.ObjectField("GIF Asset", _gifAsset, typeof(Texture2D), false);
            EditorGUILayout.HelpBox("GIF decoding relies on System.Drawing; Windows Editor only. The GIF must be imported with Read/Write enabled.", MessageType.Warning);
            if (_gifAsset != null && GUILayout.Button("Decode Frames"))
            {
                DecodeGif();
            }
        }

        private void DrawVideoInput()
        {
            EditorGUILayout.LabelField("Video Clip", EditorStyles.boldLabel);
            _videoClip = (VideoClip)EditorGUILayout.ObjectField("Video Asset", _videoClip, typeof(VideoClip), false);
            _frameStep = EditorGUILayout.IntSlider("Frame Step", _frameStep, 1, 10);
            _maxFrames = EditorGUILayout.IntSlider("Max Frames", _maxFrames, 1, 512);

            if (_videoClip != null && GUILayout.Button("Sample Frames"))
            {
                SampleVideoFrames();
            }
            EditorGUILayout.HelpBox("Sampling uses VideoClipPlayable through VideoPlayer with RenderTexture capture; ensure codecs are supported by the Editor platform.", MessageType.Info);
        }

        private void DrawProcessingSettings()
        {
            EditorGUILayout.LabelField("Processing", EditorStyles.boldLabel);
            _targetWidth = EditorGUILayout.IntSlider("Target Width", _targetWidth, 32, 2048);
            _targetHeight = EditorGUILayout.IntSlider("Target Height", _targetHeight, 32, 2048);
            _preserveAspect = EditorGUILayout.Toggle("Preserve Aspect Ratio", _preserveAspect);
            _generateMipMaps = EditorGUILayout.Toggle("Generate MipMaps", _generateMipMaps);
            _linearColor = EditorGUILayout.Toggle("Linear Color Space", _linearColor);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Compression Format", EditorStyles.boldLabel);
            _formatIndex = EditorGUILayout.Popup(new GUIContent("Texture Format"), _formatIndex, SupportedFormatLabels);
            var selectedFormat = SupportedFormats[Mathf.Clamp(_formatIndex, 0, SupportedFormats.Length - 1)];
            EditorGUILayout.HelpBox(DescribeFormat(selectedFormat), MessageType.Info);
        }

        private void DrawOutputSettings()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            _outputFolder = EditorGUILayout.TextField("Asset Folder", _outputFolder);
            _outputName = EditorGUILayout.TextField("Asset Name", _outputName);
            if (GUILayout.Button("Select Folder"))
            {
                string selected = EditorUtility.OpenFolderPanel("Select Output Folder", _outputFolder, string.Empty);
                if (!string.IsNullOrEmpty(selected) && selected.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                {
                    _outputFolder = "Assets" + selected.Substring(Application.dataPath.Length);
                }
                else if (!string.IsNullOrEmpty(selected))
                {
                    EditorUtility.DisplayDialog("Invalid Folder", "Output folder must be inside the project Assets directory.", "Ok");
                }
            }
        }

        private void DrawPreview()
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            if (_previewTextures.Count == 0)
            {
                EditorGUILayout.HelpBox("No frames processed yet.", MessageType.Info);
                return;
            }

            int previewCount = Mathf.Min(6, _previewTextures.Count);
            EditorGUILayout.LabelField($"Showing {previewCount}/{_previewTextures.Count} frames");
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < previewCount; i++)
            {
                var tex = _previewTextures[i];
                GUILayout.Box(tex, GUILayout.Width(80), GUILayout.Height(80));
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBuildButton()
        {
            using (new EditorGUI.DisabledScope(_manualTextures.Count == 0))
            {
                if (GUILayout.Button("Build Texture Array", GUILayout.Height(40)))
                {
                    try
                    {
                        BuildTextureArray();
                        EditorUtility.DisplayDialog("Success", "Texture2DArray asset created successfully.", "OK");
                    }
                    catch (Exception ex)
                    {
                        EditorUtility.DisplayDialog("Error", ex.Message, "OK");
                    }
                }
            }
        }

        private void RefreshPreview()
        {
            _previewTextures.Clear();
            foreach (var tex in _manualTextures.Where(t => t != null).Take(8))
            {
                _previewTextures.Add(tex);
            }
        }

        private void DecodeGif()
        {
#if UNITY_EDITOR_WIN
            string assetPath = AssetDatabase.GetAssetPath(_gifAsset);
            string fullPath = Path.GetFullPath(assetPath);
            var frames = GifDecoder.Decode(fullPath, _maxFrames);
            _manualTextures.Clear();
            foreach (var tex in frames)
            {
                _manualTextures.Add(tex);
            }
            RefreshPreview();
#else
            EditorUtility.DisplayDialog("Unsupported", "GIF decoding currently supports Windows Editor only.", "OK");
#endif
        }

        private void SampleVideoFrames()
        {
            string tempRtName = "TextureArrayBuilder_RT_" + Guid.NewGuid();
            var tempGO = new GameObject(tempRtName);
            var videoPlayer = tempGO.AddComponent<UnityEngine.Video.VideoPlayer>();
            var renderTexture = new RenderTexture(_targetWidth, _targetHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = tempRtName + "_RT"
            };

            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = renderTexture;
            videoPlayer.playOnAwake = false;
            videoPlayer.source = VideoSource.VideoClip;
            videoPlayer.clip = _videoClip;
            videoPlayer.isLooping = false;
            videoPlayer.skipOnDrop = false;
            videoPlayer.Prepare();

            EditorApplication.update += WaitForPreparation;

            void WaitForPreparation()
            {
                if (!videoPlayer.isPrepared)
                    return;

                EditorApplication.update -= WaitForPreparation;
                CaptureFrames();
            }

            void CaptureFrames()
            {
                _manualTextures.Clear();
                double frameRate = _videoClip.frameRate;
                int frameCount = Mathf.Min((int)_videoClip.frameCount, _maxFrames * _frameStep);

                for (int frame = 0; frame < frameCount; frame += _frameStep)
                {
                    double time = frame / frameRate;
                    videoPlayer.time = time;
                    videoPlayer.Play();
                    videoPlayer.Pause();

                    RenderTexture.active = renderTexture;
                    Texture2D tex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                    tex.Apply(false, false);

                    _manualTextures.Add(tex);
                }

                RenderTexture.active = null;
                videoPlayer.Stop();
                DestroyImmediate(tempGO);
                renderTexture.Release();
                DestroyImmediate(renderTexture);
                RefreshPreview();
            }
        }

        private void BuildTextureArray()
        {
            if (_manualTextures.Count == 0)
                throw new InvalidOperationException("No textures to build.");

            var readable = new List<Texture2D>();
            foreach (var tex in _manualTextures)
            {
                if (tex == null) continue;
                Texture2D prepared = PrepareTexture(tex);
                readable.Add(prepared);
            }

            if (readable.Count == 0)
                throw new InvalidOperationException("All textures were null; nothing to build.");

            int width = readable[0].width;
            int height = readable[0].height;
            bool needAlpha = HasAlpha(readable);
            bool srgb = !_linearColor;

            var array = BuildTextureArrayAsset(readable.ToArray(), srgb, needAlpha);
            var outputPath = $"{_outputFolder}/{_outputName}.asset";
            string unityPath = EnsureUnityPath(outputPath);

            AssetDatabase.CreateAsset(array, unityPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(unityPath);
            if (importer != null)
            {
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.mipmapEnabled = _generateMipMaps;
                importer.sRGBTexture = srgb;
                importer.SaveAndReimport();
            }

            AssetDatabase.SaveAssets();

            foreach (var tex in readable)
            {
                DestroyImmediate(tex);
            }
        }

        private Texture2D PrepareTexture(Texture2D source)
        {
            Texture2D readable = ToReadable(source, _generateMipMaps, _linearColor);

            if (_preserveAspect)
            {
                float aspect = (float)readable.width / readable.height;
                float targetAspect = (float)_targetWidth / _targetHeight;
                int finalWidth = _targetWidth;
                int finalHeight = _targetHeight;

                if (Math.Abs(aspect - targetAspect) > 0.01f)
                {
                    if (aspect > targetAspect)
                    {
                        finalHeight = Mathf.RoundToInt(_targetWidth / aspect);
                    }
                    else
                    {
                        finalWidth = Mathf.RoundToInt(_targetHeight * aspect);
                    }
                }

                Texture2D resized = ResizeTexture(readable, finalWidth, finalHeight, _linearColor, _generateMipMaps);
                Texture2D padded = new Texture2D(_targetWidth, _targetHeight, TextureFormat.RGBA32, _generateMipMaps, _linearColor);
                Color32[] clear = Enumerable.Repeat(new Color32(0, 0, 0, 0), _targetWidth * _targetHeight).ToArray();
                padded.SetPixels32(clear);
                int offsetX = (_targetWidth - finalWidth) / 2;
                int offsetY = (_targetHeight - finalHeight) / 2;
                padded.SetPixels(offsetX, offsetY, finalWidth, finalHeight, resized.GetPixels());
                padded.Apply(_generateMipMaps, false);
                DestroyImmediate(resized);
                DestroyImmediate(readable);
                return padded;
            }

            Texture2D fixedSize = ResizeTexture(readable, _targetWidth, _targetHeight, _linearColor, _generateMipMaps);
            DestroyImmediate(readable);
            return fixedSize;
        }

        private static bool HasAlpha(IEnumerable<Texture2D> textures)
        {
            foreach (var tex in textures)
            {
                var pixels = tex.GetPixels32();
                if (pixels.Any(p => p.a < 255))
                    return true;
            }
            return false;
        }

        private static string DescribeFormat(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.DXT1:
                    return "DXT1: 4bpp RGB compression. Fast, good for opaque color textures on PC (DirectX). No alpha.";
                case TextureFormat.DXT5:
                    return "DXT5: 8bpp RGBA compression. Standard PC format when alpha is needed.";
                case TextureFormat.BC7:
                    return "BC7: High-quality RGBA compression for modern GPUs. Better quality than DXT5 at same size.";
                case TextureFormat.ETC2_RGBA8:
                    return "ETC2_RGBA8: 4bpp compressed RGBA for modern mobile (OpenGL ES 3.0+/Vulkan).";
                case TextureFormat.ASTC_6x6:
                    return "ASTC 6x6: Flexible block compression for mobile. Great quality/size tradeoff when supported.";
                case TextureFormat.PVRTC_RGBA4:
                    return "PVRTC RGBA4: iOS compatible, 4bpp RGBA. Moderate artifacts, but wide device support.";
                case TextureFormat.RGBA32:
                    return "RGBA32: Uncompressed, highest quality, largest memory footprint. Universal fallback.";
                default:
                    return $"{format}: Refer to Unity documentation for exact compression characteristics.";
            }
        }

        private static string EnsureUnityPath(string path)
        {
            string unityPath = path.Replace("\\", "/");
            string[] parts = unityPath.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length - 1; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
            return unityPath;
        }

        private static Texture ResolveWindowIcon()
        {
            string[] iconKeys =
            {
                "d_PreTextureMipMapHigh",
                "d_Texture Icon",
                "Texture Icon",
                "SceneAsset Icon"
            };

            foreach (var key in iconKeys)
            {
                var content = EditorGUIUtility.IconContent(key);
                if (content != null && content.image != null)
                    return content.image;
            }

            return null;
        }

        private Texture2D ToReadable(Texture2D source, bool mipmaps, bool linear)
        {
            RenderTextureFormat rtFormat = RenderTextureFormat.ARGB32;
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, rtFormat,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);

            Graphics.Blit(source, rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, mipmaps, linear);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply(mipmaps, false);

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            readable.wrapMode = source.wrapMode;
            readable.filterMode = source.filterMode;
            readable.anisoLevel = source.anisoLevel;
            return readable;
        }

        private Texture2D ResizeTexture(Texture2D source, int width, int height, bool linear, bool mipmaps)
        {
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            rt.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D resized = new Texture2D(width, height, TextureFormat.RGBA32, mipmaps, linear);
            resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            resized.Apply(mipmaps, false);

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return resized;
        }

        private Texture2DArray BuildTextureArrayAsset(Texture2D[] slices, bool srgb, bool needAlpha)
        {
            int width = slices[0].width;
            int height = slices[0].height;

            foreach (var tex in slices)
            {
                if (tex.width != width || tex.height != height)
                    throw new InvalidOperationException("All textures must share identical dimensions after processing.");
            }

            TextureFormat desiredFormat = SupportedFormats[Mathf.Clamp(_formatIndex, 0, SupportedFormats.Length - 1)];
            TextureFormat chosenFormat = ResolveTargetFormat(desiredFormat, needAlpha);
            bool supported = SystemInfo.SupportsTextureFormat(chosenFormat);
            if (!supported)
            {
                Debug.LogWarning($"Texture format {chosenFormat} not supported on this platform. Falling back to RGBA32.");
                chosenFormat = TextureFormat.RGBA32;
            }

            Texture2DArray array = new Texture2DArray(width, height, slices.Length, chosenFormat, _generateMipMaps, _linearColor);

#if UNITY_EDITOR
            for (int i = 0; i < slices.Length; i++)
            {
                if (slices[i].format != chosenFormat && IsCompressibleFormat(chosenFormat))
                {
                    EditorUtility.CompressTexture(slices[i], chosenFormat, TextureCompressionQuality.Best);
                }
            }
#endif

            for (int slice = 0; slice < slices.Length; slice++)
            {
                Graphics.CopyTexture(slices[slice], 0, 0, array, slice, 0);
            }

            array.Apply(_generateMipMaps, false);
            array.wrapMode = TextureWrapMode.Clamp;
            array.filterMode = FilterMode.Bilinear;
            return array;
        }

        private static bool IsCompressibleFormat(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.DXT1:
                case TextureFormat.DXT5:
                case TextureFormat.BC5:
                case TextureFormat.BC6H:
                case TextureFormat.BC7:
                case TextureFormat.ETC2_RGBA8:
                case TextureFormat.ETC_RGB4:
                case TextureFormat.ASTC_4x4:
                case TextureFormat.ASTC_6x6:
                case TextureFormat.ASTC_8x8:
                case TextureFormat.ASTC_10x10:
                case TextureFormat.ASTC_12x12:
                case TextureFormat.PVRTC_RGB2:
                case TextureFormat.PVRTC_RGB4:
                case TextureFormat.PVRTC_RGBA2:
                case TextureFormat.PVRTC_RGBA4:
                    return true;
                default:
                    return false;
            }
        }

        private TextureFormat ResolveTargetFormat(TextureFormat desired, bool needAlpha)
        {
            if (needAlpha)
            {
                switch (desired)
                {
                    case TextureFormat.DXT1:
                    case TextureFormat.PVRTC_RGB2:
                    case TextureFormat.PVRTC_RGB4:
                    case TextureFormat.ETC_RGB4:
                        Debug.LogWarning($"{desired} does not support alpha. Switching to DXT5.");
                        return TextureFormat.DXT5;
                }
            }

            return desired;
        }
    }
}

