using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using VRC.Core;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	/// <summary>
	/// Per-avatar asset file that stores all configuration and metadata for a single avatar.
	/// Similar to how PackageExporter uses individual asset files per package.
	/// </summary>
	[CreateAssetMenu(fileName = "New Avatar Asset", menuName = "YUCP/Avatar Asset", order = 111)]
	public class AvatarAsset : ScriptableObject
	{
	[Header("Avatar Reference")]
	[Tooltip("The avatar prefab GameObject")]
	[SerializeField]
	public GameObject avatarPrefab;

	// Store scene instance info if avatarPrefab is from a scene or has overrides
	[SerializeField, HideInInspector]
	internal string sourceScenePath; // Path to the scene file

	[SerializeField, HideInInspector]
	internal string sourceInstancePath; // Hierarchy path in scene (e.g., "Root/Child[0]/Avatar")

		[Header("Collection Assignment")]
		[Tooltip("The collection this avatar belongs to")]
		public AvatarCollection assignedCollection;

		[Header("Blueprint IDs")]
		[Tooltip("Blueprint ID for PC platform")]
		public string blueprintIdPC;
		
		[Tooltip("Blueprint ID for Quest platform")]
		public string blueprintIdQuest;
		
		[Tooltip("Use the same blueprint ID for both platforms")]
		public bool useSameBlueprintId = false;

		[Header("Platform Selection")]
		[Tooltip("Build this avatar for PC")]
		public bool buildPC = true;
		
		[Tooltip("Build this avatar for Quest")]
		public bool buildQuest = false; // Default to PC only

		[Header("Avatar Metadata")]
		[Tooltip("Avatar name (displayed in VRChat)")]
		public string avatarName;
		
		[TextArea(3, 6)]
		[Tooltip("Avatar description")]
		public string description;
		
		[Tooltip("Avatar icon/thumbnail")]
		public Texture2D avatarIcon;

		[Header("Categories & Tags")]
		[Tooltip("Avatar tags for filtering")]
		public List<string> tags = new List<string>();
		
		[Tooltip("Avatar category")]
		public AvatarCategory category = AvatarCategory.Generic;

		[Header("Release Settings")]
		[Tooltip("Release status")]
		public ReleaseStatus releaseStatus = ReleaseStatus.Private;
		
		[Tooltip("Avatar version")]
		public string version = "1.0";

		[Header("Avatar Styles")]
		[Tooltip("Avatar styles (e.g., Sci-Fi, Anime)")]
		public List<string> styles = new List<string>();

		[Header("Performance Info (Read-Only)")]
		[Tooltip("Polygon count for PC")]
		public int polyCountPC;
		
		[Tooltip("Polygon count for Quest")]
		public int polyCountQuest;
		
		[Tooltip("Material count")]
		public int materialCount;
		
		[Tooltip("Performance rating for PC")]
		public PerformanceRating performanceRatingPC;
		
		[Tooltip("Performance rating for Quest")]
		public PerformanceRating performanceRatingQuest;

		[Header("Detailed Performance Metrics")]
		public int skinnedMeshCount;
		public int meshCount;
		public int boneCount;
		public int lightCount;
		public int particleSystemCount;
		public int audioSourceCount;
		public int dynamicBoneCount;
		public int dynamicBoneColliderCount;
		public int clothCount;
		public int constraintCount;
		public int animatorCount;
		public int animationClipCount;
		public int textureCount;
		public int materialInstanceCount;
		public float totalTextureMemoryMB;
		public float totalMeshMemoryMB;
		public float totalAnimationMemoryMB;
		public float totalAudioMemoryMB;
		public float totalMemoryMB;

		[Header("Gallery")]
		[Tooltip("Gallery images for this avatar")]
		public List<AvatarGalleryImage> galleryImages = new List<AvatarGalleryImage>();
		
		[Tooltip("Currently active gallery image index")]
		public int activeGalleryIndex;

		[Header("Build Status")]
		[Tooltip("Last build status for PC")]
		public BuildStatus lastBuildStatusPC;
		
		[Tooltip("Last build status for Quest")]
		public BuildStatus lastBuildStatusQuest;
		
		[Tooltip("Last build time")]
		public string lastBuildTime;

		/// <summary>
		/// Sync blueprint ID from the PipelineManager component on the avatar prefab.
		/// This reads the blueprint ID from the prefab asset's PipelineManager component.
		/// </summary>
		public void SyncBlueprintIdFromComponent()
		{
			if (avatarPrefab == null)
			{
				Debug.LogWarning("[AvatarAsset] SyncBlueprintIdFromComponent: avatarPrefab is null");
				return;
			}

			// First, check if avatarPrefab is directly a scene instance (even if not stored)
			// This handles cases where OnValidate() didn't run or didn't detect it
			if (avatarPrefab.scene.IsValid() && !PrefabUtility.IsPartOfPrefabAsset(avatarPrefab))
			{
				Debug.Log($"[AvatarAsset] avatarPrefab '{avatarPrefab.name}' is a scene object, checking it directly for PipelineManager...");
				var scenePMs = avatarPrefab.GetComponentsInChildren<PipelineManager>(true);
				Debug.Log($"[AvatarAsset] Found {scenePMs.Length} PipelineManager component(s) in scene object '{avatarPrefab.name}'");
				
				foreach (var pm in scenePMs)
				{
					Debug.Log($"[AvatarAsset] Found PipelineManager on scene object '{pm.gameObject.name}'. BlueprintId: '{(string.IsNullOrEmpty(pm.blueprintId) ? "(empty)" : pm.blueprintId)}'");
					if (!string.IsNullOrEmpty(pm.blueprintId))
					{
						Undo.RecordObject(this, "Sync Blueprint ID from Component");
						blueprintIdPC = pm.blueprintId;
						blueprintIdQuest = pm.blueprintId;
						EditorUtility.SetDirty(this);
						Debug.Log($"[AvatarAsset] Synced blueprint ID '{pm.blueprintId}' from PipelineManager on scene object '{pm.gameObject.name}'");
						return;
					}
				}
				
				if (scenePMs.Length > 0)
				{
					Debug.LogWarning($"[AvatarAsset] Found {scenePMs.Length} PipelineManager component(s) on scene object '{avatarPrefab.name}', but none have a blueprintId set. Please set a blueprintId on the PipelineManager component.");
				}
			}
			else if (PrefabUtility.IsPartOfPrefabInstance(avatarPrefab))
			{
				// It's a prefab instance - check if it has overrides and check it directly
				Debug.Log($"[AvatarAsset] avatarPrefab '{avatarPrefab.name}' is a prefab instance, checking it directly for PipelineManager...");
				var instancePMs = avatarPrefab.GetComponentsInChildren<PipelineManager>(true);
				Debug.Log($"[AvatarAsset] Found {instancePMs.Length} PipelineManager component(s) in prefab instance '{avatarPrefab.name}'");
				
				foreach (var pm in instancePMs)
				{
					// Check if this PipelineManager has overrides (scene-specific data)
					var prefabPM = PrefabUtility.GetCorrespondingObjectFromSource(pm);
					bool hasOverrides = PrefabUtility.HasPrefabInstanceAnyOverrides(pm.gameObject, false);
					
					// If it has overrides or the prefab doesn't have this component, check it
					if (hasOverrides || prefabPM == null)
					{
						Debug.Log($"[AvatarAsset] Found PipelineManager on prefab instance '{pm.gameObject.name}' (has overrides: {hasOverrides}). BlueprintId: '{(string.IsNullOrEmpty(pm.blueprintId) ? "(empty)" : pm.blueprintId)}'");
						if (!string.IsNullOrEmpty(pm.blueprintId))
						{
							Undo.RecordObject(this, "Sync Blueprint ID from Component");
							blueprintIdPC = pm.blueprintId;
							blueprintIdQuest = pm.blueprintId;
							EditorUtility.SetDirty(this);
							Debug.Log($"[AvatarAsset] Synced blueprint ID '{pm.blueprintId}' from PipelineManager on prefab instance '{pm.gameObject.name}'");
							return;
						}
					}
					else
					{
						Debug.Log($"[AvatarAsset] PipelineManager on '{pm.gameObject.name}' has no overrides, will check prefab asset instead");
					}
				}
			}

			// Use the same approach as Control Panel - find descriptor root first
			// This handles FBX models where PipelineManager is on a prefab instance, not the FBX itself
			// Pass 'this' so it can use stored scene instance information if available
			var descriptorRoot = AvatarToolsWindow.EnsureDescriptorRootStatic(avatarPrefab, this);
			if (descriptorRoot != null)
			{
				Debug.Log($"[AvatarAsset] Checking descriptor root '{descriptorRoot.name}' for PipelineManager...");
				
				// Try to get PipelineManager from descriptor root
				if (descriptorRoot.TryGetComponent<PipelineManager>(out var pm))
				{
					Debug.Log($"[AvatarAsset] Found PipelineManager on '{descriptorRoot.name}'. BlueprintId: '{(string.IsNullOrEmpty(pm.blueprintId) ? "(empty)" : pm.blueprintId)}'");
					if (!string.IsNullOrEmpty(pm.blueprintId))
					{
						Undo.RecordObject(this, "Sync Blueprint ID from Component");
						blueprintIdPC = pm.blueprintId;
						blueprintIdQuest = pm.blueprintId;
						EditorUtility.SetDirty(this);
						Debug.Log($"[AvatarAsset] Synced blueprint ID '{pm.blueprintId}' from PipelineManager on '{descriptorRoot.name}'");
						return;
					}
				}
				else
				{
					Debug.Log($"[AvatarAsset] No PipelineManager found directly on '{descriptorRoot.name}', checking children...");
				}
				
				// Try children of descriptor root
				var allPMs = descriptorRoot.GetComponentsInChildren<PipelineManager>(true);
				Debug.Log($"[AvatarAsset] Found {allPMs.Length} PipelineManager component(s) in children of '{descriptorRoot.name}'");
				
				foreach (var childPM in allPMs)
				{
					Debug.Log($"[AvatarAsset] Found PipelineManager on '{childPM.gameObject.name}'. BlueprintId: '{(string.IsNullOrEmpty(childPM.blueprintId) ? "(empty)" : childPM.blueprintId)}'");
					if (!string.IsNullOrEmpty(childPM.blueprintId))
					{
						Undo.RecordObject(this, "Sync Blueprint ID from Component");
						blueprintIdPC = childPM.blueprintId;
						blueprintIdQuest = childPM.blueprintId;
						EditorUtility.SetDirty(this);
						Debug.Log($"[AvatarAsset] Synced blueprint ID '{childPM.blueprintId}' from PipelineManager on '{childPM.gameObject.name}' (child of '{descriptorRoot.name}')");
						return;
					}
				}
				
				if (allPMs.Length > 0)
				{
					Debug.LogWarning($"[AvatarAsset] Found {allPMs.Length} PipelineManager component(s) on '{descriptorRoot.name}' and its children, but none have a blueprintId set. Please set a blueprintId on the PipelineManager component.");
				}
				else
				{
					Debug.LogWarning($"[AvatarAsset] Found descriptor root '{descriptorRoot.name}' but no PipelineManager component found on it or its children");
				}
			}
			else
			{
				Debug.LogWarning($"[AvatarAsset] Could not find descriptor root for prefab: {avatarPrefab.name}");
			}
			
			// Also check source scene instance if we have one stored (prioritize this over temporary instances)
			var sourceInstance = GetSourceSceneInstance();
			if (sourceInstance != null)
			{
				Debug.Log($"[AvatarAsset] Checking source scene instance '{sourceInstance.name}' for PipelineManager...");
				var allPMs = sourceInstance.GetComponentsInChildren<PipelineManager>(true);
				Debug.Log($"[AvatarAsset] Found {allPMs.Length} PipelineManager component(s) in source scene instance '{sourceInstance.name}'");
				
				foreach (var pm in allPMs)
				{
					Debug.Log($"[AvatarAsset] Found PipelineManager on source scene instance '{pm.gameObject.name}'. BlueprintId: '{(string.IsNullOrEmpty(pm.blueprintId) ? "(empty)" : pm.blueprintId)}'");
					if (!string.IsNullOrEmpty(pm.blueprintId))
					{
						Undo.RecordObject(this, "Sync Blueprint ID from Component");
						blueprintIdPC = pm.blueprintId;
						blueprintIdQuest = pm.blueprintId;
						EditorUtility.SetDirty(this);
						Debug.Log($"[AvatarAsset] Synced blueprint ID '{pm.blueprintId}' from PipelineManager on source scene instance '{pm.gameObject.name}'");
						return;
					}
				}
				
				if (allPMs.Length > 0)
				{
					Debug.LogWarning($"[AvatarAsset] Found {allPMs.Length} PipelineManager component(s) on source scene instance '{sourceInstance.name}', but none have a blueprintId set. Please set a blueprintId on the PipelineManager component.");
				}
				else
				{
					Debug.LogWarning($"[AvatarAsset] Source scene instance '{sourceInstance.name}' found but no PipelineManager component found on it or its children");
				}
			}
			else
			{
				Debug.Log($"[AvatarAsset] No stored source scene instance found (sourceScenePath: '{(string.IsNullOrEmpty(sourceScenePath) ? "(empty)" : sourceScenePath)}', sourceInstancePath: '{(string.IsNullOrEmpty(sourceInstancePath) ? "(empty)" : sourceInstancePath)}')");
			}

			// Fallback: Try direct prefab asset path (for non-FBX prefabs)
			var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(avatarPrefab);
			if (string.IsNullOrEmpty(assetPath))
			{
				assetPath = AssetDatabase.GetAssetPath(avatarPrefab);
			}

			if (!string.IsNullOrEmpty(assetPath))
			{
				var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
				if (prefabAsset != null)
				{
					if (prefabAsset.TryGetComponent<PipelineManager>(out var pm))
					{
						if (!string.IsNullOrEmpty(pm.blueprintId))
						{
							Undo.RecordObject(this, "Sync Blueprint ID from Component");
							blueprintIdPC = pm.blueprintId;
							blueprintIdQuest = pm.blueprintId;
							EditorUtility.SetDirty(this);
							return;
						}
					}
				}
			}

			Debug.LogWarning($"[AvatarAsset] No PipelineManager found with a blueprintId");
		}


		/// <summary>
		/// Populate avatar data from PipelineManager component.
		/// Reads blueprint ID and syncs it to the asset.
		/// </summary>
		public void PopulateFromPipelineManager()
		{
			SyncBlueprintIdFromComponent();
		}

		/// <summary>
		/// Get the blueprint ID for the specified platform.
		/// </summary>
		public string GetBlueprintId(PlatformSwitcher.BuildPlatform platform)
		{
			if (useSameBlueprintId && !string.IsNullOrEmpty(blueprintIdPC))
				return blueprintIdPC;
			
			return platform == PlatformSwitcher.BuildPlatform.PC ? blueprintIdPC : blueprintIdQuest;
		}

		/// <summary>
		/// Set the blueprint ID for the specified platform.
		/// </summary>
		public void SetBlueprintId(PlatformSwitcher.BuildPlatform platform, string blueprintId)
		{
			Undo.RecordObject(this, "Set Blueprint ID");
			
			if (platform == PlatformSwitcher.BuildPlatform.PC)
			{
				blueprintIdPC = blueprintId;
				if (useSameBlueprintId)
				{
					blueprintIdQuest = blueprintId;
				}
			}
			else
			{
				blueprintIdQuest = blueprintId;
				if (useSameBlueprintId)
				{
					blueprintIdPC = blueprintId;
				}
			}
			
			EditorUtility.SetDirty(this);
		}

		/// <summary>
		/// Called when the asset is validated (e.g., when avatarPrefab is assigned).
		/// Detects if avatarPrefab is a scene instance or prefab instance with overrides,
		/// and stores scene info so we can use the instance's descriptor data.
		/// </summary>
		private void OnValidate()
		{
			if (avatarPrefab != null)
			{
				// Check if it's a prefab instance (not the asset itself)
				if (PrefabUtility.IsPartOfPrefabInstance(avatarPrefab))
				{
					// It's a prefab instance - check if it has overrides
					if (PrefabUtility.HasPrefabInstanceAnyOverrides(avatarPrefab, false))
					{
						// Has overrides - store scene info so we can use the instance's descriptor
						if (avatarPrefab.scene.IsValid())
						{
							sourceScenePath = avatarPrefab.scene.path;
							sourceInstancePath = GetHierarchyPath(avatarPrefab);
							EditorUtility.SetDirty(this);
							Debug.Log($"[AvatarAsset] Stored scene instance for '{avatarPrefab.name}' from scene '{sourceScenePath}' (has overrides)");
						}
					}
					else
					{
						// No overrides - just use the prefab asset
						sourceScenePath = null;
						sourceInstancePath = null;
						EditorUtility.SetDirty(this);
					}
				}
				else if (PrefabUtility.IsPartOfPrefabAsset(avatarPrefab))
				{
					// It's the prefab asset itself - clear scene info
					sourceScenePath = null;
					sourceInstancePath = null;
					EditorUtility.SetDirty(this);
				}
				else if (avatarPrefab.scene.IsValid())
				{
					// It's a scene object (not a prefab at all)
					sourceScenePath = avatarPrefab.scene.path;
					sourceInstancePath = GetHierarchyPath(avatarPrefab);
					EditorUtility.SetDirty(this);
					Debug.Log($"[AvatarAsset] Stored scene object for '{avatarPrefab.name}' from scene '{sourceScenePath}'");
				}
			}
			else
			{
				// Clear scene info if avatarPrefab is null
				sourceScenePath = null;
				sourceInstancePath = null;
			}
		}

		/// <summary>
		/// Get the hierarchy path of a GameObject in its scene.
		/// </summary>
		private string GetHierarchyPath(GameObject obj)
		{
			var path = obj.name;
			var parent = obj.transform.parent;
			while (parent != null)
			{
				path = parent.name + "/" + path;
				parent = parent.parent;
			}
			return path;
		}

		/// <summary>
		/// Get the source scene instance if one was stored.
		/// Returns null if no scene instance was stored or if the scene/instance no longer exists.
		/// </summary>
		public GameObject GetSourceSceneInstance()
		{
			if (string.IsNullOrEmpty(sourceScenePath) || string.IsNullOrEmpty(sourceInstancePath))
				return null;

			try
			{
				var scene = EditorSceneManager.GetSceneByPath(sourceScenePath);
				if (!scene.IsValid())
				{
					// Scene not loaded - try to load it
					scene = EditorSceneManager.OpenScene(sourceScenePath, OpenSceneMode.Additive);
					if (!scene.IsValid())
					{
						Debug.LogWarning($"[AvatarAsset] Could not load scene '{sourceScenePath}'");
						return null;
					}
				}

				// Find the instance by hierarchy path
				var instance = FindGameObjectByPath(scene, sourceInstancePath);
				if (instance != null)
				{
					return instance;
				}
				else
				{
					Debug.LogWarning($"[AvatarAsset] Could not find instance at path '{sourceInstancePath}' in scene '{sourceScenePath}'");
					return null;
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[AvatarAsset] Error getting source scene instance: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Find a GameObject by hierarchy path in a scene.
		/// </summary>
		private GameObject FindGameObjectByPath(Scene scene, string path)
		{
			var parts = path.Split('/');
			if (parts.Length == 0)
				return null;

			// Find root object
			var rootObjects = scene.GetRootGameObjects();
			GameObject current = null;

			foreach (var root in rootObjects)
			{
				if (root.name == parts[0])
				{
					current = root;
					break;
				}
			}

			if (current == null)
				return null;

			// Navigate down the hierarchy
			for (int i = 1; i < parts.Length; i++)
			{
				var part = parts[i];
				// Handle sibling index notation like "Name[0]"
				var name = part;
				int siblingIndex = -1;
				var indexStart = part.LastIndexOf('[');
				var indexEnd = part.LastIndexOf(']');
				if (indexStart >= 0 && indexEnd > indexStart)
				{
					name = part.Substring(0, indexStart);
					if (int.TryParse(part.Substring(indexStart + 1, indexEnd - indexStart - 1), out siblingIndex))
					{
						// Find child by name and sibling index
						var found = false;
						for (int j = 0; j < current.transform.childCount; j++)
						{
							var child = current.transform.GetChild(j);
							if (child.name == name && j == siblingIndex)
							{
								current = child.gameObject;
								found = true;
								break;
							}
						}
						if (!found)
							return null;
					}
					else
					{
						// Just find by name
						var child = current.transform.Find(name);
						if (child == null)
							return null;
						current = child.gameObject;
					}
				}
				else
				{
					// Just find by name
					var child = current.transform.Find(name);
					if (child == null)
						return null;
					current = child.gameObject;
				}
			}

			return current;
		}
	}

	public enum AvatarCategory
	{
		Generic,
		Anime,
		Furry,
		Robot,
		Animal,
		Human,
		Other
	}

	public enum ReleaseStatus
	{
		Private,
		Public
	}

	public enum PerformanceRating
	{
		Excellent,
		Good,
		Medium,
		Poor,
		VeryPoor
	}

	public enum BuildStatus
	{
		NotBuilt,
		Building,
		Success,
		Failed
	}
}

