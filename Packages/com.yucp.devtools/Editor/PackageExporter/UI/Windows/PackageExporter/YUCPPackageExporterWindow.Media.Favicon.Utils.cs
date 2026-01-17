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
        private string ExtractDomain(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogWarning("[YUCP PackageExporter] ExtractDomain: URL is empty");
                return "";
            }
            
            try
            {
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
                    url = "https://" + url;
                    Debug.Log($"[YUCP PackageExporter] ExtractDomain: Added https:// prefix, new URL: {url}");
                }
                
                Uri uri = new Uri(url);
                string domain = uri.Host;
                Debug.Log($"[YUCP PackageExporter] ExtractDomain: Extracted domain '{domain}' from URL '{url}'");
                return domain;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageExporter] ExtractDomain failed for URL '{url}': {ex.Message}");
                return "";
            }
        }

        private string ExtractBrandIdFromJson(string json)
        {
            try
            {
                // Simple JSON parsing: look for "brandId":"value"
                int brandIdIndex = json.IndexOf("\"brandId\"");
                if (brandIdIndex < 0)
                    return null;
                
                int colonIndex = json.IndexOf(':', brandIdIndex);
                if (colonIndex < 0)
                    return null;
                
                int quoteStart = json.IndexOf('"', colonIndex);
                if (quoteStart < 0)
                    return null;
                
                int quoteEnd = json.IndexOf('"', quoteStart + 1);
                if (quoteEnd < 0)
                    return null;
                
                return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageExporter] Error parsing Brandfetch JSON: {ex.Message}");
                return null;
            }
        }

        private string ExtractBaseDomain(string host)
        {
            if (string.IsNullOrEmpty(host))
                return host;
            
            var parts = host.Split('.');
            if (parts.Length <= 2)
                return host;
            
            // Join last two labels (e.g., yeusepe.gumroad.com -> gumroad.com)
            string baseDomain = parts[parts.Length - 2] + "." + parts[parts.Length - 1];
            Debug.Log($"[YUCP PackageExporter] ExtractBaseDomain: host='{host}' -> baseDomain='{baseDomain}'");
            return baseDomain;
        }

        private Texture2D ResizeTexture(Texture2D source, int width, int height)
        {
            RenderTexture rt = RenderTexture.GetTemporary(width, height);
            Graphics.Blit(source, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D resized = new Texture2D(width, height);
            resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            resized.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return resized;
        }

        private Texture2D ExtractBitmapFromIco(byte[] icoData)
        {
            try
            {
                using (var ms = new MemoryStream(icoData))
                using (var reader = new BinaryReader(ms))
                {
                    // ICO header: 6 bytes
                    short reserved = reader.ReadInt16(); // Should be 0
                    short type = reader.ReadInt16();     // Should be 1 (icon)
                    short count = reader.ReadInt16();    // Number of images
                    
                    if (reserved != 0 || type != 1 || count <= 0)
                    {
                        Debug.LogWarning($"[YUCP PackageExporter] Invalid ICO header: reserved={reserved}, type={type}, count={count}");
                        return null;
                    }
                    
                    if (count > 20)
                    {
                        Debug.LogWarning($"[YUCP PackageExporter] ICO contains too many images: {count}");
                        return null;
                    }
                    
                    Debug.Log($"[YUCP PackageExporter] ICO file contains {count} images");
                    
                    // Read first directory entry (16 bytes)
                    byte width = reader.ReadByte();
                    byte height = reader.ReadByte();
                    reader.ReadByte(); // color count
                    reader.ReadByte(); // reserved
                    short planes = reader.ReadInt16();
                    short bitCount = reader.ReadInt16();
                    int imageSize = reader.ReadInt32();
                    int imageOffset = reader.ReadInt32();
                    
                    Debug.Log($"[YUCP PackageExporter] ICO entry: {Math.Max((int)width, 1)}x{Math.Max((int)height, 1)}, planes={planes}, bpp={bitCount}, size={imageSize}, offset={imageOffset}");
                    
                    if (imageOffset >= icoData.Length || imageOffset + imageSize > icoData.Length)
                    {
                        Debug.LogWarning($"[YUCP PackageExporter] Invalid ICO image offset or size");
                        return null;
                    }
                    
                    // Seek to image data
                    ms.Seek(imageOffset, SeekOrigin.Begin);
                    
                    // Check if it's PNG (PNG files start with 89 50 4E 47)
                    byte[] header = reader.ReadBytes(4);
                    ms.Seek(imageOffset, SeekOrigin.Begin); // Reset
                    
                    bool isPng = header.Length >= 4 && 
                                header[0] == 0x89 && header[1] == 0x50 && 
                                header[2] == 0x4E && header[3] == 0x47;
                    
                    if (isPng)
                    {
                        Debug.Log("[YUCP PackageExporter] ICO contains PNG data, loading directly");
                        byte[] pngData = reader.ReadBytes(imageSize);
                        Texture2D texture = new Texture2D(2, 2);
                        if (texture.LoadImage(pngData))
                        {
                            return texture;
                        }
                    }
                    else
                    {
                        // Read BITMAPINFOHEADER (DIB header)
                        int headerSize = reader.ReadInt32();     // Usually 40
                        int bmpWidth = reader.ReadInt32();
                        int bmpHeight = reader.ReadInt32();       // This is total height (image + mask)
                        reader.ReadInt16();                      // planes
                        int bpp = reader.ReadInt16();
                        int compression = reader.ReadInt32();
                        int imageSizeBytes = reader.ReadInt32();
                        reader.ReadInt32();                      // xPelsPerMeter
                        reader.ReadInt32();                      // yPelsPerMeter
                        int clrUsed = reader.ReadInt32();        // clrUsed
                        reader.ReadInt32();                      // clrImportant
                        
                        // Support palette-based bitmaps (1, 4, 8 bpp) and direct color (24, 32 bpp)
                        if (bpp != 1 && bpp != 4 && bpp != 8 && bpp != 24 && bpp != 32)
                        {
                            Debug.LogWarning($"[YUCP PackageExporter] Unsupported ICO bitmap bpp: {bpp}");
                            return null;
                        }
                        
                        bool isPaletteBased = (bpp == 1 || bpp == 4 || bpp == 8);
                        
                        int realHeight = bmpHeight / 2; // ICO stores height as image+mask
                        if (realHeight <= 0 || bmpWidth <= 0)
                        {
                            Debug.LogWarning($"[YUCP PackageExporter] Invalid ICO bitmap dimensions: {bmpWidth}x{realHeight}");
                            return null;
                        }
                        
                        if (bmpWidth > 512 || realHeight > 512)
                        {
                            Debug.LogWarning($"[YUCP PackageExporter] ICO bitmap too large: {bmpWidth}x{realHeight}");
                            return null;
                        }
                        
                        // Read color palette if palette-based
                        Color32[] palette = null;
                        int paletteSize = 0;
                        if (isPaletteBased)
                        {
                            paletteSize = (bpp == 1) ? 2 : ((bpp == 4) ? 16 : 256);
                            palette = new Color32[paletteSize];
                            
                            // Palette entries are BGR (3 bytes) or BGRA (4 bytes if clrUsed > 0)
                            int paletteBytes = (clrUsed > 0 && clrUsed <= paletteSize) ? 4 : 3;
                            for (int i = 0; i < paletteSize; i++)
                            {
                                byte b = reader.ReadByte();
                                byte g = reader.ReadByte();
                                byte r = reader.ReadByte();
                                byte a = (paletteBytes == 4) ? reader.ReadByte() : (byte)255;
                                palette[i] = new Color32(r, g, b, a);
                            }
                            Debug.Log($"[YUCP PackageExporter] Read {paletteSize}-color palette for {bpp}-bit bitmap");
                        }
                        
                        // Read the pixel data (DIB, bottom-up, BGR(A) or palette indices)
                        int bytesPerPixel = isPaletteBased ? 1 : (bpp / 8);
                        int bitsPerRow = bmpWidth * bpp;
                        int rowSize = ((bitsPerRow + 31) / 32) * 4; // Row size padded to 4-byte boundary
                        int dibSize = rowSize * realHeight;
                        
                        if (dibSize > imageSize - headerSize - (isPaletteBased ? (paletteSize * (clrUsed > 0 && clrUsed <= paletteSize ? 4 : 3)) : 0))
                        {
                            int available = imageSize - headerSize - (isPaletteBased ? (paletteSize * (clrUsed > 0 && clrUsed <= paletteSize ? 4 : 3)) : 0);
                            Debug.LogWarning($"[YUCP PackageExporter] DIB size calculation: calculated={dibSize}, available={available}");
                            dibSize = available;
                        }
                        
                        byte[] dib = reader.ReadBytes(dibSize);
                        if (dib.Length < dibSize)
                        {
                            Debug.LogWarning("[YUCP PackageExporter] DIB data truncated");
                            return null;
                        }
                        
                        // Create texture and convert BGR to RGB or palette indices to colors
                        Texture2D texture = new Texture2D(bmpWidth, realHeight, TextureFormat.RGBA32, false);
                        Color32[] pixels = new Color32[bmpWidth * realHeight];
                        
                        for (int y = 0; y < realHeight; y++)
                        {
                            int srcRow = realHeight - 1 - y; // Bottom-up
                            int rowOffset = srcRow * rowSize;
                            
                            for (int x = 0; x < bmpWidth; x++)
                            {
                                Color32 color;
                                
                                if (isPaletteBased)
                                {
                                    // Read palette index based on bit depth
                                    int bitOffset = x * bpp;
                                    int byteIndex = rowOffset + (bitOffset / 8);
                                    int bitIndex = bitOffset % 8;
                                    
                                    if (byteIndex >= dib.Length)
                                        break;
                                    
                                    int paletteIndex = 0;
                                    if (bpp == 1)
                                    {
                                        // 1-bit: each bit is a palette index
                                        paletteIndex = (dib[byteIndex] >> (7 - bitIndex)) & 0x01;
                                    }
                                    else if (bpp == 4)
                                    {
                                        // 4-bit: two palette indices per byte
                                        // For 4-bit, bitIndex will be 0, 4, 8, 12, etc.
                                        // If bitIndex is 0 or 4 mod 8, it's the first pixel in that byte
                                        if ((x % 2) == 0)
                                        {
                                            // First pixel in byte (high 4 bits)
                                            paletteIndex = (dib[byteIndex] >> 4) & 0x0F;
                                        }
                                        else
                                        {
                                            // Second pixel in byte (low 4 bits)
                                            paletteIndex = dib[byteIndex] & 0x0F;
                                        }
                                    }
                                    else // bpp == 8
                                    {
                                        // 8-bit: one byte per pixel
                                        paletteIndex = dib[byteIndex];
                                    }
                                    
                                    if (paletteIndex >= paletteSize || paletteIndex < 0)
                                        paletteIndex = 0;
                                    
                                    color = palette[paletteIndex];
                                }
                                else
                                {
                                    // Direct color (24 or 32 bpp)
                                    int srcIndex = rowOffset + x * bytesPerPixel;
                                    if (srcIndex + bytesPerPixel > dib.Length)
                                        break;
                                    
                                    byte b = dib[srcIndex + 0];
                                    byte g = dib[srcIndex + 1];
                                    byte r = dib[srcIndex + 2];
                                    byte a = (bytesPerPixel == 4) ? dib[srcIndex + 3] : (byte)255;
                                    
                                    color = new Color32(r, g, b, a);
                                }
                                
                                pixels[y * bmpWidth + x] = color;
                            }
                        }
                        
                        texture.SetPixels32(pixels);
                        texture.Apply();
                        Debug.Log($"[YUCP PackageExporter] ICO bitmap extracted as texture: {bmpWidth}x{realHeight}, bpp={bpp}");
                        return texture;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageExporter] ExtractBitmapFromIco failed: {ex.Message}\n{ex.StackTrace}");
            }
            
            return null;
        }

    }
}
