using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

// IMPORTANT: The namespace marker below (YUCP.CompanionTutorial.Generated.Source) is swapped to a
// per-export-unique namespace (YUCP.CompanionTutorial.Generated_<guid>) by PackageBuilder when this
// runtime is injected into an exported package, so two imported packages never collide. Do not rename
// it without updating the swap in PackageBuilder.TryInjectCompanionRuntime.
namespace YUCP.CompanionTutorial.Generated.Source
{
    [InitializeOnLoad]
    public static class CompanionTutorialRunner
    {
        private static CompanionTutorialDefinition s_tutorial;
        private static int s_stepIndex = -1;
        private static string s_helperBytesPath;
        private static CompanionOverlayWindow s_overlay;
        private static double s_stepStartedAt;
        private static Rect s_lastTarget;
        private static bool s_currentTargetResolved;
        private static UnityEngine.Object s_selectionAtStepStart;
        private static readonly Dictionary<int, Rect> s_hierarchyItemRects = new Dictionary<int, Rect>();
        private static readonly Dictionary<string, Rect> s_projectItemRects = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, Rect> s_sceneObjectRects = new Dictionary<int, Rect>();
        private static Rect s_transformGizmoRect;
        private static readonly FieldInfo s_editorWindowParentField = typeof(EditorWindow).GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Type s_unityToolbarType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");
        private static Transform s_transformAtStepStart;
        private static Vector3 s_positionAtStepStart;
        private static Quaternion s_rotationAtStepStart;
        private static Vector3 s_scaleAtStepStart;
        private static Material s_demoMaterial;
        private static bool s_demoMode;

        [Serializable]
        private class MetadataWithCompanionTutorial
        {
            public CompanionTutorialDefinition companionTutorial;
        }

        static CompanionTutorialRunner()
        {
            EditorApplication.update += Update;
            EditorApplication.hierarchyWindowItemOnGUI += CaptureHierarchyItemRect;
            EditorApplication.projectWindowItemOnGUI += CaptureProjectItemRect;
            SceneView.duringSceneGui += CaptureSceneViewTargets;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        public static bool IsRunning => s_tutorial != null && s_stepIndex >= 0;

        /// <summary>
        /// Tells the overlay where the injected YUCPCompanionOverlay.bytes payload lives (a
        /// project-relative path). The injected bootstrap calls this before starting so the overlay
        /// can be found at its package-specific location in the buyer's project.
        /// </summary>
        public static void SetHelperPath(string overlayBytesPath)
        {
            s_helperBytesPath = overlayBytesPath;
            CompanionOverlayWindow.HelperBytesPathOverride = overlayBytesPath;
        }

        public static bool QueueRunFromMetadataPath(string metadataPath)
        {
            if (string.IsNullOrEmpty(metadataPath) || !File.Exists(metadataPath))
                return false;

            try
            {
                string json = File.ReadAllText(metadataPath);
                var metadata = JsonUtility.FromJson<MetadataWithCompanionTutorial>(json);
                return Start(metadata?.companionTutorial);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP Companion Tutorial] Failed to parse tutorial metadata: {ex.Message}");
                return false;
            }
        }

        public static bool Start(CompanionTutorialDefinition tutorial)
        {
            return Start(tutorial, false, 0);
        }

        /// <summary>Starts the tutorial at a specific step (used by the authoring "Test from here" button).</summary>
        public static bool Start(CompanionTutorialDefinition tutorial, int startIndex)
        {
            return Start(tutorial, false, startIndex);
        }

        /// <summary>
        /// Parses a serialized <see cref="CompanionTutorialDefinition"/> (as produced by
        /// JsonUtility.ToJson on the authoring definition) and starts it. Lets the devtools authoring
        /// UI preview tutorials without sharing this runtime's POCO type — the JSON field names are the
        /// contract.
        /// </summary>
        public static bool QueueRunFromJson(string json, int startIndex = 0)
        {
            if (string.IsNullOrEmpty(json))
                return false;

            try
            {
                var definition = JsonUtility.FromJson<CompanionTutorialDefinition>(json);
                return Start(definition, false, startIndex);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP Companion Tutorial] Failed to parse tutorial JSON: {ex.Message}");
                return false;
            }
        }

        private static bool Start(CompanionTutorialDefinition tutorial, bool demoMode, int startIndex)
        {
            if (tutorial == null || !tutorial.enabled || tutorial.steps == null || tutorial.steps.Count == 0)
                return false;

            Stop();
            s_tutorial = tutorial;
            s_demoMode = demoMode;
            s_stepIndex = Mathf.Clamp(startIndex, 0, tutorial.steps.Count - 1);
            CompanionOverlayWindow.HelperBytesPathOverride = s_helperBytesPath;
            s_overlay = new CompanionOverlayWindow();
            s_overlay.NextRequested = Next;
            s_overlay.PreviousRequested = Previous;
            s_overlay.CloseRequested = Stop;
            s_overlay.Show();
            EnterCurrentStep();
            return true;
        }

