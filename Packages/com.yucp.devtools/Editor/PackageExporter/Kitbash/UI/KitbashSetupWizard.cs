#if YUCP_KITBASH_ENABLED
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.DevTools.Editor.PackageExporter.Kitbash.UI
{
    /// <summary>
    /// Compact setup wizard for kitbash configurations.
    /// Uses UI Toolkit with minimal, modern design.
    /// </summary>
    public class KitbashSetupWizard : EditorWindow
    {
        private int _step = 0;
        private GameObject _derivedFbx;
        private List<GameObject> _sources = new List<GameObject>();
        private bool _autoFill = true;
        private string _displayName;
        
        [MenuItem("YUCP/Kitbash/Setup Wizard...", priority = 100)]
        public static void ShowWindow()
        {
            var window = GetWindow<KitbashSetupWizard>("Kitbash Setup");
            window.minSize = new Vector2(340, 400);
            window.maxSize = new Vector2(400, 500);
            window.Show();
        }
        
        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);
            root.style.paddingTop = 12;
            root.style.paddingBottom = 12;
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;
            
            // Step indicator
            var stepRow = new VisualElement();
            stepRow.style.flexDirection = FlexDirection.Row;
            stepRow.style.justifyContent = Justify.Center;
            stepRow.style.marginBottom = 16;
            
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var dot = new VisualElement();
                dot.name = $"step-{i}";
                dot.style.width = 8;
                dot.style.height = 8;
                // Set all border radius values for circular shape
                dot.style.borderTopLeftRadius = 4;
                dot.style.borderTopRightRadius = 4;
                dot.style.borderBottomLeftRadius = 4;
                dot.style.borderBottomRightRadius = 4;
                dot.style.marginLeft = 4;
                dot.style.marginRight = 4;
                dot.style.backgroundColor = _step >= i ? new Color(0.21f, 0.75f, 0.69f) : new Color(0.3f, 0.3f, 0.3f);
                stepRow.Add(dot);
            }
            root.Add(stepRow);
            
            // Content container (changes per step)
            var content = new VisualElement();
            content.name = "content";
            content.style.flexGrow = 1;
            root.Add(content);
            
            // Navigation
            var nav = new VisualElement();
            nav.style.flexDirection = FlexDirection.Row;
            nav.style.marginTop = 16;
            
            var backBtn = new Button(() => GoToStep(_step - 1)) { text = "← Back" };
            backBtn.name = "back-btn";
            backBtn.style.width = 70;
            backBtn.SetEnabled(_step > 0);
            nav.Add(backBtn);
            
            nav.Add(new VisualElement { style = { flexGrow = 1 } });
            
            var nextBtn = new Button(() => GoToStep(_step + 1)) { text = "Next →" };
            nextBtn.name = "next-btn";
            nextBtn.style.width = 80;
            nextBtn.style.backgroundColor = new Color(0.21f, 0.75f, 0.69f);
            nav.Add(nextBtn);
            
            root.Add(nav);
            
            // Initial render
            RenderStep();
        }
        
        private void GoToStep(int step)
        {
            if (step < 0) return;
            if (step >= 3)
            {
                Finish();
                return;
            }
            _step = step;
            RenderStep();
        }
        
        private void RenderStep()
        {
            var content = rootVisualElement.Q("content");
            content.Clear();
            
            // Update step indicators
            for (int i = 0; i < 3; i++)
            {
                var dot = rootVisualElement.Q($"step-{i}");
                if (dot != null)
                    dot.style.backgroundColor = _step >= i ? new Color(0.21f, 0.75f, 0.69f) : new Color(0.3f, 0.3f, 0.3f);
            }
            
            var backBtn = rootVisualElement.Q<Button>("back-btn");
            var nextBtn = rootVisualElement.Q<Button>("next-btn");
            backBtn?.SetEnabled(_step > 0);
            
            switch (_step)
            {
                case 0: RenderStep1(content); break;
                case 1: RenderStep2(content); break;
                case 2: RenderStep3(content); break;
            }
            
            // Update next button state
            bool canProceed = _step == 0 ? _derivedFbx != null :
                              _step == 1 ? _sources.Exists(s => s != null) : true;
            nextBtn?.SetEnabled(canProceed);
            if (nextBtn != null)
                nextBtn.text = _step == 2 ? "Finish ✓" : "Next →";
        }
        
        private void RenderStep1(VisualElement content)
        {
            var title = new Label("1. Select Your Mesh");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            content.Add(title);
            
            var desc = new Label("Choose the FBX with your edited mesh that you want to distribute.");
            desc.style.fontSize = 11;
            desc.style.color = new Color(1, 1, 1, 0.6f);
            desc.style.marginBottom = 12;
            desc.style.whiteSpace = WhiteSpace.Normal;
            content.Add(desc);
            
            var field = new ObjectField("Derived FBX");
            field.objectType = typeof(GameObject);
            field.allowSceneObjects = false;
            field.value = _derivedFbx;
            field.RegisterValueChangedCallback(e => {
                _derivedFbx = e.newValue as GameObject;
                if (_derivedFbx != null)
                    _displayName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(_derivedFbx));
                RenderStep();
            });
            content.Add(field);
            
            if (_derivedFbx != null)
            {
                var mesh = GetMesh(_derivedFbx);
                if (mesh != null)
                {
                    var info = new Label($"▸ {mesh.vertexCount:N0} verts · {mesh.triangles.Length / 3:N0} tris · {mesh.blendShapeCount} shapes");
                    info.style.fontSize = 10;
                    info.style.color = new Color(1, 1, 1, 0.5f);
                    info.style.marginTop = 8;
                    content.Add(info);
                }
            }
        }
        
        private void RenderStep2(VisualElement content)
        {
            var title = new Label("2. Add Source FBXs");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            content.Add(title);
            
            var desc = new Label("Add the original FBXs that users need to own.");
            desc.style.fontSize = 11;
            desc.style.color = new Color(1, 1, 1, 0.6f);
            desc.style.marginBottom = 12;
            content.Add(desc);
            
            // Source list
            var list = new VisualElement();
            list.style.marginBottom = 8;
            
            for (int i = 0; i < _sources.Count; i++)
            {
                int idx = i;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = 4;
                
                var field = new ObjectField();
                field.objectType = typeof(GameObject);
                field.allowSceneObjects = false;
                field.value = _sources[i];
                field.style.flexGrow = 1;
                field.RegisterValueChangedCallback(e => {
                    _sources[idx] = e.newValue as GameObject;
                    RenderStep();
                });
                row.Add(field);
                
                var removeBtn = new Button(() => { _sources.RemoveAt(idx); RenderStep(); }) { text = "×" };
                removeBtn.style.width = 24;
                removeBtn.style.marginLeft = 4;
                row.Add(removeBtn);
                
                list.Add(row);
            }
            content.Add(list);
            
            var addBtn = new Button(() => { _sources.Add(null); RenderStep(); }) { text = "+ Add Source" };
            addBtn.style.height = 24;
            content.Add(addBtn);
        }
        
        private void RenderStep3(VisualElement content)
        {
            var title = new Label("3. Configure");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            content.Add(title);
            
            var nameField = new TextField("Display Name");
            nameField.value = _displayName ?? "";
            nameField.RegisterValueChangedCallback(e => _displayName = e.newValue);
            content.Add(nameField);
            
            var autoFillToggle = new Toggle("Auto-fill from submeshes");
            autoFillToggle.value = _autoFill;
            autoFillToggle.RegisterValueChangedCallback(e => _autoFill = e.newValue);
            autoFillToggle.style.marginTop = 12;
            content.Add(autoFillToggle);
            
            // Summary
            var summary = new VisualElement();
            summary.style.marginTop = 20;
            summary.style.paddingTop = 12;
            summary.style.borderTopWidth = 1;
            summary.style.borderTopColor = new Color(1, 1, 1, 0.1f);
            
            var sumTitle = new Label("Summary");
            sumTitle.style.fontSize = 11;
            sumTitle.style.color = new Color(1, 1, 1, 0.5f);
            sumTitle.style.marginBottom = 4;
            summary.Add(sumTitle);
            
            summary.Add(new Label($"Mesh: {(_derivedFbx?.name ?? "—")}") { style = { fontSize = 11 } });
            summary.Add(new Label($"Sources: {_sources.FindAll(s => s != null).Count}") { style = { fontSize = 11 } });
            
            content.Add(summary);
        }
        
        private void Finish()
        {
            if (_derivedFbx == null) return;
            
            string path = AssetDatabase.GetAssetPath(_derivedFbx);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            
            if (importer != null)
            {
                var settings = new DerivedSettings
                {
                    mode = DerivedMode.KitbashRecipeHdiff,
                    friendlyName = _displayName
                };
                importer.userData = JsonUtility.ToJson(settings);
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                
                KitbashStage.Enter(importer, settings);
                
                if (_autoFill && KitbashStage.Current != null)
                    KitbashStage.Current.AutoFillFromSubmeshes();
            }
            
            Close();
        }
        
        private static Mesh GetMesh(GameObject go)
        {
            var smr = go?.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null) return smr.sharedMesh;
            var mf = go?.GetComponentInChildren<MeshFilter>();
            return mf?.sharedMesh;
        }
    }
}
#endif
