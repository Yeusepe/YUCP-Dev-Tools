namespace YUCP.DevTools.Editor.PackageExporter
{
    /// <summary>
    /// Obfuscation protection levels for ConfuserEx.
    /// Each preset defines a different balance between protection strength and compatibility.
    /// </summary>
    public enum ConfuserExPreset
    {
        Mild,
        Normal,
        Aggressive,
        Maximum
    }

    /// <summary>
    /// Generates ConfuserEx protection configurations using preset level.
    /// </summary>
    public static class ConfuserExPresetGenerator
    {
        public static string GenerateProtectionRules(ConfuserExPreset preset, ObfuscationSettings settings = null)
        {
            switch (preset)
            {
                case ConfuserExPreset.Mild:
                    return @"
    <!-- Mild protection - basic name obfuscation and string encryption -->
    <protection id=""rename"">
      <argument name=""mode"" value=""letters"" />
      <argument name=""renEnum"" value=""true"" />
    </protection>
    
    <protection id=""constants"">
      <argument name=""mode"" value=""normal"" />
      <argument name=""decoderCount"" value=""3"" />
    </protection>";

                case ConfuserExPreset.Normal:
                    return @"
    <!-- Normal protection - UNITY COMPATIBLE (NO anti-tamper) -->
    <protection id=""anti ildasm"" />
    
    <protection id=""rename"">
      <argument name=""mode"" value=""letters"" />
      <argument name=""renEnum"" value=""true"" />
    </protection>
    
    <protection id=""constants"">
      <argument name=""mode"" value=""normal"" />
      <argument name=""decoderCount"" value=""5"" />
    </protection>";

                case ConfuserExPreset.Aggressive:
                    return @"
    <!-- Aggressive protection - maximum obfuscation (may affect performance) -->
    <protection id=""anti ildasm"" />
    
    <protection id=""anti tamper"">
      <argument name=""key"" value=""dynamic"" />
    </protection>
    
    <protection id=""anti debug"" />
    
    <protection id=""anti dump"" />
    
    <protection id=""ctrl flow"">
      <argument name=""type"" value=""switch"" />
      <argument name=""predicate"" value=""expression"" />
    </protection>
    
    <protection id=""ref proxy"">
      <argument name=""mode"" value=""strong"" />
      <argument name=""typeErasure"" value=""true"" />
      <argument name=""depth"" value=""5"" />
    </protection>
    
    <protection id=""rename"">
      <argument name=""mode"" value=""unicode"" />
      <argument name=""renEnum"" value=""true"" />
      <argument name=""renameArgs"" value=""true"" />
      <argument name=""flatten"" value=""true"" />
    </protection>
    
    <protection id=""constants"">
      <argument name=""mode"" value=""dynamic"" />
      <argument name=""decoderCount"" value=""10"" />
    </protection>
    
    <protection id=""resources"">
      <argument name=""mode"" value=""dynamic"" />
    </protection>";

                case ConfuserExPreset.Maximum:
                    return GenerateMaximumPreset(settings);

                default:
                    return GenerateProtectionRules(ConfuserExPreset.Normal, settings);
            }
        }

        public static string GetPresetDescription(ConfuserExPreset preset)
        {
            switch (preset)
            {
                case ConfuserExPreset.Mild:
                    return "Basic protection - Renames symbols and encrypts strings. Fast and compatible.";
                
                case ConfuserExPreset.Normal:
                    return "Recommended protection - Full obfuscation with control flow and anti-tampering. Good balance.";
                
                case ConfuserExPreset.Aggressive:
                    return "Maximum protection - All features enabled. May impact performance and compatibility.";
                
                case ConfuserExPreset.Maximum:
                    return "Maximum Unity-compatible protection - All safe obfuscation features enabled. Works with Mono and IL2CPP.";
                
                default:
                    return "";
            }
        }
        
        /// <summary>
        /// Generate maximum preset with optional custom settings
        /// </summary>
        public static string GenerateMaximumPreset(ObfuscationSettings settings = null)
        {
            if (settings == null)
            {
                settings = ObfuscationSettings.GetMaximum();
            }
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("    <!-- Maximum protection - UNITY COMPATIBLE (excludes anti-tamper/anti-debug/anti-dump) -->");
            sb.AppendLine("    <protection id=\"anti ildasm\" />");
            sb.AppendLine();
            
            if (settings.enableControlFlow)
            {
                sb.AppendLine("    <protection id=\"ctrl flow\">");
                sb.AppendLine("      <argument name=\"type\" value=\"switch\" />");
                sb.AppendLine("      <argument name=\"predicate\" value=\"expression\" />");
                sb.AppendLine($"      <argument name=\"intensity\" value=\"{settings.controlFlowIntensity}\" />");
                sb.AppendLine("      <argument name=\"depth\" value=\"6\" />");
                sb.AppendLine("    </protection>");
                sb.AppendLine();
            }
            
            if (settings.enableReferenceProxy)
            {
                sb.AppendLine("    <protection id=\"ref proxy\">");
                sb.AppendLine("      <argument name=\"mode\" value=\"strong\" />");
                sb.AppendLine("      <argument name=\"typeErasure\" value=\"true\" />");
                sb.AppendLine($"      <argument name=\"depth\" value=\"{settings.referenceProxyDepth}\" />");
                sb.AppendLine("    </protection>");
                sb.AppendLine();
            }
            
            string renameMode = settings.renameMode switch
            {
                ObfuscationSettings.RenameMode.Letters => "letters",
                ObfuscationSettings.RenameMode.Unicode => "unicode",
                ObfuscationSettings.RenameMode.Sequential => "sequential",
                _ => "unicode"
            };
            
            sb.AppendLine("    <protection id=\"rename\">");
            sb.AppendLine($"      <argument name=\"mode\" value=\"{renameMode}\" />");
            sb.AppendLine("      <argument name=\"renEnum\" value=\"true\" />");
            sb.AppendLine("      <argument name=\"renameArgs\" value=\"true\" />");
            sb.AppendLine("      <argument name=\"flatten\" value=\"true\" />");
            sb.AppendLine("    </protection>");
            sb.AppendLine();
            
            if (settings.enableConstants)
            {
                sb.AppendLine("    <protection id=\"constants\">");
                sb.AppendLine("      <argument name=\"mode\" value=\"dynamic\" />");
                sb.AppendLine("      <argument name=\"decoderCount\" value=\"10\" />");
                sb.AppendLine("    </protection>");
                sb.AppendLine();
            }
            
            if (settings.enableResources)
            {
                sb.AppendLine("    <protection id=\"resources\">");
                sb.AppendLine("      <argument name=\"mode\" value=\"dynamic\" />");
                sb.AppendLine("    </protection>");
                sb.AppendLine();
            }
            
            if (settings.enableInvalidMetadata)
            {
                sb.AppendLine("    <protection id=\"invalid metadata\" />");
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Generate Unity exclusion patterns for reflection-dependent code
        /// </summary>
        public static string GenerateUnityExclusionPatterns(ObfuscationSettings settings)
        {
            if (settings == null || !settings.preserveReflection)
            {
                return "";
            }
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("  <!-- Unity-specific exclusions for reflection-dependent code -->");
            sb.AppendLine("  <rule pattern=\"member-type('type') and full-name('UnityEngine.*')\" inherit=\"false\">");
            sb.AppendLine("    <protection id=\"rename\" action=\"remove\" />");
            sb.AppendLine("  </rule>");
            sb.AppendLine();
            sb.AppendLine("  <rule pattern=\"member-type('type') and full-name('UnityEditor.*')\" inherit=\"false\">");
            sb.AppendLine("    <protection id=\"rename\" action=\"remove\" />");
            sb.AppendLine("  </rule>");
            sb.AppendLine();
            sb.AppendLine("  <rule pattern=\"has-attr('System.Serializable')\" inherit=\"false\">");
            sb.AppendLine("    <protection id=\"rename\" action=\"remove\" />");
            sb.AppendLine("  </rule>");
            sb.AppendLine();
            sb.AppendLine("  <rule pattern=\"name('Awake') or name('Start') or name('Update') or name('FixedUpdate') or name('LateUpdate') or name('OnEnable') or name('OnDisable') or name('OnDestroy')\" inherit=\"false\">");
            sb.AppendLine("    <protection id=\"ctrl flow\" action=\"remove\" />");
            sb.AppendLine("  </rule>");
            sb.AppendLine();
            sb.AppendLine("  <rule pattern=\"name('OnSerialize') or name('OnDeserialize') or name('OnBeforeSerialize') or name('OnAfterDeserialize')\" inherit=\"false\">");
            sb.AppendLine("    <protection id=\"ctrl flow\" action=\"remove\" />");
            sb.AppendLine("    <protection id=\"rename\" action=\"remove\" />");
            sb.AppendLine("  </rule>");
            sb.AppendLine();
            sb.AppendLine("  <rule pattern=\"name('OnValidate') or name('Reset')\" inherit=\"false\">");
            sb.AppendLine("    <protection id=\"ctrl flow\" action=\"remove\" />");
            sb.AppendLine("    <protection id=\"rename\" action=\"remove\" />");
            sb.AppendLine("  </rule>");
            
            return sb.ToString();
        }
    }
}

