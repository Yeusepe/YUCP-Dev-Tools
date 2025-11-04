using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	public class AvatarUploaderWindow : EditorWindow
	{
		[MenuItem("Tools/YUCP/Avatar Uploader")]
		public static void ShowWindow()
		{
			var window = GetWindow<AvatarUploaderWindow>();
			window.titleContent = new GUIContent("YUCP Avatar Uploader");
			window.minSize = new Vector2(780, 600);
			window.Show();
		}

		private readonly List<AvatarUploadProfile> _profiles = new List<AvatarUploadProfile>();
		private int _selectedIndex = -1;
		private Vector2 _leftScroll;
		private Vector2 _rightScroll;

		private bool _isBuilding;
		private float _progress;
		private string _status = string.Empty;

		// Styling (mirror Package Exporter look)
		private Texture2D _logoTexture;
		private GUIStyle _headerStyle;
		private GUIStyle _sectionHeaderStyle;
		private GUIStyle _profileButtonStyle;
		private GUIStyle _selectedProfileButtonStyle;

		private void OnEnable()
		{
			ReloadProfiles();
			LoadResources();
		}

		private void ReloadProfiles()
		{
			_profiles.Clear();
			string[] guids = AssetDatabase.FindAssets("t:AvatarUploadProfile");
			foreach (var guid in guids)
			{
				var path = AssetDatabase.GUIDToAssetPath(guid);
				var p = AssetDatabase.LoadAssetAtPath<AvatarUploadProfile>(path);
				if (p != null) _profiles.Add(p);
			}
			_profiles.Sort((a, b) => string.Compare(a.profileName, b.profileName, StringComparison.OrdinalIgnoreCase));
			if (_profiles.Count == 0) _selectedIndex = -1;
			else if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count) _selectedIndex = 0;
		}

		private void LoadResources()
		{
			_logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.components/Resources/Icons/Logo@2x.png");
		}

		private void InitializeStyles()
		{
			if (_headerStyle == null)
			{
				_headerStyle = new GUIStyle(EditorStyles.largeLabel);
				_headerStyle.fontSize = 20;
				_headerStyle.normal.textColor = new Color(0.8f, 0.9f, 1f);
				_headerStyle.alignment = TextAnchor.MiddleLeft;
			}

			if (_sectionHeaderStyle == null)
			{
				_sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel);
				_sectionHeaderStyle.fontSize = 14;
				_sectionHeaderStyle.normal.textColor = new Color(0.2f, 0.75f, 0.73f);
			}

			if (_profileButtonStyle == null)
			{
				_profileButtonStyle = new GUIStyle(GUI.skin.button);
				_profileButtonStyle.alignment = TextAnchor.MiddleLeft;
				_profileButtonStyle.padding = new RectOffset(10, 10, 8, 8);
				_profileButtonStyle.normal.textColor = Color.white;
				_profileButtonStyle.fontSize = 12;
				_profileButtonStyle.wordWrap = false;
				_profileButtonStyle.clipping = TextClipping.Overflow;
				_profileButtonStyle.fixedHeight = 0;
				_profileButtonStyle.border = new RectOffset(4, 4, 4, 4);
			}

			if (_selectedProfileButtonStyle == null)
			{
				_selectedProfileButtonStyle = new GUIStyle(GUI.skin.button);
				_selectedProfileButtonStyle.alignment = TextAnchor.MiddleLeft;
				_selectedProfileButtonStyle.padding = new RectOffset(10, 10, 8, 8);
				_selectedProfileButtonStyle.normal.background = MakeTex(2, 2, new Color(0.2f, 0.75f, 0.73f, 0.5f));
				_selectedProfileButtonStyle.normal.textColor = new Color(0.2f, 0.75f, 0.73f);
				_selectedProfileButtonStyle.fontStyle = FontStyle.Bold;
				_selectedProfileButtonStyle.fontSize = 12;
				_selectedProfileButtonStyle.wordWrap = false;
				_selectedProfileButtonStyle.clipping = TextClipping.Overflow;
				_selectedProfileButtonStyle.fixedHeight = 0;
				_selectedProfileButtonStyle.border = new RectOffset(4, 4, 4, 4);
			}
		}

		private void OnGUI()
		{
			InitializeStyles();

			// Dark background
			EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), new Color(0.035f, 0.035f, 0.035f, 1f));

			EditorGUILayout.BeginVertical();
			// Header area
			GUILayout.BeginVertical(GUILayout.Height(100));
			DrawHeader();
			GUILayout.EndVertical();

			// Main area
			GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
			DrawLeftPanel();
			DrawRightPanel();
			GUILayout.EndHorizontal();

			// Footer controls + progress
			GUILayout.FlexibleSpace();
			DrawFooter();

			EditorGUILayout.EndVertical();
		}

		private void DrawHeader()
		{
			GUILayout.Space(10);

			GUILayout.BeginHorizontal(GUILayout.Height(60));
			GUILayout.Space(20);

			if (_logoTexture != null)
			{
				float logoHeight = 50;
				float logoWidth = logoHeight * (2020f / 865f);
				Rect logoRect = GUILayoutUtility.GetRect(logoWidth, logoHeight, GUILayout.ExpandWidth(false));
				GUI.DrawTexture(logoRect, _logoTexture, ScaleMode.ScaleToFit, true);
				GUILayout.Space(15);
			}

			GUILayout.BeginVertical();
			GUILayout.FlexibleSpace();
			GUILayout.Label("Avatar Uploader", _headerStyle);
			GUILayout.FlexibleSpace();
			GUILayout.EndVertical();

			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.Space(10);
			DrawHorizontalLine();
		}

		private void DrawLeftPanel()
		{
			GUILayout.Space(20);
			GUILayout.BeginVertical(GUILayout.Width(270), GUILayout.ExpandHeight(true));
			GUILayout.Label("Profiles", _sectionHeaderStyle);
			GUILayout.Space(5);
			_leftScroll = GUILayout.BeginScrollView(_leftScroll, GUI.skin.box, GUILayout.ExpandHeight(true));
			if (_profiles.Count == 0)
			{
				GUILayout.Label("No profiles found", EditorStyles.centeredGreyMiniLabel);
				GUILayout.Label("Create one using the button below", EditorStyles.centeredGreyMiniLabel);
			}
			else
			{
				for (int i = 0; i < _profiles.Count; i++)
				{
					bool isSelected = i == _selectedIndex;
					var buttonStyle = isSelected ? _selectedProfileButtonStyle : _profileButtonStyle;
					string label = GetProfileButtonLabel(_profiles[i]);
					if (GUILayout.Button(label, buttonStyle, GUILayout.Height(28)))
					{
						_selectedIndex = i;
					}
					GUILayout.Space(5);
				}
			}
			GUILayout.EndScrollView();
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("+ New", GUILayout.Height(24)))
			{
				CreateNewProfile();
			}
			GUI.enabled = _selectedIndex >= 0 && _selectedIndex < _profiles.Count;
			if (GUILayout.Button("Clone", GUILayout.Height(24)))
			{
				CloneSelectedProfile();
			}
			if (GUILayout.Button("Delete", GUILayout.Height(24)))
			{
				DeleteSelectedProfile();
			}
			GUI.enabled = true;
			EditorGUILayout.EndHorizontal();

			if (GUILayout.Button("Refresh", GUILayout.Height(24)))
			{
				ReloadProfiles();
			}
			GUILayout.EndVertical();
			GUILayout.Space(20);
		}

		private void DrawRightPanel()
		{
			EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
			_rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
			if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count)
			{
				EditorGUILayout.HelpBox("Select a profile to edit.", MessageType.None);
			}
			else
			{
				var profile = _profiles[_selectedIndex];
				EditorGUI.BeginChangeCheck();
				GUILayout.Label("Selected Profile", _sectionHeaderStyle);
				GUILayout.Space(4);
				profile.profileName = EditorGUILayout.TextField(new GUIContent("Profile Name", "Display name for this profile"), profile.profileName);
				GUILayout.Space(4);
				GUILayout.BeginVertical(EditorStyles.helpBox);
				GUILayout.Label("Build Settings", EditorStyles.boldLabel);
				profile.autoBuildPC = EditorGUILayout.Toggle(new GUIContent("Auto Build PC", "Include PC builds when building this profile"), profile.autoBuildPC);
				profile.autoBuildQuest = EditorGUILayout.Toggle(new GUIContent("Auto Build Quest", "Include Quest builds when building this profile"), profile.autoBuildQuest);
				profile.validationLevel = (ValidationLevel)EditorGUILayout.EnumPopup(new GUIContent("Validation Level", "How strict validation should be before build"), profile.validationLevel);
				GUILayout.EndVertical();
				if (EditorGUI.EndChangeCheck())
				{
					EditorUtility.SetDirty(profile);
				}

				GUILayout.Space(8);
				GUILayout.Label("Avatars", _sectionHeaderStyle);
				if (profile.avatars == null || profile.avatars.Count == 0)
				{
					EditorGUILayout.HelpBox("No avatars in this profile.", MessageType.Info);
				}
				else
				{
					for (int i = 0; i < profile.avatars.Count; i++)
					{
						DrawAvatarCard(profile, profile.avatars[i], i);
					}
				}
			}
			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}

		private void DrawAvatarCard(AvatarUploadProfile profile, AvatarBuildConfig cfg, int index)
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			EditorGUILayout.LabelField($"Avatar #{index + 1}", EditorStyles.boldLabel);

			EditorGUI.BeginChangeCheck();
			cfg.avatarPrefab = (GameObject)EditorGUILayout.ObjectField("Avatar Prefab", cfg.avatarPrefab, typeof(GameObject), false);
			cfg.buildPC = EditorGUILayout.Toggle("Build PC", cfg.buildPC);
			cfg.buildQuest = EditorGUILayout.Toggle("Build Quest", cfg.buildQuest);

			cfg.useSameBlueprintId = EditorGUILayout.Toggle("Use Same Blueprint ID", cfg.useSameBlueprintId);
			if (cfg.useSameBlueprintId)
			{
				cfg.blueprintIdPC = EditorGUILayout.TextField("Blueprint ID (Shared)", string.IsNullOrEmpty(cfg.blueprintIdPC) ? cfg.blueprintIdQuest : cfg.blueprintIdPC);
				cfg.blueprintIdQuest = cfg.blueprintIdPC;
			}
			else
			{
				cfg.blueprintIdPC = EditorGUILayout.TextField("Blueprint ID (PC)", cfg.blueprintIdPC);
				cfg.blueprintIdQuest = EditorGUILayout.TextField("Blueprint ID (Quest)", cfg.blueprintIdQuest);
			}

			GUILayout.Space(6);
			EditorGUILayout.LabelField("Metadata", EditorStyles.boldLabel);
			cfg.avatarName = EditorGUILayout.TextField("Name", cfg.avatarName);
			cfg.description = EditorGUILayout.TextArea(cfg.description, GUILayout.MinHeight(48));
			cfg.avatarIcon = (Texture2D)EditorGUILayout.ObjectField("Icon", cfg.avatarIcon, typeof(Texture2D), false);
			cfg.category = (AvatarCategory)EditorGUILayout.EnumPopup("Category", cfg.category);
			cfg.releaseStatus = (ReleaseStatus)EditorGUILayout.EnumPopup("Release", cfg.releaseStatus);
			cfg.version = EditorGUILayout.TextField("Version", cfg.version);

			// Tags as comma-separated
			string tagsStr = string.Join(", ", cfg.tags ?? new List<string>());
			tagsStr = EditorGUILayout.TextField("Tags (comma)", tagsStr);
			cfg.tags = SplitTags(tagsStr);

			if (EditorGUI.EndChangeCheck())
			{
				EditorUtility.SetDirty(profile);
			}

			EditorGUILayout.EndVertical();
		}

		private static List<string> SplitTags(string tags)
		{
			if (string.IsNullOrWhiteSpace(tags)) return new List<string>();
			return tags
				.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(t => t.Trim())
				.Where(t => !string.IsNullOrEmpty(t))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		private void DrawFooter()
		{
			DrawHorizontalLine();
			GUILayout.Space(10);
			GUILayout.BeginHorizontal();
			GUI.enabled = !_isBuilding && _selectedIndex >= 0 && _selectedIndex < _profiles.Count;
			GUI.backgroundColor = new Color(0.2f, 0.75f, 0.73f);
			if (GUILayout.Button("Build Selected Profile", GUILayout.Height(50)))
			{
				BuildSelectedProfile();
			}
			GUI.backgroundColor = Color.white;
			GUI.enabled = true;
			GUILayout.EndHorizontal();
			GUILayout.Space(5);
			if (_isBuilding)
			{
				Rect r = GUILayoutUtility.GetRect(18, 18, GUILayout.ExpandWidth(true));
				EditorGUI.ProgressBar(r, _progress, string.IsNullOrEmpty(_status) ? "Building..." : _status);
			}
		}

		private void BuildSelectedProfile()
		{
			if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count) return;
			var profile = _profiles[_selectedIndex];
			var configs = profile.avatars?.ToList() ?? new List<AvatarBuildConfig>();
			if (configs.Count == 0)
			{
				EditorUtility.DisplayDialog("No Avatars", "This profile has no avatars to build.", "OK");
				return;
			}

			_isBuilding = true;
			_progress = 0f;
			_status = "Preparing...";
			Repaint();

			try
			{
				var toBuildPC = configs.Where(c => c.buildPC && (profile.autoBuildPC || c.buildPC)).ToList();
				var toBuildQuest = configs.Where(c => c.buildQuest && (profile.autoBuildQuest || c.buildQuest)).ToList();

				int total = toBuildPC.Count + toBuildQuest.Count;
				int built = 0;

				if (toBuildPC.Count > 0)
				{
					_status = "Switching to PC..."; Repaint();
					PlatformSwitcher.EnsurePlatform(PlatformSwitcher.BuildPlatform.PC);
					foreach (var cfg in toBuildPC)
					{
						_status = $"Building PC: {cfg.avatarName}"; Repaint();
						var r = AvatarBuilder.BuildAvatar(profile, cfg, PlatformSwitcher.BuildPlatform.PC, s => { _status = s; Repaint(); });
						built++;
						_progress = Mathf.Clamp01(built / (float)total);
						Repaint();
					}
				}

				if (toBuildQuest.Count > 0)
				{
					_status = "Switching to Quest..."; Repaint();
					PlatformSwitcher.EnsurePlatform(PlatformSwitcher.BuildPlatform.Quest);
					foreach (var cfg in toBuildQuest)
					{
						_status = $"Building Quest: {cfg.avatarName}"; Repaint();
						var r = AvatarBuilder.BuildAvatar(profile, cfg, PlatformSwitcher.BuildPlatform.Quest, s => { _status = s; Repaint(); });
						built++;
						_progress = Mathf.Clamp01(built / (float)total);
						Repaint();
					}
				}

				profile.RecordBuild();
				EditorUtility.SetDirty(profile);
				AssetDatabase.SaveAssets();
				EditorUtility.DisplayDialog("Build Complete", "Finished building avatars for the selected profile.", "OK");
			}
			finally
			{
				_isBuilding = false;
				_status = string.Empty;
				_progress = 0f;
				Repaint();
			}
		}

		private void CreateNewProfile()
		{
			var dir = "Assets/YUCP/AvatarUploadProfiles";
			if (!AssetDatabase.IsValidFolder("Assets/YUCP"))
			{
				AssetDatabase.CreateFolder("Assets", "YUCP");
			}
			if (!AssetDatabase.IsValidFolder(dir))
			{
				AssetDatabase.CreateFolder("Assets/YUCP", "AvatarUploadProfiles");
			}
			var profile = ScriptableObject.CreateInstance<AvatarUploadProfile>();
			profile.profileName = "New Avatar Profile";
			var path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/New Avatar Upload Profile.asset");
			AssetDatabase.CreateAsset(profile, path);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			ReloadProfiles();
			_selectedIndex = _profiles.FindIndex(p => AssetDatabase.GetAssetPath(p) == path);
			Selection.activeObject = profile;
			EditorGUIUtility.PingObject(profile);
		}

		private void CloneSelectedProfile()
		{
			if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count) return;
			var source = _profiles[_selectedIndex];
			var clone = Instantiate(source);
			clone.name = source.name + " (Clone)";
			var dir = AssetDatabase.GetAssetPath(source);
			dir = System.IO.Path.GetDirectoryName(dir).Replace('\\','/');
			var path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{clone.name}.asset");
			AssetDatabase.CreateAsset(clone, path);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			ReloadProfiles();
			_selectedIndex = _profiles.FindIndex(p => AssetDatabase.GetAssetPath(p) == path);
			Selection.activeObject = clone;
			EditorGUIUtility.PingObject(clone);
		}

		private void DeleteSelectedProfile()
		{
			if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count) return;
			var profile = _profiles[_selectedIndex];
			var path = AssetDatabase.GetAssetPath(profile);
			if (EditorUtility.DisplayDialog("Delete Profile", $"Delete profile '{profile.profileName}'? This cannot be undone.", "Delete", "Cancel"))
			{
				AssetDatabase.DeleteAsset(path);
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				ReloadProfiles();
			}
		}

		private void DrawSeparator()
		{
			Rect rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
			EditorGUI.DrawRect(rect, new Color(0.3f, 0.3f, 0.3f, 1f));
		}

		private void DrawHorizontalLine()
		{
			Rect rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
			EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
		}

		private Texture2D MakeTex(int width, int height, Color color)
		{
			var pix = new Color[width * height];
			for (int i = 0; i < pix.Length; i++) pix[i] = color;
			var tex = new Texture2D(width, height);
			tex.SetPixels(pix);
			tex.Apply();
			return tex;
		}

		private string GetProfileButtonLabel(AvatarUploadProfile profile)
		{
			return string.IsNullOrEmpty(profile.profileName) ? profile.name : profile.profileName;
		}
	}
}


