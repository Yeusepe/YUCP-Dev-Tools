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
        private OnboardingOverlay _onboardingOverlay;
        private const string OnboardingPrefKey = "com.yucp.devtools.packageexporter.onboarding.shown";

        private void CheckAndStartOnboarding()
        {
            if (!EditorPrefs.GetBool(OnboardingPrefKey, false))
            {
                StartOnboarding();
            }
        }

        private void StartOnboarding()
        {
            if (_onboardingOverlay == null) return;

            // Ensure we have a demo profile to show
            EnsureDemoProfile();

            var steps = new List<OnboardingStep>
            {
                new OnboardingStep(
                    "Welcome to Package Exporter", 
                    "This tool helps you create and share Unity packages easily. Let's take a quick tour to see what it can do!",
                    null // Center screen
                ),
                new OnboardingStep(
                    "Profiles & Organization", 
                    "Create and organize your **export profiles** (saved export settings) here. Think of profiles as templates for your packages. Use folders to group related packages together for easier management.",
                    "yucp-left-pane" // Needs ID or class selector ref from Layout
                ) { SpotlightPadding = new Vector4(0, 0, 0, 0) },
                new OnboardingStep(
                    "Search & Filters", 
                    "Quickly find your packages using the search bar. You can also use **filters** (like tags and folders) to narrow down your list and find exactly what you're looking for.",
                    "global-search-field"
                ),
                new OnboardingStep(
                    "Package Identity", 
                    "Set your package name, version, and description. This information is what users will see when they install your package in Unity's **Package Manager** (the built-in tool where people install packages).",
                    "package-identity-section" // Need to ensure this name exists in ProfileDetails
                ),
                new OnboardingStep(
                    "Export Inspector & Ignores", 
                    "Review all the files that will be included in your package. You can create **.yucpignore** files (similar to .gitignore files) to create exclusion lists. These tell the exporter which files or folders to skip when building your package.",
                    "export-inspector-section" // Need to ensure name exists
                ),
                new OnboardingStep(
                    "Derived Patches", 
                    "Manage **Derived Patches**. These let you share changes to 3D models (FBX files) without including the original file. It uses a technique called **binary patching** to create a small file containing only your modifications, which is perfect for editing protected or paid assets you can't redistribute.",
                    "derived-fbx-section" // Need to ensure name exists
                ),
                new OnboardingStep(
                    "Dependencies", 
                    "The tool automatically finds other packages your assets need to work. When you set up **dependencies** (other packages your package requires), users will see an installation popup when they import your package, making sure they have everything needed.",
                    "dependencies-section" // Need to ensure name exists
                ),
                new OnboardingStep(
                    "Bundled Profiles", 
                    "Create composite packages by combining multiple profiles. You can **bundle** profiles (combine all their assets into one package) or export them **side-by-side** (create separate package files at the same time).",
                    "bundled-profiles-section" // Need to ensure name exists
                ),
                new OnboardingStep(
                    "Versioning & Rules", 
                    "Set up automatic version numbering for your packages. You can use **semantic versioning** (a standard way to number versions like 1.0.0, 1.1.0, 2.0.0) that automatically updates based on rules you create.",
                    "versioning-section" 
                ),
                new OnboardingStep(
                    "Action Center", 
                    "Ready to create your package files? **Export** selected profiles (create the package file to share) or use batch export to create multiple packages at once.",
                    "yucp-bottom-bar"
                )
            };
            
            // Add sidebar auto-open/close for steps that target elements in the left pane
            // Steps 1 and 2 target "yucp-left-pane" and "global-search-field" which are in the sidebar
            Action openSidebarIfNeeded = () =>
            {
                // Check if we're in narrow/compact mode (sidebar is collapsed)
                if (rootVisualElement.ClassListContains("yucp-window-narrow") && !_isOverlayOpen)
                {
                    OpenOverlay();
                }
            };
            
            Action closeSidebarIfNeeded = () =>
            {
                // Close sidebar if it was opened for onboarding
                if (_isOverlayOpen)
                {
                    CloseOverlay();
                }
            };
            
            // Step 1 (index 1): "Profiles & Organization" - targets left pane
            steps[1].OnStepShown = openSidebarIfNeeded;
            steps[1].OnStepHidden = closeSidebarIfNeeded;
            steps[1].RequiresLayoutDelay = true; // Wait for sidebar animation to complete
            
            // Step 2 (index 2): "Search & Filters" - targets search field in left pane
            steps[2].OnStepShown = openSidebarIfNeeded;
            steps[2].OnStepHidden = closeSidebarIfNeeded;
            steps[2].RequiresLayoutDelay = true; // Wait for sidebar animation to complete

            _onboardingOverlay.style.display = DisplayStyle.Flex;
            _onboardingOverlay.Start(steps);
        }

        private void EnsureDemoProfile()
        {
            // Check if demo profile already exists
            const string demoPath = "Assets/Getting Started Demo.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ExportProfile>(demoPath);
            
            if (existing != null)
            {
                // Select it so the UI populates
                Selection.activeObject = existing;
                selectedProfile = existing;
                
                selectedProfileIndices.Clear();
                int existIndex = allProfiles.IndexOf(existing);
                if (existIndex >= 0) selectedProfileIndices.Add(existIndex);
                
                UpdateProfileList();
                UpdateProfileDetails();
                
                // Scroll sidebar
                if (existIndex >= 0 && _profileListScrollView != null)
                {
                    rootVisualElement.schedule.Execute(() => 
                    {
                        var item = _profileListScrollView.Query<VisualElement>(className: "yucp-profile-item")
                            .Where(e => e.userData is int idx && idx == existIndex).ToList().FirstOrDefault();
                        if (item != null) SmoothScrollTo(_profileListScrollView, item);
                    }).ExecuteLater(50);
                }
                
                return;
            }

            // Create new demo profile
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            profile.packageName = "com.example.demo";
            profile.version = "1.0.0";
            profile.description = "This is a demo package to help you get started with the Package Exporter.";
            profile.author = "You";
            
            profile.foldersToExport = new List<string> { "Assets" };
            
            AssetDatabase.CreateAsset(profile, demoPath);
            AssetDatabase.SaveAssets();

            // Refresh profiles list to include the new one (safely)
            if (!allProfiles.Contains(profile))
            {
                allProfiles.Add(profile);
                // Simple sort 
                allProfiles = ApplyCustomOrder(allProfiles);
            }
            
            // Select it
            Selection.activeObject = profile;
            selectedProfile = profile;
            
            // Update indices
            selectedProfileIndices.Clear();
            int index = allProfiles.IndexOf(profile);
            if (index >= 0) selectedProfileIndices.Add(index);
            
            // Force refresh UI
            UpdateProfileList();
            UpdateProfileDetails();
            
            // Scroll sidebar to item
            if (index >= 0 && _profileListScrollView != null)
            {
                // Delay to allow UI rebuild
                rootVisualElement.schedule.Execute(() => 
                {
                    var item = _profileListScrollView.Query<VisualElement>(className: "yucp-profile-item")
                        .Where(e => e.userData is int idx && idx == index).ToList().FirstOrDefault();
                        
                    if (item != null)
                    {
                         SmoothScrollTo(_profileListScrollView, item);
                    }
                }).ExecuteLater(50);
            }
        }
        
        private void SmoothScrollTo(ScrollView scrollView, VisualElement element)
        {
            if (scrollView == null || element == null) return;
            
            // If element layout is invalid (width/height 0), we might be too early. Retry.
            if (float.IsNaN(element.layout.height) || element.layout.height < 1)
            {
                scrollView.schedule.Execute(() => SmoothScrollTo(scrollView, element)).ExecuteLater(50);
                return;
            }
            
            // Calculate target offset to center the element
            // We use worldBound.y difference
            float elementY = element.worldBound.y;
            float contentY = scrollView.contentContainer.worldBound.y;
            
            // If worldBounds are suspiciously zero, maybe layout pass hasn't run.
            // But usually safely handled by the retry above if layout is empty.
            
            // relativeY: position of element inside the scrollable content area
            // relativeY = (element world y) - (scroll container world y) + (current scroll offset)
            float relativeY = element.worldBound.y - scrollView.worldBound.y + scrollView.verticalScroller.value;
            
            float viewportHeight = scrollView.layout.height;
            float elementHeight = element.layout.height;
            
            float targetOffset = Mathf.Max(0, relativeY - (viewportHeight / 2) + (elementHeight / 2));
            float startOffset = scrollView.verticalScroller.value;
            
            // Ensure target is valid
            if (float.IsNaN(targetOffset)) targetOffset = 0;
            
            float duration = 0.4f;
            double startTime = EditorApplication.timeSinceStartup;
            
            IVisualElementScheduledItem anim = null;
            anim = scrollView.schedule.Execute(() =>
            {
                if (scrollView == null) return;
                
                float t = (float)(EditorApplication.timeSinceStartup - startTime) / duration;
                if (t >= 1f)
                {
                    scrollView.verticalScroller.value = targetOffset;
                    return; 
                }
                
                float smoothT = Mathf.SmoothStep(0, 1, t);
                scrollView.verticalScroller.value = Mathf.Lerp(startOffset, targetOffset, smoothT);
                
                anim.ExecuteLater(10);
            });
        }

        private void OnEnable()
        {
            // Initialize Motion system
            global::YUCP.Motion.Motion.Initialize();
            
            LoadProfiles();
            LoadProjectFolders();
            LoadResources();
            LoadTagColors(); // Load tag colors
            
            // Migrate to unified order if needed
            MigrateToUnifiedOrder();
            LoadUnifiedOrder();
            
            // Refresh dependencies on domain reload
            EditorApplication.delayCall += RefreshDependenciesOnDomainReload;
            
            // Register update for gap animation (vFavorites approach)
            EditorApplication.update -= UpdateGapAnimations;
            EditorApplication.update += UpdateGapAnimations;
        }

        // Tag Color Management
        private Dictionary<string, Color> _tagColors = new Dictionary<string, Color>();
        private const string TagColorsPrefKey = "YUCP_GlobalTagColors";

        private void LoadTagColors()
        {
            _tagColors.Clear();
            string rawTagColors = EditorPrefs.GetString(TagColorsPrefKey, "");
            if (!string.IsNullOrEmpty(rawTagColors))
            {
                var entries = rawTagColors.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var entry in entries)
                {
                    var parts = entry.Split(':');
                    if (parts.Length == 2 && ColorUtility.TryParseHtmlString("#" + parts[1], out var color))
                    {
                        _tagColors[parts[0]] = color;
                    }
                }
            }
            
            // Assign default colors for presets if not set
            if (!_tagColors.ContainsKey("Production")) _tagColors["Production"] = new Color(0.29f, 0.87f, 0.5f);
            if (!_tagColors.ContainsKey("Beta")) _tagColors["Beta"] = new Color(0.98f, 0.57f, 0.24f); 
            if (!_tagColors.ContainsKey("Archived")) _tagColors["Archived"] = new Color(0.61f, 0.64f, 0.69f); 
            if (!_tagColors.ContainsKey("Active")) _tagColors["Active"] = new Color(0.21f, 0.75f, 0.69f);
            if (!_tagColors.ContainsKey("Deprecated")) _tagColors["Deprecated"] = new Color(0.97f, 0.44f, 0.44f);
            if (!_tagColors.ContainsKey("Experimental")) _tagColors["Experimental"] = new Color(0.75f, 0.52f, 0.99f);
            if (!_tagColors.ContainsKey("Stable")) _tagColors["Stable"] = new Color(0.38f, 0.65f, 0.98f);
            if (!_tagColors.ContainsKey("WIP")) _tagColors["WIP"] = new Color(0.98f, 0.75f, 0.14f);
        }

        private void SaveTagColors()
        {
            var colorList = _tagColors.Select(kvp => $"{kvp.Key}:{ColorUtility.ToHtmlStringRGB(kvp.Value)}");
            EditorPrefs.SetString(TagColorsPrefKey, string.Join("|", colorList));
        }

        public Color GetTagColor(string tag)
        {
            return _tagColors.TryGetValue(tag, out var color) ? color : new Color(0.21f, 0.75f, 0.69f); // Default Teal
        }

        public void SetTagColor(string tag, Color color)
        {
            _tagColors[tag] = color;
            SaveTagColors();
        }

        public void DeleteGlobalTag(string tag)
        {
            // Remove from global tags list
            string rawGlobalTags = EditorPrefs.GetString("YUCP_GlobalCustomTags", "");
            var globalTags = !string.IsNullOrEmpty(rawGlobalTags) 
                ? rawGlobalTags.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList() 
                : new List<string>();

            if (globalTags.Contains(tag))
            {
                globalTags.Remove(tag);
                EditorPrefs.SetString("YUCP_GlobalCustomTags", string.Join("|", globalTags));
            }
            
            // Remove color
            if (_tagColors.ContainsKey(tag))
            {
                _tagColors.Remove(tag);
                SaveTagColors();
            }
            
            UpdateProfileList();
            UpdateProfileDetails();
        }

        private void ShowTagContextMenu(string tag, VisualElement modalTarget)
        {
             var contextMenu = new VisualElement();
             contextMenu.AddToClassList("yucp-popover");
             contextMenu.style.position = Position.Absolute;
             contextMenu.style.width = 180;
             contextMenu.style.paddingLeft = 12;
             contextMenu.style.paddingRight = 12;
             contextMenu.style.paddingTop = 12;
             contextMenu.style.paddingBottom = 12;
             
             // 1. Color Grid
             var colorLabel = new Label("Tag Color");
             colorLabel.AddToClassList("yucp-section-header");
             colorLabel.style.fontSize = 10;
             colorLabel.style.marginBottom = 8;
             contextMenu.Add(colorLabel);

             var colorGrid = new VisualElement();
             colorGrid.style.flexDirection = FlexDirection.Row;
             colorGrid.style.flexWrap = Wrap.Wrap;
             colorGrid.style.marginBottom = 12;

             // Preset colors same as TokenizedTagInput
             var presetColors = new[]
             {
                new Color(0.21f, 0.75f, 0.69f), // Default Teal (#36BFB1)
                new Color(0.61f, 0.64f, 0.69f), // Gray (#9ca3af)
                new Color(0.97f, 0.44f, 0.44f), // Red (#f87171)
                new Color(0.98f, 0.57f, 0.24f), // Orange (#fb923c)
                new Color(0.98f, 0.75f, 0.14f), // Amber (#fbbf24)
                new Color(0.29f, 0.87f, 0.5f),  // Green (#4ade80)
                new Color(0.38f, 0.65f, 0.98f), // Blue (#60a5fa)
                new Color(0.51f, 0.55f, 0.97f), // Indigo (#818cf8)
                new Color(0.75f, 0.52f, 0.99f), // Purple (#c084fc)
                new Color(0.96f, 0.45f, 0.71f)  // Pink (#f472b6)
             };

             // Helper to manage user palette
             Func<List<Color>> GetUserPalette = () => {
                 string raw = EditorPrefs.GetString("YUCP_UserPalette", "");
                 if (string.IsNullOrEmpty(raw)) return new List<Color>();
                 return raw.Split('|').Select(s => ColorUtility.TryParseHtmlString("#" + s, out var c) ? c : Color.white).ToList();
             };
             
             Action<Color> AddToUserPalette = (c) => {
                 var list = GetUserPalette();
                 if(list.Any(existing => existing == c)) return; // No dupe
                 list.Insert(0, c); // Add to front
                 if(list.Count > 10) list.RemoveAt(list.Count - 1); // Limit to 10
                 EditorPrefs.SetString("YUCP_UserPalette", string.Join("|", list.Select(col => ColorUtility.ToHtmlStringRGB(col))));
             };

             // 1. Color Grid (Presets + User)
             Action rebuildGrid = null;
             rebuildGrid = () => {
                 colorGrid.Clear();
                 
                 // Standard Presets
                 foreach (var color in presetColors)
                 {
                     var swatch = new VisualElement();
                     swatch.style.width = 18; swatch.style.height = 18;
                     swatch.style.marginRight = 4; swatch.style.marginBottom = 4;
                     swatch.style.borderTopLeftRadius = 9; swatch.style.borderTopRightRadius = 9;
                     swatch.style.borderBottomLeftRadius = 9; swatch.style.borderBottomRightRadius = 9;
                     swatch.style.backgroundColor = color;
                     
                     // Border
                     swatch.style.borderTopWidth=1; swatch.style.borderBottomWidth=1;
                     swatch.style.borderLeftWidth=1; swatch.style.borderRightWidth=1;
                     swatch.style.borderTopColor=new Color(1,1,1,0.1f); swatch.style.borderBottomColor=new Color(1,1,1,0.1f);
                     swatch.style.borderLeftColor=new Color(1,1,1,0.1f); swatch.style.borderRightColor=new Color(1,1,1,0.1f);
                     
                     swatch.RegisterCallback<MouseDownEvent>(e => {
                         if(e.button == 0) {
                             SetTagColor(tag, color);
                             UpdateProfileList();
                             UpdateProfileDetails();
                             contextMenu.RemoveFromHierarchy();
                             e.StopPropagation();
                         }
                     });
                     colorGrid.Add(swatch);
                 }
                 
                 // Separator if custom colors exist
                 var userColors = GetUserPalette();
                 if (userColors.Count > 0)
                 {
                     var sep = new VisualElement();
                     sep.style.width = 4; sep.style.height = 18;
                     colorGrid.Add(sep);
                     
                     foreach (var color in userColors)
                     {
                         var swatch = new VisualElement();
                         swatch.style.width = 18; swatch.style.height = 18;
                         swatch.style.marginRight = 4; swatch.style.marginBottom = 4;
                         swatch.style.borderTopLeftRadius = 9; swatch.style.borderTopRightRadius = 9;
                         swatch.style.borderBottomLeftRadius = 9; swatch.style.borderBottomRightRadius = 9;
                         swatch.style.backgroundColor = color;
                         
                         swatch.style.borderTopWidth=1; swatch.style.borderBottomWidth=1;
                         swatch.style.borderLeftWidth=1; swatch.style.borderRightWidth=1;
                         swatch.style.borderTopColor=new Color(1,1,1,0.1f); swatch.style.borderBottomColor=new Color(1,1,1,0.1f);
                         swatch.style.borderLeftColor=new Color(1,1,1,0.1f); swatch.style.borderRightColor=new Color(1,1,1,0.1f);
                         
                         swatch.RegisterCallback<MouseDownEvent>(e => {
                             if(e.button == 0) {
                                 SetTagColor(tag, color);
                                 UpdateProfileList();
                                 UpdateProfileDetails();
                                 contextMenu.RemoveFromHierarchy();
                                 e.StopPropagation();
                             }
                         });
                         colorGrid.Add(swatch);
                     }
                 }
                 
                 // Custom (+) Button
                 var customContainer = new VisualElement();
                 customContainer.style.width = 18; customContainer.style.height = 18;
                 customContainer.style.marginRight = 4; customContainer.style.marginBottom = 4;
                 customContainer.style.justifyContent = Justify.Center;
                 customContainer.style.alignItems = Align.Center;
                 
                 customContainer.style.borderTopLeftRadius = 9; customContainer.style.borderTopRightRadius = 9;
                 customContainer.style.borderBottomLeftRadius = 9; customContainer.style.borderBottomRightRadius = 9;
                 
                 customContainer.style.backgroundColor = new Color(0,0,0,0.1f);
                 customContainer.style.borderTopWidth = 1; customContainer.style.borderBottomWidth = 1;
                 customContainer.style.borderLeftWidth = 1; customContainer.style.borderRightWidth = 1;
                 customContainer.style.borderTopColor=new Color(1,1,1,0.2f); customContainer.style.borderBottomColor=new Color(1,1,1,0.2f);
                 customContainer.style.borderLeftColor=new Color(1,1,1,0.2f); customContainer.style.borderRightColor=new Color(1,1,1,0.2f);
                 
                 var plusLabel = new Label("+");
                 plusLabel.style.color = new Color(1,1,1,0.5f);
                 plusLabel.style.fontSize = 12;
                 plusLabel.style.paddingTop = 0; plusLabel.style.paddingBottom = 0;
                 plusLabel.style.paddingLeft = 0; plusLabel.style.paddingRight = 0;
                 customContainer.Add(plusLabel);
                 
                 customContainer.RegisterCallback<MouseDownEvent>(e => 
                 {
                     if (e.button == 0)
                     {
                         // Switch to Embedded Color Picker View
                         contextMenu.Clear();
                         contextMenu.style.width = 230; // Slightly wider
                         
                         var baseColor = GetTagColor(tag);
                         float currentH, currentS, currentV;
                         Color.RGBToHSV(baseColor, out currentH, out currentS, out currentV);
                         
                         // Better texture gen
                         Texture2D GenerateCusomTexture(int w, int h, Func<int, int, Color> pixelFunc) {
                             var tex = new Texture2D(w, h);
                             var pix = new Color[w * h];
                             for(int y=0; y<h; y++) {
                                 for(int x=0; x<w; x++) {
                                     pix[y*w + x] = pixelFunc(x, y);
                                 }
                             }
                             tex.SetPixels(pix);
                             tex.filterMode = FilterMode.Bilinear;
                             tex.wrapMode = TextureWrapMode.Clamp;
                             tex.Apply();
                             return tex;
                          }
    
                          var root = new VisualElement();
                          root.style.paddingLeft = 8; root.style.paddingRight = 8;
                          root.style.paddingTop = 8; root.style.paddingBottom = 8;
    
                          // 1. Sat/Val Box
                          var svBox = new VisualElement();
                          svBox.style.width = 210; svBox.style.height = 150;
                          svBox.style.marginBottom = 12;
                          svBox.style.borderTopWidth = 1; svBox.style.borderBottomWidth = 1;
                          svBox.style.borderLeftWidth = 1; svBox.style.borderRightWidth = 1;
                          svBox.style.borderTopColor = new Color(0,0,0,0.5f);
                          svBox.style.borderBottomColor = new Color(0,0,0,0.5f);
                          svBox.style.borderLeftColor = new Color(0,0,0,0.5f);
                          svBox.style.borderRightColor = new Color(0,0,0,0.5f);
                          
                          var hueBg = new VisualElement();
                          hueBg.style.flexGrow = 1;
                          svBox.Add(hueBg);
                          
                          var satGrad = new VisualElement();
                          satGrad.style.position = Position.Absolute;
                          satGrad.style.top=0; satGrad.style.bottom=0; satGrad.style.left=0; satGrad.style.right=0;
                          satGrad.style.backgroundImage = GenerateCusomTexture(2, 1, (x,y) => x==0 ? Color.white : new Color(1,1,1,0));
                          svBox.Add(satGrad);
                          
                          var valGrad = new VisualElement();
                          valGrad.style.position = Position.Absolute;
                          valGrad.style.top=0; valGrad.style.bottom=0; valGrad.style.left=0; valGrad.style.right=0;
                          valGrad.style.backgroundImage = GenerateCusomTexture(1, 2, (x,y) => y==1 ? new Color(0,0,0,0) : Color.black);
                          svBox.Add(valGrad);
                          
                          var svHandle = new VisualElement();
                          svHandle.style.width = 12; svHandle.style.height = 12;
                          svHandle.style.position = Position.Absolute;
                          svHandle.style.borderTopLeftRadius = 6; svHandle.style.borderTopRightRadius = 6;
                          svHandle.style.borderBottomLeftRadius = 6; svHandle.style.borderBottomRightRadius = 6;
                          svHandle.style.borderTopWidth=2; svHandle.style.borderBottomWidth=2; // Thicker border
                          svHandle.style.borderLeftWidth=2; svHandle.style.borderRightWidth=2;
                          svHandle.style.borderTopColor=Color.white; svHandle.style.borderBottomColor=Color.white;
                          svHandle.style.borderLeftColor=Color.white; svHandle.style.borderRightColor=Color.white;
                          svHandle.style.backgroundColor = Color.clear;
                          // Shadow for better visibility
                          var shadow = new VisualElement();
                          shadow.style.position = Position.Absolute;
                          shadow.style.top = -1; shadow.style.bottom = -1; shadow.style.left = -1; shadow.style.right = -1;
                          shadow.style.borderTopWidth=1; shadow.style.borderBottomWidth=1;
                          shadow.style.borderLeftWidth=1; shadow.style.borderRightWidth=1;
                          shadow.style.borderTopColor=new Color(0,0,0,0.5f); shadow.style.borderBottomColor=new Color(0,0,0,0.5f);
                          shadow.style.borderLeftColor=new Color(0,0,0,0.5f); shadow.style.borderRightColor=new Color(0,0,0,0.5f);
                          shadow.style.borderTopLeftRadius = 7; shadow.style.borderTopRightRadius = 7;
                          shadow.style.borderBottomLeftRadius = 7; shadow.style.borderBottomRightRadius = 7;
                          svHandle.Add(shadow);
                          svBox.Add(svHandle);
    
                          // 2. Hue Slider
                          var hueSlider = new VisualElement();
                          hueSlider.style.width = 210; hueSlider.style.height = 16;
                          hueSlider.style.marginBottom = 12;
                          hueSlider.style.borderTopWidth=1; hueSlider.style.borderBottomWidth=1;
                          hueSlider.style.borderLeftWidth=1; hueSlider.style.borderRightWidth=1;
                          hueSlider.style.borderTopColor=new Color(0,0,0,0.3f);
                          hueSlider.style.borderBottomColor=new Color(0,0,0,0.3f);
                          hueSlider.style.borderLeftColor=new Color(0,0,0,0.3f);
                          hueSlider.style.borderRightColor=new Color(0,0,0,0.3f);
                          hueSlider.style.borderTopLeftRadius = 3; hueSlider.style.borderTopRightRadius = 3;
                          hueSlider.style.borderBottomLeftRadius = 3; hueSlider.style.borderBottomRightRadius = 3;
                          
                          hueSlider.style.backgroundImage = GenerateCusomTexture(256, 1, (x,y) => Color.HSVToRGB((float)x/255f, 1, 1));
                          
                          var hueHandle = new VisualElement();
                          hueHandle.style.width = 4; hueHandle.style.height = 16;
                          hueHandle.style.position = Position.Absolute;
                          hueHandle.style.backgroundColor = Color.white;
                          hueHandle.style.borderTopWidth=1; hueHandle.style.borderBottomWidth=1;
                          hueHandle.style.borderLeftWidth=1; hueHandle.style.borderRightWidth=1;
                          hueHandle.style.borderTopColor=new Color(0,0,0,0.5f); hueHandle.style.borderBottomColor=new Color(0,0,0,0.5f);
                          hueHandle.style.borderLeftColor=new Color(0,0,0,0.5f); hueHandle.style.borderRightColor=new Color(0,0,0,0.5f);
                          hueSlider.Add(hueHandle);
    
                          // 3. Save Button
                          var saveBtn = new Button(() => {
                              // Save to palette logic
                              Color c = Color.HSVToRGB(currentH, currentS, currentV);
                              AddToUserPalette(c);
                              // Rebuild main menu
                              contextMenu.Clear();
                              // Add grid back
                              var label = new Label("Tag Color");
                              label.AddToClassList("yucp-section-header");
                              label.style.fontSize = 10;
                              label.style.marginBottom = 8;
                              contextMenu.style.width = 180; // Reset width
                              contextMenu.style.paddingLeft = 12; contextMenu.style.paddingRight = 12;
                              contextMenu.Add(label);
                              
                              colorGrid.Clear();
                              rebuildGrid(); 
                              contextMenu.Add(colorGrid);
                              
                              // Separator
                              var div = new VisualElement();
                              div.style.height = 1; div.style.backgroundColor = new Color(1,1,1,0.1f); div.style.marginBottom = 8;
                              contextMenu.Add(div);
                              
                              // Delete Button
                              var deleteBtn = new Button();
                              deleteBtn.text = "Delete Tag Globally";
                              deleteBtn.AddToClassList("yucp-button-danger");
                              deleteBtn.style.fontSize = 10; deleteBtn.style.height = 24; deleteBtn.style.width = Length.Percent(100);
                              deleteBtn.clicked += () => { 
                                   // Re-implement delete logic
                                    if (EditorUtility.DisplayDialog("Delete Tag", $"Are you sure you want to delete the tag '{tag}' globally?", "Delete", "Cancel"))
                                     {
                                         DeleteGlobalTag(tag);
                                         contextMenu.RemoveFromHierarchy(); 
                                     } 
                              };
                              contextMenu.Add(deleteBtn);
                              
                          });
                          saveBtn.text = "Save Color";
                          saveBtn.style.height = 24;
                          saveBtn.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
                          saveBtn.style.color = Color.white;
                          saveBtn.style.borderTopWidth=0; saveBtn.style.borderBottomWidth=0;
                          saveBtn.style.borderLeftWidth=0; saveBtn.style.borderRightWidth=0;
                          saveBtn.style.fontSize = 11;
                          root.Add(saveBtn);
    
                          // Update Logic
                          Action updateColor = () => {
                              Color c = Color.HSVToRGB(currentH, currentS, currentV);
                              SetTagColor(tag, c);
                              
                              hueBg.style.backgroundColor = Color.HSVToRGB(currentH, 1, 1);
                              
                              float handleX = currentS * 210f;
                              float handleY = (1f - currentV) * 150f;
                              svHandle.style.left = Mathf.Clamp(handleX - 6, -6, 204);
                              svHandle.style.top = Mathf.Clamp(handleY - 6, -6, 144);
                              svHandle.style.borderTopColor = currentV > 0.5f ? Color.black : Color.white;
                              
                              hueHandle.style.left = Mathf.Clamp((currentH * 210f) - 2, -2, 208);
                              
                              UpdateProfileList();
                              UpdateProfileDetails();
                          };
    
                          // Input Handling
                          svBox.RegisterCallback<MouseDownEvent>(evt => {
                              if(evt.button == 0) {
                                  svBox.CaptureMouse();
                                  currentS = Mathf.Clamp01(evt.localMousePosition.x / 210f);
                                  currentV = 1f - Mathf.Clamp01(evt.localMousePosition.y / 150f);
                                  updateColor();
                              }
                          });
                          svBox.RegisterCallback<MouseMoveEvent>(evt => {
                              if(svBox.HasMouseCapture()) {
                                  currentS = Mathf.Clamp01(evt.localMousePosition.x / 210f);
                                  currentV = 1f - Mathf.Clamp01(evt.localMousePosition.y / 150f);
                                  updateColor();
                              }
                          });
                          svBox.RegisterCallback<MouseUpEvent>(evt => { if(svBox.HasMouseCapture()) svBox.ReleaseMouse(); });
    
                          hueSlider.RegisterCallback<MouseDownEvent>(evt => {
                              if(evt.button == 0) {
                                  hueSlider.CaptureMouse();
                                  currentH = Mathf.Clamp01(evt.localMousePosition.x / 210f);
                                  updateColor();
                              }
                          });
                          hueSlider.RegisterCallback<MouseMoveEvent>(evt => {
                              if(hueSlider.HasMouseCapture()) {
                                  currentH = Mathf.Clamp01(evt.localMousePosition.x / 210f);
                                  updateColor();
                              }
                          });
                          hueSlider.RegisterCallback<MouseUpEvent>(evt => { if(hueSlider.HasMouseCapture()) hueSlider.ReleaseMouse(); });
    
                          root.Add(svBox);
                          root.Add(hueSlider);
                          root.Add(saveBtn);
                          contextMenu.Add(root);
                          
                          updateColor();
                          
                          e.StopPropagation();
                     }
                 });
                 
                 colorGrid.Add(customContainer);
             };
             
             rebuildGrid();
             
             contextMenu.Add(colorGrid);

             // Dividend
             var div = new VisualElement();
             div.style.height = 1;
             div.style.backgroundColor = new Color(1,1,1,0.1f);
             div.style.marginBottom = 8;
             contextMenu.Add(div);

             // 2. Delete Action
             var deleteBtn = new Button(() => 
             {
                 if (EditorUtility.DisplayDialog("Delete Tag", $"Are you sure you want to delete the tag '{tag}' globally? This cannot be undone.", "Delete", "Cancel"))
                 {
                     DeleteGlobalTag(tag);
                     UpdateProfileList();
                     UpdateProfileDetails();
                 }
                 contextMenu.RemoveFromHierarchy();
             });
             deleteBtn.text = "Delete Tag Globally";
             deleteBtn.AddToClassList("yucp-button-danger");
             deleteBtn.style.fontSize = 10;
             deleteBtn.style.height = 24;
             deleteBtn.style.width = Length.Percent(100);
             contextMenu.Add(deleteBtn);
             
             // Add dismiss overlay
             var overlay = new VisualElement();
             overlay.style.position = Position.Absolute;
             overlay.style.top = 0;
             overlay.style.bottom = 0;
             overlay.style.left = 0;
             overlay.style.right = 0;
             overlay.RegisterCallback<MouseDownEvent>(e => 
             {
                 contextMenu.RemoveFromHierarchy();
                 overlay.RemoveFromHierarchy();
                 e.StopPropagation();
             });

             rootVisualElement.Add(overlay);
             rootVisualElement.Add(contextMenu);
             
             // Position
             var targetBound = modalTarget.worldBound;
             var rootBound = rootVisualElement.worldBound;
             contextMenu.style.left = targetBound.x - rootBound.x;
             contextMenu.style.top = targetBound.yMax - rootBound.y + 4;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateGapAnimations;
        }

        private void RefreshDependenciesOnDomainReload()
        {
            // Refresh dependencies for all profiles that have dependencies configured
            if (selectedProfile != null && selectedProfile.dependencies.Count > 0)
            {
                // Silently refresh dependencies to pick up newly installed packages
                ScanProfileDependencies(selectedProfile, silent: true);
            }
        }

        private void LoadResources()
        {
            logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.devtools/Resources/DevTools.png");
            CreateBannerGradientTexture();
            CreateDottedBorderTexture();
        }

        private static bool IsDefaultGridPlaceholder(Texture2D texture)
        {
            if (texture == null) return false;
            string assetPath = AssetDatabase.GetAssetPath(texture);
            return assetPath == DefaultGridPlaceholderPath;
        }

        private static Texture2D GetPlaceholderTexture()
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultGridPlaceholderPath);
        }
        [MenuItem("Tools/YUCP/Package Exporter")]
        public static void ShowWindow()
        {
            var window = GetWindow<YUCPPackageExporterWindow>();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.devtools/Resources/DevTools.png");
            window.titleContent = new GUIContent("YUCP Package Exporter", icon);
            window.minSize = new Vector2(800, 700); // Increased default window size
            window.Show();
        }

    }
}
