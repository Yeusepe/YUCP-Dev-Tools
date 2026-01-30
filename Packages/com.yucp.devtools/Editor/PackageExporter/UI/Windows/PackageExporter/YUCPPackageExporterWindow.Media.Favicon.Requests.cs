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
        private void FetchFavicon(ExportProfile profile, ProductLink link, Image iconImage)
        {
            Debug.Log($"[YUCP PackageExporter] FetchFavicon called for URL: {link.url}");
            
            if (string.IsNullOrEmpty(link.url))
            {
                Debug.LogWarning("[YUCP PackageExporter] FetchFavicon: URL is empty, aborting");
                return;
            }
            
            // Show loading state
            iconImage.image = GetPlaceholderTexture();
            
            // Extract domain from URL
            string domain = ExtractDomain(link.url);
            Debug.Log($"[YUCP PackageExporter] Extracted domain: {domain}");
            
            // Reduce to base domain for Brandfetch and favicon (e.g. yeusepe.gumroad.com -> gumroad.com)
            string baseDomain = ExtractBaseDomain(domain);
            Debug.Log($"[YUCP PackageExporter] Base domain: {baseDomain}");
            
            if (string.IsNullOrEmpty(baseDomain))
            {
                Debug.LogWarning("[YUCP PackageExporter] FetchFavicon: Failed to extract domain, aborting");
                return;
            }
            
            // Start async fetch
            EditorApplication.update += CheckFaviconRequest;
            
            var requestData = new FaviconRequestData
            {
                profile = profile,
                link = link,
                iconImage = iconImage,
                domain = baseDomain,
                currentUrlIndex = 0,
                request = null,
                isBrandfetchSearch = false,
                brandId = null
            };
            
            faviconRequests.Add(requestData);
            Debug.Log($"[YUCP PackageExporter] Starting favicon request for domain: {domain}");
            StartFaviconRequest(requestData);
        }

        private void CheckFaviconRequest()
        {
            for (int i = faviconRequests.Count - 1; i >= 0; i--)
            {
                var requestData = faviconRequests[i];
                
                if (requestData.request == null)
                {
                    Debug.LogWarning($"[YUCP PackageExporter] CheckFaviconRequest: Request is null for domain {requestData.domain}");
                    continue;
                }
                
                if (requestData.request.isDone)
                {
                    Debug.Log($"[YUCP PackageExporter] Request completed for domain {requestData.domain}, result: {requestData.request.result}, URL index: {requestData.currentUrlIndex}");
                    bool success = false;
                    
                    if (requestData.request.result == UnityWebRequest.Result.Success)
                    {
                        // Handle Brandfetch search API response
                        if (requestData.isBrandfetchSearch)
                        {
                            string jsonResponse = requestData.request.downloadHandler.text;
                            Debug.Log($"[YUCP PackageExporter] Brandfetch search response: {jsonResponse}");
                            
                            // Parse JSON to extract brandId (simple string parsing)
                            string brandId = ExtractBrandIdFromJson(jsonResponse);
                            if (!string.IsNullOrEmpty(brandId))
                            {
                                Debug.Log($"[YUCP PackageExporter] Found Brandfetch brandId: {brandId}");
                                requestData.brandId = brandId;
                                requestData.isBrandfetchSearch = false;
                                // Now fetch the actual icon
                                StartFaviconRequest(requestData);
                                continue;
                            }
                            else
                            {
                                Debug.LogWarning("[YUCP PackageExporter] Failed to extract brandId from Brandfetch response");
                                // Fall through to try next URL
                            }
                        }
                        
                        byte[] imageData = requestData.request.downloadHandler.data;
                        if (imageData != null && imageData.Length > 0)
                        {
                            Debug.Log($"[YUCP PackageExporter] Downloaded {imageData.Length} bytes of image data");
                            
                            // Check if this is an ICO file (ICO files start with 00 00 01 00)
                            bool isIcoFile = imageData.Length >= 4 && 
                                           imageData[0] == 0x00 && imageData[1] == 0x00 && 
                                           imageData[2] == 0x01 && imageData[3] == 0x00;
                            
                            if (isIcoFile)
                            {
                                Debug.Log($"[YUCP PackageExporter] Detected ICO file format, extracting bitmap from ICO");
                                Texture2D texture = ExtractBitmapFromIco(imageData);
                                if (texture != null && texture.width > 0 && texture.height > 0)
                                {
                                    Debug.Log($"[YUCP PackageExporter] Successfully extracted bitmap from ICO: {texture.width}x{texture.height}");
                                    
                                    // Resize if needed
                                    if (texture.width != 32 || texture.height != 32)
                                    {
                                        texture = ResizeTexture(texture, 32, 32);
                                        Debug.Log("[YUCP PackageExporter] Resized favicon to 32x32");
                                    }
                                    
                                    // Cache the icon outside Assets so it doesn't create project residue
                                    string iconCachePath = PackageBuilder.SaveTextureToLibraryCache(texture, requestData.link.label ?? requestData.domain);
                                    if (!string.IsNullOrEmpty(iconCachePath))
                                    {
                                        requestData.link.cachedIconPath = iconCachePath;
                                        requestData.link.icon = texture;
                                        Debug.Log($"[YUCP PackageExporter] Cached favicon: {iconCachePath}");
                                    }
                                    else
                                    {
                                        // Fallback to in-memory texture if saving fails
                                        requestData.link.icon = texture;
                                        Debug.LogWarning($"[YUCP PackageExporter] Failed to cache favicon, using in-memory texture");
                                    }
                                    
                                    // Only set fetched icon if no custom icon is set
                                    if (requestData.link.customIcon == null)
                                    {
                                        requestData.iconImage.image = requestData.link.icon;
                                        Debug.Log($"[YUCP PackageExporter] Favicon successfully set for {requestData.domain}");
                                    }
                                    else
                                    {
                                        Debug.Log($"[YUCP PackageExporter] Favicon fetched but custom icon is displayed for {requestData.domain}");
                                    }
                                    if (requestData.profile != null)
                                    {
                                        EditorUtility.SetDirty(requestData.profile);
                                    }
                                    success = true;
                                }
                                else
                                {
                                    Debug.LogWarning($"[YUCP PackageExporter] Failed to extract bitmap from ICO file");
                                }
                            }
                            else
                            {
                                // Try to load as texture from raw bytes (PNG, JPG, etc.)
                                Texture2D texture = new Texture2D(2, 2);
                                bool loaded = texture.LoadImage(imageData);
                                
                                if (loaded && texture.width > 2 && texture.height > 2)
                                {
                                    Debug.Log($"[YUCP PackageExporter] Successfully loaded favicon: {texture.width}x{texture.height}");
                                    
                                    // Resize if needed (favicons can be various sizes)
                                    if (texture.width != 32 || texture.height != 32)
                                    {
                                        texture = ResizeTexture(texture, 32, 32);
                                        Debug.Log("[YUCP PackageExporter] Resized favicon to 32x32");
                                    }
                                    
                                    // Cache the icon outside Assets so it doesn't create project residue
                                    string iconCachePath = PackageBuilder.SaveTextureToLibraryCache(texture, requestData.link.label ?? requestData.domain);
                                    if (!string.IsNullOrEmpty(iconCachePath))
                                    {
                                        requestData.link.cachedIconPath = iconCachePath;
                                        requestData.link.icon = texture;
                                        Debug.Log($"[YUCP PackageExporter] Cached favicon: {iconCachePath}");
                                    }
                                    else
                                    {
                                        // Fallback to in-memory texture if saving fails
                                        requestData.link.icon = texture;
                                        Debug.LogWarning($"[YUCP PackageExporter] Failed to cache favicon, using in-memory texture");
                                    }
                                    
                                    // Only set fetched icon if no custom icon is set
                                    if (requestData.link.customIcon == null)
                                    {
                                        requestData.iconImage.image = requestData.link.icon;
                                        Debug.Log($"[YUCP PackageExporter] Favicon successfully set for {requestData.domain}");
                                    }
                                    else
                                    {
                                        Debug.Log($"[YUCP PackageExporter] Favicon fetched but custom icon is displayed for {requestData.domain}");
                                    }
                                    if (requestData.profile != null)
                                    {
                                        EditorUtility.SetDirty(requestData.profile);
                                    }
                                    success = true;
                                }
                                else
                                {
                                    Debug.LogWarning($"[YUCP PackageExporter] Failed to load image data as texture (width: {texture.width}, height: {texture.height}, loaded: {loaded})");
                                    UnityEngine.Object.DestroyImmediate(texture);
                                }
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"[YUCP PackageExporter] Downloaded data is null or empty");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[YUCP PackageExporter] Request failed: {requestData.request.error}");
                    }
                    
                    requestData.request.Dispose();
                    requestData.request = null;
                    
                    if (!success)
                    {
                        // Try next URL
                        requestData.currentUrlIndex++;
                        Debug.Log($"[YUCP PackageExporter] Trying next favicon URL (index {requestData.currentUrlIndex})");
                        if (requestData.currentUrlIndex < 3)
                        {
                            StartFaviconRequest(requestData);
                            continue;
                        }
                        else
                        {
                            Debug.LogWarning($"[YUCP PackageExporter] All favicon URLs failed for domain: {requestData.domain}");
                        }
                    }
                    
                    faviconRequests.RemoveAt(i);
                }
            }
            
            if (faviconRequests.Count == 0)
            {
                EditorApplication.update -= CheckFaviconRequest;
                Debug.Log("[YUCP PackageExporter] All favicon requests completed, removed update callback");
            }
        }

        private void StartFaviconRequest(FaviconRequestData requestData)
        {
            // Try Brandfetch Logo API first (using base domain), then direct favicon, then DuckDuckGo fallback
            string[] faviconUrls = new string[]
            {
                $"https://cdn.brandfetch.io/{requestData.domain}/w/128/h/128/theme/dark/icon.png?c={BrandfetchClientId}",
                $"https://{requestData.domain}/favicon.ico",
                $"https://icons.duckduckgo.com/ip3/{requestData.domain}.ico"
            };
            
            if (requestData.currentUrlIndex >= faviconUrls.Length)
            {
                Debug.LogWarning($"[YUCP PackageExporter] URL index {requestData.currentUrlIndex} is out of range");
                return;
            }
            
            string faviconUrl = faviconUrls[requestData.currentUrlIndex];
            Debug.Log($"[YUCP PackageExporter] Starting request for favicon URL: {faviconUrl}");
            
            // Use regular UnityWebRequest instead of UnityWebRequestTexture to handle various formats
            requestData.request = UnityWebRequest.Get(faviconUrl);
            requestData.request.timeout = 5;
            
            // Set User-Agent to avoid blocking by some services
            requestData.request.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            
            var operation = requestData.request.SendWebRequest();
            
            if (operation == null)
            {
                Debug.LogError("[YUCP PackageExporter] Failed to start web request");
            }
            else
            {
                Debug.Log($"[YUCP PackageExporter] Web request started successfully");
            }
        }

        private class FaviconRequestData
        {
            public ExportProfile profile;
            public ProductLink link;
            public Image iconImage;
            public string domain;
            public int currentUrlIndex;
            public UnityWebRequest request;
            public bool isBrandfetchSearch; // True if this request is for Brandfetch search API
            public string brandId; // Brandfetch brandId after search
        }

    }
}
