using System;
using System.Text;

namespace YUCP.DevTools.Editor.PackageExporter
{
    internal static class EmbeddedTextEncodingUtility
    {
        private const string Base64Prefix = "yucp-b64:";

        public static string Encode(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return Base64Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        }

        public static bool TryDecode(string storedText, out string decodedText)
        {
            decodedText = storedText;

            if (string.IsNullOrEmpty(storedText) || !storedText.StartsWith(Base64Prefix, StringComparison.Ordinal))
                return true;

            try
            {
                decodedText = Encoding.UTF8.GetString(Convert.FromBase64String(storedText.Substring(Base64Prefix.Length)));
                return true;
            }
            catch (FormatException)
            {
                decodedText = null;
                return false;
            }
        }
    }
}