        public static void StartDemo()
        {
            GameObject demoObject = GameObject.Find("Main Camera");
            if (demoObject != null)
                Selection.activeGameObject = demoObject;

            Start(new CompanionTutorialDefinition
            {
                enabled = true,
                title = "Whole Unity Overlay Demo",
                steps = new List<CompanionTutorialStep>
                {
                    new CompanionTutorialStep
                    {
                        title = "Toolbar",
                        text = "The overlay covers the top Unity toolbar. Clicks outside this card still reach Unity.",
                        target = "toolbar",
                        waitFor = "manual",
                        mouseAction = "click"
                    },
                    new CompanionTutorialStep
                    {
                        title = "Play Button",
                        text = "Tutorials can point to a specific control in the main Unity toolbar.",
                        target = "toolbar:play",
                        waitFor = "manual",
                        mouseAction = "click",
                        spotlightPadding = new Vector4(8, 8, 8, 8)
                    },
                    new CompanionTutorialStep
                    {
                        title = "Layers Dropdown",
                        text = "Named toolbar regions can point at specific top-bar controls like Layers or Layout.",
                        target = "toolbar:layers",
                        waitFor = "manual",
                        mouseAction = "click",
                        spotlightPadding = new Vector4(8, 8, 8, 8)
                    },
                    new CompanionTutorialStep
                    {
                        title = "YUCP Menu",
                        text = "Menu-bar entries can be targeted as fixed editor chrome regions when Unity has no public control object.",
                        target = "menu:yucp",
                        waitFor = "manual",
                        mouseAction = "click",
                        spotlightPadding = new Vector4(6, 5, 6, 5)
                    },
                    new CompanionTutorialStep
                    {
                        title = "Project Window",
                        text = "Package tutorials can point to project assets, folders, or broad editor regions.",
                        target = "project",
                        waitFor = "manual",
                        mouseAction = "doubleClick"
                    },
                    new CompanionTutorialStep
                    {
                        title = "Inspector",
                        text = "Steps can wait for real Unity state in package tutorials. This demo uses manual navigation.",
                        target = "inspector",
                        waitFor = "manual",
                        mouseAction = "rightClick",
                        overlayMode = "unintrusive"
                    },
                    new CompanionTutorialStep
                    {
                        title = "Hierarchy Object",
                        text = "This points to a specific object row in the Hierarchy, not just the whole panel.",
                        target = "hierarchy:Main Camera",
                        waitFor = "manual",
                        mouseAction = "click",
                        spotlightPadding = new Vector4(8, 6, 8, 6)
                    },
                    new CompanionTutorialStep
                    {
                        title = "Main Camera",
                        text = "Tutorials can point to a specific GameObject in the Scene view.",
                        target = "scene:Main Camera",
                        waitFor = "manual",
                        mouseAction = "click"
                    },
                    new CompanionTutorialStep
                    {
                        title = "Move the object",
                        text = "Move the selected object with the transform gizmo. This step completes when its transform changes.",
                        target = "gizmo",
                        waitFor = "transformMoved:selected",
                        mouseAction = "drag"
                    },
                    new CompanionTutorialStep
                    {
                        title = "Transform Position",
                        text = "Inspector targets can point at exposed Transform properties through the Inspector visual tree.",
                        target = "property:position",
                        waitFor = "manual",
                        mouseAction = "click",
                        spotlightPadding = new Vector4(10, 8, 10, 8)
                    },
                    new CompanionTutorialStep
                    {
                        title = "Transform Rotation",
                        text = "The resolver also checks serialized binding paths such as local rotation when Unity exposes them.",
                        target = "property:rotation",
                        waitFor = "manual",
                        mouseAction = "click",
                        spotlightPadding = new Vector4(10, 8, 10, 8)
                    },
                    new CompanionTutorialStep
                    {
                        title = "Transform Scale",
                        text = "Scale is another Transform row resolved from the Inspector, not a guessed absolute screen position.",
                        target = "property:scale",
                        waitFor = "manual",
                        mouseAction = "click",
                        spotlightPadding = new Vector4(10, 8, 10, 8)
                    }
                }
            }, true, 0);
        }

        public static void Next()
        {
            if (!IsRunning)
                return;

            if (s_stepIndex >= s_tutorial.steps.Count - 1)
            {
                Stop();
                return;
            }

            s_stepIndex++;
            EnterCurrentStep();
        }

        public static void Previous()
        {
            if (!IsRunning)
                return;

            s_stepIndex = Mathf.Max(0, s_stepIndex - 1);
            EnterCurrentStep();
        }

        public static void Stop()
        {
            s_overlay?.Close();
            s_overlay = null;
            CompanionOverlayWindow.CloseOrphanedNativeWindows();
            if (s_demoMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(s_demoMaterial);
                s_demoMaterial = null;
            }
            s_tutorial = null;
            s_stepIndex = -1;
            s_currentTargetResolved = false;
            s_demoMode = false;
        }

        private static void EnterCurrentStep()
        {
            PrepareStepSelection(GetCurrentStep());
            s_stepStartedAt = EditorApplication.timeSinceStartup;
            s_selectionAtStepStart = Selection.activeObject;
            CaptureTransformAtStepStart();
            TryFocusBroadEditorWindowTarget(GetCurrentStep());
            RepaintTargetSources();
            s_currentTargetResolved = TryResolveTarget(GetCurrentStep(), out s_lastTarget);
            Render();
        }

        private static CompanionTutorialStep GetCurrentStep()
        {
            if (!IsRunning || s_stepIndex < 0 || s_stepIndex >= s_tutorial.steps.Count)
                return null;
            return s_tutorial.steps[s_stepIndex];
        }

        private static void Update()
        {
            if (!IsRunning)
                return;

            TryResolveTarget(GetCurrentStep(), out s_lastTarget);
            s_currentTargetResolved = s_lastTarget.width > 1 && s_lastTarget.height > 1;
            if (!s_currentTargetResolved)
                RepaintTargetSources();
            Render();

            if (IsWaitConditionSatisfied(GetCurrentStep()))
            {
                Next();
            }
        }

        private static void RepaintTargetSources()
        {
            EditorApplication.RepaintHierarchyWindow();
            EditorApplication.RepaintProjectWindow();
            SceneView.RepaintAll();
        }

        private static void Render()
        {
            if (s_overlay == null)
                return;

            var step = GetCurrentStep();
            if (step == null)
                return;

            s_overlay.Render(new CompanionOverlayFrame
            {
                TutorialTitle = s_tutorial.title ?? "Tutorial",
                StepTitle = step.title ?? "Step",
                StepText = step.text ?? "",
                StepCounter = $"{s_stepIndex + 1} / {s_tutorial.steps.Count}",
                TargetRect = s_lastTarget,
                TargetResolved = s_currentTargetResolved,
                CanGoBack = s_stepIndex > 0,
                IsLastStep = s_stepIndex >= s_tutorial.steps.Count - 1,
                SpotlightPadding = step.spotlightPadding,
                StartedAt = s_stepStartedAt,
                WaitDescription = GetWaitDescription(step.waitFor),
                MouseAction = step.mouseAction ?? "none",
                OverlayMode = string.IsNullOrWhiteSpace(step.overlayMode) ? "intrusive" : step.overlayMode
            });
        }

