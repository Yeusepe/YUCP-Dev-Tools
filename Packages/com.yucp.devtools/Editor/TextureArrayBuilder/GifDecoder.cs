#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using UnityEngine;

namespace YUCP.DevTools.Editor.TextureArrays
{
    internal static class GifDecoder
    {
        public static List<Texture2D> Decode(string filePath, int maxFrames)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("GIF file not found", filePath);

            using (var image = Image.FromFile(filePath))
            {
                var dimension = new FrameDimension(image.FrameDimensionsList[0]);
                int frameCount = Math.Min(image.GetFrameCount(dimension), maxFrames);
                var frames = new List<Texture2D>(frameCount);

                for (int i = 0; i < frameCount; i++)
                {
                    image.SelectActiveFrame(dimension, i);
                    using (var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb))
                    {
                        using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bitmap))
                        {
                            g.DrawImage(image, Point.Empty);
                        }

                        frames.Add(ConvertBitmapToTexture(bitmap));
                    }
                }

                return frames;
            }
        }

        private static Texture2D ConvertBitmapToTexture(Bitmap bitmap)
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int size = Math.Abs(data.Stride) * data.Height;
                byte[] pixels = new byte[size];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, size);

                Texture2D texture = new Texture2D(bitmap.Width, bitmap.Height, TextureFormat.RGBA32, false);
                var colors = new Color32[bitmap.Width * bitmap.Height];

                int rowLength = bitmap.Width * 4;
                for (int y = 0; y < bitmap.Height; y++)
                {
                    int rowSrc = y * data.Stride;
                    int rowDst = y * bitmap.Width;
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        int srcIndex = rowSrc + x * 4;
                        byte b = pixels[srcIndex];
                        byte g = pixels[srcIndex + 1];
                        byte r = pixels[srcIndex + 2];
                        byte a = pixels[srcIndex + 3];
                        colors[rowDst + x] = new Color32(r, g, b, a);
                    }
                }

                texture.SetPixels32(colors);
                texture.Apply(false, false);
                return texture;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
    }
}
#endif

