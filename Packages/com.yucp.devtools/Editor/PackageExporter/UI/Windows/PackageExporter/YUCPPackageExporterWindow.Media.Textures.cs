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
        private void CreateBannerGradientTexture()
        {
            if (bannerGradientTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(bannerGradientTexture);
            }
            
            int width = 1;
            int height = Mathf.RoundToInt(BannerHeight);
            bannerGradientTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            
            // Configurable gradient parameters
            float segment1End = 0.3f;  // Top segment ends at 30% (was 20%)
            float segment2End = 0.7f;  // Middle segment ends at 70% (was 60%)
            float alpha1 = 0.2f;        // First segment alpha (was 0.3f)
            float alpha2 = 0.6f;        // Second segment alpha (was 0.8f)
            float alpha3 = 1.0f;
            
            for (int y = 0; y < height; y++)
            {
                float t = (float)y / (height - 1);
                Color color;
                
                if (t < segment1End)
                {
                    float localT = t / segment1End;
                    color = new Color(0.082f, 0.082f, 0.082f, Mathf.Lerp(0f, alpha1, localT));
                }
                else if (t < segment2End)
                {
                    float localT = (t - segment1End) / (segment2End - segment1End);
                    color = new Color(0.082f, 0.082f, 0.082f, Mathf.Lerp(alpha1, alpha2, localT));
                }
                else
                {
                    float localT = (t - segment2End) / (1.0f - segment2End);
                    color = new Color(0.082f, 0.082f, 0.082f, Mathf.Lerp(alpha2, alpha3, localT));
                }
                
                for (int x = 0; x < width; x++)
                {
                    bannerGradientTexture.SetPixel(x, height - 1 - y, color);
                }
            }
            
            bannerGradientTexture.Apply();
        }

        private void CreateDottedBorderTexture()
        {
            int size = 16;
            dottedBorderTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            
            Color transparent = new Color(0, 0, 0, 0);
            Color teal = new Color(54f / 255f, 191f / 255f, 177f / 255f, 0.6f);
            
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isBorder = (x == 0 || x == size - 1 || y == 0 || y == size - 1);
                    bool isDot = isBorder && ((x % 4 < 2 && y == 0) || (x % 4 < 2 && y == size - 1) || 
                                              (y % 4 < 2 && x == 0) || (y % 4 < 2 && x == size - 1));
                    dottedBorderTexture.SetPixel(x, y, isDot ? teal : transparent);
                }
            }
            
            dottedBorderTexture.Apply();
            dottedBorderTexture.wrapMode = TextureWrapMode.Repeat;
        }

    }
}