        private static bool TryResolveTarget(CompanionTutorialStep step, out Rect rect)
        {
            rect = Rect.zero;
            Rect main = EditorGUIUtility.GetMainWindowPosition();

            if (step == null)
                return false;

            if (step.targetRect.z > 1 && step.targetRect.w > 1)
            {
                rect = new Rect(step.targetRect.x, step.targetRect.y, step.targetRect.z, step.targetRect.w);
                return true;
            }

            string target = (step.target ?? "center").Trim();
            if (target.StartsWith("ui:", StringComparison.OrdinalIgnoreCase))
            {
                return TryResolveUiToolkitTarget(target.Substring(3).Trim(), main, out rect);
            }

            if (target.StartsWith("toolbar:", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("topbar:", StringComparison.OrdinalIgnoreCase))
            {
                string selector = target.Substring(target.IndexOf(':') + 1).Trim();
                return TryResolveToolbarTarget(selector, main, out rect);
            }

            if (target.StartsWith("menu:", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("menubar:", StringComparison.OrdinalIgnoreCase))
            {
                string selector = target.Substring(target.IndexOf(':') + 1).Trim();
                return TryResolveMenuBarTarget(selector, main, out rect);
            }

            if (target.StartsWith("material:", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("shader:", StringComparison.OrdinalIgnoreCase))
            {
                string selector = target.Substring(target.IndexOf(':') + 1).Trim();
                return TryResolveMaterialInspectorTarget(selector, main, out rect);
            }

            if (target.StartsWith("property:", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("inspector:", StringComparison.OrdinalIgnoreCase))
            {
                string selector = target.Substring(target.IndexOf(':') + 1).Trim();
                return TryResolveInspectorPropertyTarget(selector, main, out rect) ||
                       TryResolveTransformInspectorFallback(selector, main, out rect);
            }

            if (target.StartsWith("hierarchy:", StringComparison.OrdinalIgnoreCase))
                return TryResolveHierarchyItemTarget(target.Substring("hierarchy:".Length).Trim(), out rect);

            if (target.StartsWith("project:", StringComparison.OrdinalIgnoreCase))
                return TryResolveProjectItemTarget(target.Substring("project:".Length).Trim(), out rect);

            if (target.StartsWith("scene:", StringComparison.OrdinalIgnoreCase))
                return TryResolveSceneObjectTarget(target.Substring("scene:".Length).Trim(), out rect);

            if (target.StartsWith("object:", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("gameobject:", StringComparison.OrdinalIgnoreCase))
            {
                string objectTarget = target.Substring(target.IndexOf(':') + 1).Trim();
                return TryResolveSceneObjectTarget(objectTarget, out rect) ||
                       TryResolveHierarchyItemTarget(objectTarget, out rect);
            }

            if (target.Equals("gizmo", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("transform-gizmo", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("gizmo:transform", StringComparison.OrdinalIgnoreCase))
            {
                rect = s_transformGizmoRect;
                return rect.width > 1 && rect.height > 1;
            }

            if (TryResolveKnownEditorWindow(target, main, out rect))
                return true;

            if (IsKnownEditorWindowTarget(target))
                return false;

            rect = ResolveKnownRegion(target, main);
            return rect.width > 1 && rect.height > 1;
        }

        private static void PrepareStepSelection(CompanionTutorialStep step)
        {
            string target = (step?.target ?? "").Trim();
            if (string.IsNullOrEmpty(target))
                return;

            if (target.StartsWith("material:", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("shader:", StringComparison.OrdinalIgnoreCase))
            {
                if (s_demoMode)
                    EnsureDemoMaterial();
                if (s_demoMode && s_demoMaterial != null)
                    Selection.activeObject = s_demoMaterial;
                TryFocusBroadEditorWindowTarget(new CompanionTutorialStep { target = "inspector" });
                return;
            }

            if (target.StartsWith("property:", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("inspector:", StringComparison.OrdinalIgnoreCase))
            {
                string selector = target.Substring(target.IndexOf(':') + 1).Trim();
                if (IsTransformPropertySelector(selector))
                {
                    GameObject demoObject = ResolveSceneObject("Main Camera");
                    if (demoObject != null)
                        Selection.activeGameObject = demoObject;
                }

                TryFocusBroadEditorWindowTarget(new CompanionTutorialStep { target = "inspector" });
                return;
            }

            if (target.Equals("gizmo", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("hierarchy:", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("scene:", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("object:", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("gameobject:", StringComparison.OrdinalIgnoreCase))
            {
                string selector = target;
                int colon = target.IndexOf(':');
                if (colon >= 0)
                    selector = target.Substring(colon + 1).Trim();
                if (target.Equals("gizmo", StringComparison.OrdinalIgnoreCase))
                    selector = "Main Camera";

                GameObject sceneObject = ResolveSceneObject(selector);
                if (sceneObject != null)
                    Selection.activeGameObject = sceneObject;
            }
        }

        private static bool IsTransformPropertySelector(string selector)
        {
            string key = NormalizeInspectorSelector(selector);
            return key == "position" ||
                   key == "rotation" ||
                   key == "scale" ||
                   key == "localposition" ||
                   key == "localrotation" ||
                   key == "localscale";
        }

        private static void EnsureDemoMaterial()
        {
            if (s_demoMaterial != null)
                return;

            Shader demoShader = Shader.Find("Standard") ??
                                Shader.Find("Universal Render Pipeline/Lit") ??
                                Shader.Find("Hidden/InternalErrorShader") ??
                                Shader.Find("Sprites/Default") ??
                                Shader.Find("Diffuse");
            if (demoShader == null)
            {
                Debug.LogWarning("[YUCP Companion Tutorial] Could not create demo material because no compatible built-in shader was found.");
                return;
            }

            s_demoMaterial = new Material(demoShader)
            {
                name = "YUCP Companion Demo Material",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static void TryFocusBroadEditorWindowTarget(CompanionTutorialStep step)
        {
            string target = (step?.target ?? "").Trim();
            if (!IsKnownEditorWindowTarget(target))
                return;

            Type type = ResolveKnownEditorWindowType(target);
            if (type == null)
                return;

            try
            {
                MethodInfo method = typeof(EditorWindow).GetMethod(
                    "FocusWindowIfItsOpen",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Type) },
                    null);
                method?.Invoke(null, new object[] { type });
            }
            catch
            {
                // Focusing is best-effort; unresolved targets fall back to a centered card.
            }
        }

        private static bool TryResolveUiToolkitTarget(string elementName, Rect mainWindow, out Rect localRect)
        {
            localRect = Rect.zero;
            if (string.IsNullOrEmpty(elementName))
                return false;

            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window == null || window.rootVisualElement == null)
                    continue;

                VisualElement element = window.rootVisualElement.Q(elementName);
                if (element == null || element.panel == null || element.worldBound.width <= 1 || element.worldBound.height <= 1)
                    continue;

                Rect world = element.worldBound;
                Rect windowPosition = window.position;
                localRect = new Rect(
                    windowPosition.x + world.x - mainWindow.x,
                    windowPosition.y + world.y - mainWindow.y,
                    world.width,
                    world.height);
                return true;
            }

            return false;
        }

        private static bool TryResolveInspectorPropertyTarget(string selector, Rect mainWindow, out Rect rect)
        {
            rect = Rect.zero;
            string[] bindingPaths = GetInspectorBindingPathCandidates(selector);
            string[] tokens = GetInspectorTextTokens(selector);

            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window == null || window.rootVisualElement == null)
                    continue;

                if (!IsInspectorWindow(window))
                    continue;

                VisualElement element = FindInspectorVisualElement(window.rootVisualElement, bindingPaths, tokens);
                if (element == null)
                    continue;

                if (TryEditorWindowVisualElementToMainWindowRect(window, element, mainWindow, out rect))
                    return true;
            }

            return false;
        }

        private static bool IsInspectorWindow(EditorWindow window)
        {
            Type type = window.GetType();
            return type.FullName == "UnityEditor.InspectorWindow" ||
                   string.Equals(window.titleContent?.text, "Inspector", StringComparison.OrdinalIgnoreCase);
        }

        private static VisualElement FindInspectorVisualElement(VisualElement root, string[] bindingPaths, string[] tokens)
        {
            VisualElement best = null;
            int bestScore = int.MinValue;
            float bestArea = float.MaxValue;

            VisitVisualElements(root, element =>
            {
                if (!IsUsableVisualElement(element))
                    return;

                int score = ScoreInspectorVisualElement(element, bindingPaths, tokens);
                if (score <= 0)
                    return;

                float area = element.worldBound.width * element.worldBound.height;
                if (score > bestScore || (score == bestScore && area > 1 && area < bestArea))
                {
                    best = element;
                    bestScore = score;
                    bestArea = area;
                }
            });

            return best;
        }

        private static int ScoreInspectorVisualElement(VisualElement element, string[] bindingPaths, string[] tokens)
        {
            string bindingPath = GetBindingPath(element);
            if (!string.IsNullOrEmpty(bindingPath))
            {
                foreach (string candidate in bindingPaths)
                {
                    if (string.Equals(bindingPath, candidate, StringComparison.OrdinalIgnoreCase))
                        return 100;

                    if (bindingPath.EndsWith("." + candidate, StringComparison.OrdinalIgnoreCase))
                        return 90;

                    if (bindingPath.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                        return 75;
                }
            }

            int textScore = 0;
            foreach (string token in tokens)
            {
                if (VisualElementMatchesToken(element, token))
                    textScore = Mathf.Max(textScore, 45);
            }

            return textScore;
        }

        private static string GetBindingPath(VisualElement element)
        {
            object value = GetMemberValue(element, element.GetType(), "bindingPath");
            return value as string;
        }

        private static string[] GetInspectorBindingPathCandidates(string selector)
        {
            string key = NormalizeInspectorSelector(selector);
            switch (key)
            {
                case "shader":
                case "shaderfield":
                case "shaderinput":
                    return new[] { "m_Shader", "shader" };
                case "color":
                case "basecolor":
                case "_color":
                    return new[] { "_Color", "m_Color", "m_SavedProperties.m_Colors", "color" };
                case "maintex":
                case "basemap":
                case "_maintex":
                    return new[] { "_MainTex", "m_MainTex", "m_SavedProperties.m_TexEnvs", "mainTex" };
                case "metallic":
                case "_metallic":
                    return new[] { "_Metallic", "metallic" };
                case "smoothness":
                case "glossiness":
                case "_glossiness":
                    return new[] { "_Glossiness", "_Smoothness", "smoothness", "glossiness" };
                case "position":
                    return new[] { "m_LocalPosition" };
                case "rotation":
                    return new[] { "m_LocalRotation", "m_LocalEulerAnglesHint" };
                case "scale":
                    return new[] { "m_LocalScale" };
                default:
                    return new[] { selector ?? "" };
            }
        }

        private static string[] GetInspectorTextTokens(string selector)
        {
            string key = NormalizeInspectorSelector(selector);
            switch (key)
            {
                case "shader":
                case "shaderfield":
                case "shaderinput":
                    return new[] { "shader" };
                case "color":
                case "basecolor":
                case "_color":
                    return new[] { "base map", "base color", "albedo", "color", "_Color" };
                case "maintex":
                case "basemap":
                case "_maintex":
                    return new[] { "base map", "main tex", "texture", "_MainTex" };
                case "metallic":
                case "_metallic":
                    return new[] { "metallic" };
                case "smoothness":
                case "glossiness":
                case "_glossiness":
                    return new[] { "smoothness", "glossiness" };
                case "position":
                    return new[] { "position" };
                case "rotation":
                    return new[] { "rotation" };
                case "scale":
                    return new[] { "scale" };
                default:
                    return new[] { selector ?? "" };
            }
        }

        private static string NormalizeInspectorSelector(string selector)
        {
            return (selector ?? "")
                .Replace("-", "")
                .Replace("_", "")
                .Replace(" ", "")
                .Trim()
                .ToLowerInvariant();
        }

        private static bool TryEditorWindowVisualElementToMainWindowRect(EditorWindow window, VisualElement element, Rect mainWindow, out Rect rect)
        {
            rect = Rect.zero;
            if (window == null || element == null || !IsUsableVisualElement(element))
                return false;

            Rect world = element.worldBound;
            if (!TryGetVisibleEditorWindowRect(window, out Rect windowRect))
                windowRect = window.position;

            rect = new Rect(
                windowRect.x + world.x - mainWindow.x,
                windowRect.y + world.y - mainWindow.y,
                world.width,
                world.height);
            return rect.width > 1 && rect.height > 1;
        }

        private static bool TryResolveTransformInspectorFallback(string selector, Rect mainWindow, out Rect rect)
        {
            rect = Rect.zero;
            if (!IsTransformPropertySelector(selector) || Selection.activeTransform == null)
                return false;

            if (!TryResolveKnownEditorWindow("inspector", mainWindow, out Rect inspector))
                return false;

            string key = NormalizeInspectorSelector(selector);
            float rowHeight = Mathf.Max(18f, EditorGUIUtility.singleLineHeight);
            float spacing = Mathf.Max(2f, EditorGUIUtility.standardVerticalSpacing);
            float transformHeaderY = inspector.y + 84f;
            float firstRowY = transformHeaderY + rowHeight + spacing + 4f;

            int rowIndex;
            if (key == "position" || key == "localposition")
                rowIndex = 0;
            else if (key == "rotation" || key == "localrotation")
                rowIndex = 1;
            else if (key == "scale" || key == "localscale")
                rowIndex = 2;
            else
                return false;

            float x = inspector.x + 18f;
            float y = firstRowY + rowIndex * (rowHeight + spacing);
            float width = Mathf.Max(80f, inspector.width - 36f);
            rect = new Rect(x, y, width, rowHeight + 2f);
            return rect.width > 1 && rect.height > 1;
        }

        private static bool TryResolveToolbarTarget(string selector, Rect mainWindow, out Rect rect)
        {
            rect = Rect.zero;
            string key = (selector ?? "").Trim().ToLowerInvariant();
            if (TryResolveToolbarVisualElementTarget(key, mainWindow, out rect))
                return true;

            return false;
        }

        private static bool TryResolveToolbarVisualElementTarget(string key, Rect mainWindow, out Rect rect)
        {
            rect = Rect.zero;
            if (s_unityToolbarType == null)
                return false;

            foreach (UnityEngine.Object toolbar in Resources.FindObjectsOfTypeAll(s_unityToolbarType))
            {
                if (toolbar == null)
                    continue;

                VisualElement root = GetMemberValue(toolbar, toolbar.GetType(), "m_Root") as VisualElement;
                if (root == null || root.panel == null)
                    continue;

                VisualElement element = FindToolbarElement(root, key);
                if (element == null || !IsUsableVisualElement(element))
                    continue;

                if (TryVisualElementToMainWindowRect(toolbar, element, mainWindow, out rect))
                    return true;
            }

            return false;
        }

        private static VisualElement FindToolbarElement(VisualElement root, string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            if (key == "play-button")
                key = "play";
            else if (key == "pause-button")
                key = "pause";
            else if (key == "step-button")
                key = "step";
            else if (key == "layers-dropdown")
                key = "layers";
            else if (key == "layout-dropdown")
                key = "layout";

            if (key == "play" || key == "pause" || key == "step")
            {
                VisualElement playZone = root.Q("ToolbarZonePlayMode") ?? root.Q("ToolbarZonePlayModeControls");
                VisualElement named = FindVisualElementByToken(playZone ?? root, key);
                if (named != null && IsButtonLikeElement(named))
                    return named;

                var buttons = new List<VisualElement>();
                CollectButtonLikeElements(playZone ?? root, buttons);
                buttons.Sort((a, b) => a.worldBound.x.CompareTo(b.worldBound.x));
                int index = key == "play" ? 0 : key == "pause" ? 1 : 2;
                return index >= 0 && index < buttons.Count ? buttons[index] : null;
            }

            return FindVisualElementByToken(root, key);
        }

        private static VisualElement FindVisualElementByToken(VisualElement root, string token)
        {
            if (root == null)
                return null;

            VisualElement best = null;
            float bestArea = float.MaxValue;
            VisitVisualElements(root, element =>
            {
                if (!IsUsableVisualElement(element))
                    return;

                if (!VisualElementMatchesToken(element, token))
                    return;

                float area = element.worldBound.width * element.worldBound.height;
                if (area > 1 && area < bestArea)
                {
                    best = element;
                    bestArea = area;
                }
            });
            return best;
        }

        private static void CollectButtonLikeElements(VisualElement root, List<VisualElement> results)
        {
            VisitVisualElements(root, element =>
            {
                if (!IsUsableVisualElement(element))
                    return;

                if (IsButtonLikeElement(element))
                    results.Add(element);
            });
        }

        private static bool IsButtonLikeElement(VisualElement element)
        {
            if (element == null)
                return false;

            string typeName = element.GetType().Name;
            return typeName.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   HasClassContaining(element, "button");
        }

        private static void VisitVisualElements(VisualElement root, Action<VisualElement> visitor)
        {
            if (root == null || visitor == null)
                return;

            visitor(root);
            foreach (VisualElement child in root.Children())
                VisitVisualElements(child, visitor);
        }

        private static bool VisualElementMatchesToken(VisualElement element, string token)
        {
            return ContainsToken(element.name, token) ||
                   ContainsToken(element.tooltip, token) ||
                   ContainsToken(GetVisualElementText(element), token) ||
                   HasClassContaining(element, token);
        }

        private static string GetVisualElementText(VisualElement element)
        {
            PropertyInfo textProperty = element.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (textProperty == null || textProperty.PropertyType != typeof(string))
                return null;

            try
            {
                return textProperty.GetValue(element, null) as string;
            }
            catch
            {
                return null;
            }
        }

        private static bool HasClassContaining(VisualElement element, string token)
        {
            foreach (string className in element.GetClasses())
            {
                if (ContainsToken(className, token))
                    return true;
            }

            return false;
        }

        private static bool ContainsToken(string value, string token)
        {
            return !string.IsNullOrEmpty(value) &&
                   !string.IsNullOrEmpty(token) &&
                   value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsUsableVisualElement(VisualElement element)
        {
            return element != null &&
                   element.panel != null &&
                   element.resolvedStyle.display != DisplayStyle.None &&
                   element.resolvedStyle.visibility == Visibility.Visible &&
                   element.worldBound.width > 1 &&
                   element.worldBound.height > 1;
        }

        private static bool TryVisualElementToMainWindowRect(UnityEngine.Object owner, VisualElement element, Rect mainWindow, out Rect rect)
        {
            rect = Rect.zero;
            Rect world = element.worldBound;
            if (TryGetToolbarScreenPosition(owner, out Rect screenPosition))
            {
                rect = new Rect(
                    screenPosition.x + world.x - mainWindow.x,
                    screenPosition.y + world.y - mainWindow.y,
                    world.width,
                    world.height);
                return rect.width > 1 && rect.height > 1;
            }

            rect = new Rect(world.x, world.y, world.width, world.height);
            return rect.width > 1 &&
                   rect.height > 1 &&
                   rect.xMin >= -2 &&
                   rect.yMin >= -2 &&
                   rect.xMax <= mainWindow.width + 2 &&
                   rect.yMax <= mainWindow.height + 2;
        }

        private static bool TryGetToolbarScreenPosition(UnityEngine.Object toolbar, out Rect rect)
        {
            rect = Rect.zero;
            if (toolbar == null)
                return false;

            Type type = toolbar.GetType();
            if (TryGetRectProperty(toolbar, type, "screenPosition", out rect) ||
                TryGetRectProperty(toolbar, type, "windowPosition", out rect) ||
                TryGetRectProperty(toolbar, type, "position", out rect))
            {
                return rect.width > 1 && rect.height > 1;
            }

            object backend = GetMemberValue(toolbar, type, "windowBackend");
            if (backend != null)
            {
                Type backendType = backend.GetType();
                object window = GetMemberValue(backend, backendType, "window");
                if (window != null)
                {
                    Type windowType = window.GetType();
                    if (TryGetRectProperty(window, windowType, "screenPosition", out rect) ||
                        TryGetRectProperty(window, windowType, "position", out rect))
                    {
                        return rect.width > 1 && rect.height > 1;
                    }
                }
            }

            return false;
        }

        private static bool TryResolveMenuBarTarget(string selector, Rect mainWindow, out Rect rect)
        {
            rect = Rect.zero;
            return TryResolveNativeMenuBarTarget(selector, mainWindow, out rect);
        }

        private static bool TryResolveNativeMenuBarTarget(string selector, Rect mainWindow, out Rect rect)
        {
            rect = Rect.zero;
#if UNITY_EDITOR_WIN
            string key = NormalizeMenuLabel(selector);
            if (string.IsNullOrEmpty(key))
                return false;

            IntPtr hwnd = FindUnityMainWindowHandle(mainWindow);
            if (hwnd == IntPtr.Zero)
                return false;

            IntPtr menu = GetMenu(hwnd);
            if (menu == IntPtr.Zero)
                return false;

            int count = GetMenuItemCount(menu);
            for (int i = 0; i < count; i++)
            {
                string label = GetMenuItemText(menu, i);
                if (NormalizeMenuLabel(label) != key)
                    continue;

                if (!GetMenuItemRect(hwnd, menu, (uint)i, out NativeRect nativeRect))
                    return false;

                rect = new Rect(
                    nativeRect.Left - mainWindow.x,
                    nativeRect.Top - mainWindow.y,
                    nativeRect.Right - nativeRect.Left,
                    nativeRect.Bottom - nativeRect.Top);
                return rect.width > 1 && rect.height > 1;
            }
#endif
            return false;
        }

        private static string NormalizeMenuLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return value
                .Replace("&", "")
                .Replace("...", "")
                .Trim()
                .ToLowerInvariant();
        }

#if UNITY_EDITOR_WIN
        private static IntPtr FindUnityMainWindowHandle(Rect mainWindow)
        {
            IntPtr result = IntPtr.Zero;
            int processId = Process.GetCurrentProcess().Id;
            float bestDistance = float.MaxValue;

            EnumWindows((hwnd, lParam) =>
            {
                if (!IsWindowVisible(hwnd))
                    return true;

                GetWindowThreadProcessId(hwnd, out int windowProcessId);
                if (windowProcessId != processId)
                    return true;

                if (!GetWindowRect(hwnd, out NativeRect nativeRect))
                    return true;

                float width = nativeRect.Right - nativeRect.Left;
                float height = nativeRect.Bottom - nativeRect.Top;
                if (width < 300 || height < 200)
                    return true;

                float distance =
                    Mathf.Abs(nativeRect.Left - mainWindow.x) +
                    Mathf.Abs(nativeRect.Top - mainWindow.y) +
                    Mathf.Abs(width - mainWindow.width) +
                    Mathf.Abs(height - mainWindow.height);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    result = hwnd;
                }

                return true;
            }, IntPtr.Zero);

            return result;
        }

        private static string GetMenuItemText(IntPtr menu, int index)
        {
            var builder = new StringBuilder(256);
            GetMenuString(menu, (uint)index, builder, builder.Capacity, MF_BYPOSITION);
            return builder.ToString();
        }

        private const uint MF_BYPOSITION = 0x00000400;

        private delegate bool EnumWindowsDelegate(IntPtr hwnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsDelegate lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetMenu(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int GetMenuItemCount(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool GetMenuItemRect(IntPtr hwnd, IntPtr hMenu, uint item, out NativeRect rect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetMenuString(IntPtr hMenu, uint uIDItem, StringBuilder lpString, int nMaxCount, uint uFlag);
#endif

        private static bool TryResolveMaterialInspectorTarget(string selector, Rect mainWindow, out Rect rect)
        {
            rect = Rect.zero;
            if (!(Selection.activeObject is Material))
                return false;

            return TryResolveInspectorPropertyTarget(selector, mainWindow, out rect);
        }

        private static bool TryResolveHierarchyItemTarget(string selector, out Rect rect)
        {
            rect = Rect.zero;
            GameObject target = ResolveSceneObject(selector);
            if (target == null)
                return false;

            return s_hierarchyItemRects.TryGetValue(target.GetInstanceID(), out rect) &&
                   rect.width > 1 &&
                   rect.height > 1;
        }

        private static bool TryResolveProjectItemTarget(string selector, out Rect rect)
        {
            rect = Rect.zero;
            if (string.IsNullOrWhiteSpace(selector))
                return false;

            string guid = selector;
            if (selector.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
                guid = selector.Substring("guid:".Length).Trim();
            else if (selector.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                     selector.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                guid = AssetDatabase.AssetPathToGUID(selector);
            else
                guid = FindAssetGuidByName(selector);

            return !string.IsNullOrEmpty(guid) &&
                   s_projectItemRects.TryGetValue(guid, out rect) &&
                   rect.width > 1 &&
                   rect.height > 1;
        }

        private static bool TryResolveSceneObjectTarget(string selector, out Rect rect)
        {
            rect = Rect.zero;
            GameObject target = ResolveSceneObject(selector);
            if (target == null)
                return false;

            return s_sceneObjectRects.TryGetValue(target.GetInstanceID(), out rect) &&
                   rect.width > 1 &&
                   rect.height > 1;
        }

        private static void CaptureHierarchyItemRect(int instanceId, Rect selectionRect)
        {
            if (!IsRunning)
                return;

            if (selectionRect.width <= 1 || selectionRect.height <= 1)
                return;

            if (!(EditorUtility.InstanceIDToObject(instanceId) is GameObject))
                return;

            s_hierarchyItemRects[instanceId] = GuiRectToMainWindowRect(selectionRect);
        }

        private static void CaptureProjectItemRect(string guid, Rect selectionRect)
        {
            if (!IsRunning)
                return;

            if (string.IsNullOrEmpty(guid) || selectionRect.width <= 1 || selectionRect.height <= 1)
                return;

            s_projectItemRects[guid] = GuiRectToMainWindowRect(selectionRect);
        }

        private static void CaptureSceneViewTargets(SceneView sceneView)
        {
            if (!IsRunning)
                return;

            if (sceneView == null || Event.current == null || Event.current.type != EventType.Repaint)
                return;

            s_sceneObjectRects.Clear();
            Camera camera = sceneView.camera;
            if (camera == null)
                return;

            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (!IsSceneObject(go))
                    continue;

                if (TryProjectGameObjectToSceneRect(go, camera, out Rect rect))
                    s_sceneObjectRects[go.GetInstanceID()] = rect;
            }

            if (Selection.transforms != null && Selection.transforms.Length > 0)
            {
                Vector2 handleGuiPoint = HandleUtility.WorldToGUIPoint(Tools.handlePosition);
                Vector2 handleScreenPoint = GUIUtility.GUIToScreenPoint(handleGuiPoint);
                Rect main = EditorGUIUtility.GetMainWindowPosition();
                s_transformGizmoRect = new Rect(handleScreenPoint.x - main.x - 18, handleScreenPoint.y - main.y - 18, 36, 36);
            }
            else
            {
                s_transformGizmoRect = Rect.zero;
            }
        }

        private static bool TryProjectGameObjectToSceneRect(GameObject go, Camera camera, out Rect rect)
        {
            rect = Rect.zero;
            Bounds bounds = GetWorldBounds(go);
            if (bounds.size == Vector3.zero)
            {
                Vector3 viewport = camera.WorldToViewportPoint(go.transform.position);
                if (viewport.z <= 0 || viewport.x < -0.25f || viewport.x > 1.25f || viewport.y < -0.25f || viewport.y > 1.25f)
                    return false;

                Vector2 screenPoint = GUIUtility.GUIToScreenPoint(HandleUtility.WorldToGUIPoint(go.transform.position));
                Rect main = EditorGUIUtility.GetMainWindowPosition();
                rect = new Rect(screenPoint.x - main.x - 12, screenPoint.y - main.y - 12, 24, 24);
                return true;
            }

            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector3[] corners =
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z)
            };

            Rect mainWindow = EditorGUIUtility.GetMainWindowPosition();
            bool hasPoint = false;
            float xMin = float.PositiveInfinity;
            float yMin = float.PositiveInfinity;
            float xMax = float.NegativeInfinity;
            float yMax = float.NegativeInfinity;

            foreach (Vector3 corner in corners)
            {
                Vector3 viewport = camera.WorldToViewportPoint(corner);
                if (viewport.z <= 0)
                    continue;

                Vector2 screenPoint = GUIUtility.GUIToScreenPoint(HandleUtility.WorldToGUIPoint(corner));
                float x = screenPoint.x - mainWindow.x;
                float y = screenPoint.y - mainWindow.y;
                xMin = Mathf.Min(xMin, x);
                yMin = Mathf.Min(yMin, y);
                xMax = Mathf.Max(xMax, x);
                yMax = Mathf.Max(yMax, y);
                hasPoint = true;
            }

            if (!hasPoint)
                return false;

            rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            if (rect.width < 24)
                rect = new Rect(rect.center.x - 12, rect.y, 24, rect.height);
            if (rect.height < 24)
                rect = new Rect(rect.x, rect.center.y - 12, rect.width, 24);
            return true;
        }

        private static Rect GuiRectToMainWindowRect(Rect guiRect)
        {
            Vector2 screenPoint = GUIUtility.GUIToScreenPoint(guiRect.position);
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            return new Rect(screenPoint.x - main.x, screenPoint.y - main.y, guiRect.width, guiRect.height);
        }

        private static GameObject ResolveSceneObject(string selector)
        {
            if (string.IsNullOrWhiteSpace(selector))
                return Selection.activeGameObject;

            if (selector.Equals("selected", StringComparison.OrdinalIgnoreCase) ||
                selector.Equals("selection", StringComparison.OrdinalIgnoreCase))
                return Selection.activeGameObject;

            if (selector.StartsWith("id:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(selector.Substring("id:".Length).Trim(), out int instanceId))
            {
                return EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            }

            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (!IsSceneObject(go))
                    continue;

                if (selector.StartsWith("/", StringComparison.Ordinal))
                {
                    if (string.Equals(GetHierarchyPath(go), selector, StringComparison.OrdinalIgnoreCase))
                        return go;
                }
                else if (string.Equals(go.name, selector, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(GetHierarchyPath(go).TrimStart('/'), selector, StringComparison.OrdinalIgnoreCase))
                {
                    return go;
                }
            }

            return null;
        }

        private static string FindAssetGuidByName(string selector)
        {
            string[] guids = AssetDatabase.FindAssets(selector);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileNameWithoutExtension(path), selector, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(path), selector, StringComparison.OrdinalIgnoreCase))
                {
                    return guid;
                }
            }

            return guids.Length > 0 ? guids[0] : null;
        }

        private static bool IsSceneObject(GameObject go)
        {
            if (go == null || !go.scene.IsValid())
                return false;

            return (go.hideFlags & HideFlags.HideInHierarchy) == 0 &&
                   (go.hideFlags & HideFlags.HideAndDontSave) == 0;
        }

        private static string GetHierarchyPath(GameObject go)
        {
            string path = "/" + go.name;
            Transform current = go.transform.parent;
            while (current != null)
            {
                path = "/" + current.name + path;
                current = current.parent;
            }

            return path;
        }

        private static Bounds GetWorldBounds(GameObject go)
        {
            bool hasBounds = false;
            Bounds bounds = new Bounds(go.transform.position, Vector3.zero);

            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            foreach (Collider collider in go.GetComponentsInChildren<Collider>())
            {
                if (collider == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return hasBounds ? bounds : new Bounds(go.transform.position, Vector3.zero);
        }

        private static bool TryResolveKnownEditorWindow(string target, Rect mainWindow, out Rect localRect)
        {
            localRect = Rect.zero;
            if (string.IsNullOrWhiteSpace(target))
                return false;

            string[] typeNames = GetKnownEditorWindowTypeNames(target.Trim());
            if (typeNames == null || typeNames.Length == 0)
                return false;

            Rect bestRect = Rect.zero;
            float bestArea = 0f;
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window == null)
                    continue;

                Type type = window.GetType();
                if (!MatchesKnownWindow(type, window.titleContent?.text, typeNames))
                    continue;

                if (!TryGetVisibleEditorWindowRect(window, out Rect position))
                    continue;

                if (position.width <= 1 || position.height <= 1)
                    continue;

                Rect overlap = Intersect(position, mainWindow);
                float area = overlap.width * overlap.height;
                if (area <= bestArea)
                    continue;

                bestArea = area;
                bestRect = new Rect(
                    position.x - mainWindow.x,
                    position.y - mainWindow.y,
                    position.width,
                    position.height);
            }

            localRect = bestRect;
            return bestArea > 1f;
        }

        private static bool TryGetVisibleEditorWindowRect(EditorWindow window, out Rect position)
        {
            position = Rect.zero;
            if (window == null)
                return false;

            object parent = s_editorWindowParentField?.GetValue(window);
            if (parent == null)
            {
                position = window.position;
                return true;
            }

            Type parentType = parent.GetType();
            if (!IsActualView(parent, parentType, window))
                return false;

            if (TryGetRectProperty(parent, parentType, "screenPosition", out position) ||
                TryGetRectProperty(parent, parentType, "windowPosition", out position))
            {
                return true;
            }

            position = window.position;
            return true;
        }

        private static bool IsActualView(object parent, Type parentType, EditorWindow window)
        {
            object actualView = GetMemberValue(parent, parentType, "actualView") ??
                                GetMemberValue(parent, parentType, "m_ActualView") ??
                                GetMemberValue(parent, parentType, "selected") ??
                                GetMemberValue(parent, parentType, "m_Selected");

            return actualView == null || ReferenceEquals(actualView, window);
        }

        private static bool TryGetRectProperty(object instance, Type type, string name, out Rect rect)
        {
            rect = Rect.zero;
            object value = GetMemberValue(instance, type, name);
            if (value is Rect reflectedRect)
            {
                rect = reflectedRect;
                return true;
            }

            return false;
        }

        private static object GetMemberValue(object instance, Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name, flags);
                if (property != null)
                {
                    try
                    {
                        return property.GetValue(instance, null);
                    }
                    catch
                    {
                        return null;
                    }
                }

                FieldInfo field = current.GetField(name, flags);
                if (field != null)
                {
                    try
                    {
                        return field.GetValue(instance);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            return null;
        }

        private static bool IsKnownEditorWindowTarget(string target)
        {
            return ResolveKnownEditorWindowType(target) != null;
        }

        private static Type ResolveKnownEditorWindowType(string target)
        {
            string[] typeNames = GetKnownEditorWindowTypeNames((target ?? "").Trim());
            foreach (string typeName in typeNames)
            {
                Type type = Type.GetType(typeName + ",UnityEditor");
                if (type != null)
                    return type;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (string typeName in typeNames)
                {
                    Type type = assembly.GetType(typeName);
                    if (type != null)
                        return type;
                }
            }

            return null;
        }

        private static string[] GetKnownEditorWindowTypeNames(string target)
        {
            string key = target.ToLowerInvariant();
            switch (key)
            {
                case "hierarchy":
                case "scene-hierarchy":
                    return new[] { "UnityEditor.SceneHierarchyWindow" };
                case "project":
                case "project-browser":
                    return new[] { "UnityEditor.ProjectBrowser" };
                case "inspector":
                case "inspector-window":
                    return new[] { "UnityEditor.InspectorWindow" };
                case "scene":
                case "scene-view":
                    return new[] { "UnityEditor.SceneView" };
                case "game":
                case "game-view":
                    return new[] { "UnityEditor.GameView" };
                case "console":
                    return new[] { "UnityEditor.ConsoleWindow" };
                case "animation":
                    return new[] { "UnityEditor.AnimationWindow" };
                case "animator":
                    return new[] { "UnityEditor.Graphs.AnimatorControllerTool" };
                default:
                    return Array.Empty<string>();
            }
        }

        private static bool MatchesKnownWindow(Type type, string title, string[] expectedNames)
        {
            string fullName = type.FullName ?? type.Name;
            foreach (string expected in expectedNames)
            {
                if (string.Equals(fullName, expected, StringComparison.Ordinal))
                    return true;
                if (!string.IsNullOrEmpty(title) &&
                    string.Equals(title, expected.Replace("UnityEditor.", "").Replace("Window", ""), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static Rect Intersect(Rect a, Rect b)
        {
            float xMin = Mathf.Max(a.xMin, b.xMin);
            float yMin = Mathf.Max(a.yMin, b.yMin);
            float xMax = Mathf.Min(a.xMax, b.xMax);
            float yMax = Mathf.Min(a.yMax, b.yMax);
            if (xMax <= xMin || yMax <= yMin)
                return Rect.zero;
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static Rect ResolveKnownRegion(string target, Rect main)
        {
            float w = Mathf.Max(400, main.width);
            float h = Mathf.Max(300, main.height);
            string key = target.ToLowerInvariant();

            switch (key)
            {
                case "menu":
                case "menu-bar":
                    return new Rect(0, 0, w, 22);
                case "toolbar":
                case "main-toolbar":
                    return new Rect(0, 22, w, 44);
                case "hierarchy":
                    return new Rect(0, 70, 280, Mathf.Max(120, h - 340));
                case "project":
                    return new Rect(0, Mathf.Max(90, h - 260), Mathf.Max(320, w * 0.55f), 260);
                case "inspector":
                    return new Rect(Mathf.Max(0, w - 380), 70, 380, Mathf.Max(120, h - 70));
                case "scene":
                case "scene-view":
                    return new Rect(280, 70, Mathf.Max(280, w - 660), Mathf.Max(200, h - 340));
                case "center":
                default:
                    return new Rect(w * 0.5f - 80, h * 0.5f - 40, 160, 80);
            }
        }

        private static bool IsWaitConditionSatisfied(CompanionTutorialStep step)
        {
            if (step == null)
                return false;

            string wait = (step.waitFor ?? "manual").Trim();
            if (string.IsNullOrEmpty(wait) || wait.Equals("manual", StringComparison.OrdinalIgnoreCase))
                return false;

            if (wait.StartsWith("delay:", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(wait.Substring("delay:".Length), out double seconds))
                    return EditorApplication.timeSinceStartup - s_stepStartedAt >= seconds;
                return false;
            }

            if (wait.Equals("selection", StringComparison.OrdinalIgnoreCase))
                return Selection.activeObject != null && Selection.activeObject != s_selectionAtStepStart;

            if (wait.StartsWith("assetExists:", StringComparison.OrdinalIgnoreCase))
            {
                string path = wait.Substring("assetExists:".Length).Trim();
                return !string.IsNullOrEmpty(path) && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null;
            }

            if (wait.StartsWith("packageInstalled:", StringComparison.OrdinalIgnoreCase))
            {
                string packageName = wait.Substring("packageInstalled:".Length).Trim();
                if (string.IsNullOrEmpty(packageName))
                    return false;
                return IsPackageInstalled(packageName);
            }

            if (wait.StartsWith("componentExists:", StringComparison.OrdinalIgnoreCase))
            {
                string typeName = wait.Substring("componentExists:".Length).Trim();
                Type type = ResolveType(typeName);
                return type != null && typeof(Component).IsAssignableFrom(type) &&
                       Resources.FindObjectsOfTypeAll(type).Length > 0;
            }

            if (wait.StartsWith("transformMoved:", StringComparison.OrdinalIgnoreCase))
            {
                string selector = wait.Substring("transformMoved:".Length).Trim();
                GameObject target = ResolveSceneObject(selector);
                return HasTransformChanged(target != null ? target.transform : null);
            }

            return false;
        }

        private static void CaptureTransformAtStepStart()
        {
            s_transformAtStepStart = Selection.activeTransform;
            if (s_transformAtStepStart == null)
                return;

            s_positionAtStepStart = s_transformAtStepStart.position;
            s_rotationAtStepStart = s_transformAtStepStart.rotation;
            s_scaleAtStepStart = s_transformAtStepStart.localScale;
        }

        private static bool HasTransformChanged(Transform transform)
        {
            if (transform == null || s_transformAtStepStart == null || transform != s_transformAtStepStart)
                return false;

            return transform.position != s_positionAtStepStart ||
                   transform.rotation != s_rotationAtStepStart ||
                   transform.localScale != s_scaleAtStepStart;
        }

        private static bool IsPackageInstalled(string packageName)
        {
            try
            {
                var method = typeof(UnityEditor.PackageManager.PackageInfo).GetMethod(
                    "GetAllRegisteredPackages",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method != null)
                {
                    var packages = method.Invoke(null, null) as Array;
                    if (packages != null)
                    {
                        foreach (var package in packages)
                        {
                            var nameProperty = package?.GetType().GetProperty("name");
                            string name = nameProperty?.GetValue(package, null) as string;
                            if (string.Equals(name, packageName, StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                    }
                }
            }
            catch
            {
                // Fall back to manifest files below for Unity versions without this API.
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string quotedName = "\"" + packageName + "\"";
            string manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
            if (File.Exists(manifestPath) &&
                File.ReadAllText(manifestPath).IndexOf(quotedName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string lockPath = Path.Combine(projectRoot, "Packages", "packages-lock.json");
            return File.Exists(lockPath) &&
                   File.ReadAllText(lockPath).IndexOf(quotedName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Type ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            Type type = Type.GetType(typeName);
            if (type != null)
                return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(typeName);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // Ignore dynamic/reflection-only assemblies that cannot resolve types normally.
                }
            }

            return null;
        }

        private static string GetWaitDescription(string waitFor)
        {
            string wait = (waitFor ?? "manual").Trim();
            if (string.IsNullOrEmpty(wait) || wait.Equals("manual", StringComparison.OrdinalIgnoreCase))
                return "Use Next when ready";
            if (wait.StartsWith("delay:", StringComparison.OrdinalIgnoreCase))
                return "Continuing shortly";
            if (wait.Equals("selection", StringComparison.OrdinalIgnoreCase))
                return "Waiting for a Unity selection";
            if (wait.StartsWith("assetExists:", StringComparison.OrdinalIgnoreCase))
                return "Waiting for an asset";
            if (wait.StartsWith("packageInstalled:", StringComparison.OrdinalIgnoreCase))
                return "Waiting for a package";
            if (wait.StartsWith("componentExists:", StringComparison.OrdinalIgnoreCase))
                return "Waiting for a component";
            if (wait.StartsWith("transformMoved:", StringComparison.OrdinalIgnoreCase))
                return "Waiting for the selected object to move";
            return wait;
        }
    }
}
