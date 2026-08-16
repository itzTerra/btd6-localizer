using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace Btd6Localizer;

public sealed record LocalizationEntry(string LanguageName, string XmlContent);

public static class BundleTool
{
    public static IReadOnlyList<LocalizationEntry> ReadAllLanguages(string bundlePath)
    {
        var am = new AssetsManager();
        try
        {
            var bunInst = am.LoadBundleFile(bundlePath);
            var afileInst = am.LoadAssetsFileFromBundle(bunInst, 0);

            var results = new List<LocalizationEntry>();
            foreach (var info in afileInst.file.GetAssetsOfType(AssetClassID.TextAsset))
            {
                var baseField = am.GetBaseField(afileInst, info);
                var name = baseField["m_Name"].AsString;
                var script = baseField["m_Script"].AsString;
                results.Add(new LocalizationEntry(name, script));
            }

            return results;
        }
        finally
        {
            am.UnloadAll();
        }
    }

    public static IReadOnlyList<string> ListLanguageNames(string bundlePath) =>
        ReadAllLanguages(bundlePath).Select(e => e.LanguageName).ToList();
}
