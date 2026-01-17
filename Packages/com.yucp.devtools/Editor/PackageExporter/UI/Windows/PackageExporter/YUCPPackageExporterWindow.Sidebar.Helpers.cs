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
        private VisualElement CloneVisualElement(VisualElement source)
        {
            var clone = new VisualElement();
            
            // Copy all classes
            foreach (var className in source.GetClasses())
            {
                clone.AddToClassList(className);
            }
            
            // Copy style properties
            clone.style.width = source.resolvedStyle.width;
            clone.style.height = source.resolvedStyle.height;
            clone.style.backgroundColor = source.resolvedStyle.backgroundColor;
            clone.style.marginLeft = source.resolvedStyle.marginLeft;
            clone.style.marginRight = source.resolvedStyle.marginRight;
            clone.style.marginTop = source.resolvedStyle.marginTop;
            clone.style.marginBottom = source.resolvedStyle.marginBottom;
            clone.style.paddingLeft = source.resolvedStyle.paddingLeft;
            clone.style.paddingRight = source.resolvedStyle.paddingRight;
            clone.style.paddingTop = source.resolvedStyle.paddingTop;
            clone.style.paddingBottom = source.resolvedStyle.paddingBottom;
            clone.style.flexGrow = source.resolvedStyle.flexGrow;
            clone.style.flexShrink = source.resolvedStyle.flexShrink;
            clone.style.flexDirection = source.resolvedStyle.flexDirection;
            clone.style.alignItems = source.resolvedStyle.alignItems;
            
            // Clone children
            foreach (var child in source.Children())
            {
                if (child is Label label)
                {
                    var labelClone = new Label(label.text);
                    foreach (var className in label.GetClasses())
                    {
                        labelClone.AddToClassList(className);
                    }
                    labelClone.style.fontSize = label.resolvedStyle.fontSize;
                    labelClone.style.color = label.resolvedStyle.color;
                    labelClone.style.unityFontStyleAndWeight = label.resolvedStyle.unityFontStyleAndWeight;
                    clone.Add(labelClone);
                }
                else if (child is Image img)
                {
                    var imgClone = new Image();
                    imgClone.image = img.image;
                    foreach (var className in img.GetClasses())
                    {
                        imgClone.AddToClassList(className);
                    }
                    imgClone.style.width = img.resolvedStyle.width;
                    imgClone.style.height = img.resolvedStyle.height;
                    clone.Add(imgClone);
                }
                else
                {
                    // Recursively clone containers
                    var containerClone = CloneVisualElement(child);
                    clone.Add(containerClone);
                }
            }
            
            return clone;
        }

    }
}
