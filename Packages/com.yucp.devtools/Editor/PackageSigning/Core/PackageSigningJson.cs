using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using YUCP.DevTools.Editor.PackageSigning.Data;

namespace YUCP.DevTools.Editor.PackageSigning.Core
{
    public static class PackageSigningJson
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            Converters = { new StringEnumConverter() },
        };

        public static string SerializeManifest(PackageManifest manifest)
        {
            return JsonConvert.SerializeObject(manifest, Settings);
        }

        public static string SerializeSignature(SignatureData signature)
        {
            return JsonConvert.SerializeObject(signature, Settings);
        }
    }
}
