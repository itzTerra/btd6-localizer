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

    public static void ReplaceLanguage(
        string sourceBundlePath, string destBundlePath, string languageName, string newXmlContent)
    {
        var am = new AssetsManager();
        try
        {
            var bunInst = am.LoadBundleFile(sourceBundlePath);
            var afileInst = am.LoadAssetsFileFromBundle(bunInst, 0);

            AssetFileInfo? target = null;
            foreach (var info in afileInst.file.GetAssetsOfType(AssetClassID.TextAsset))
            {
                var baseField = am.GetBaseField(afileInst, info);
                if (baseField["m_Name"].AsString == languageName)
                {
                    target = info;
                    break;
                }
            }

            if (target is null)
            {
                throw new Btd6LocalizerException(
                    $"Language '{languageName}' not found in bundle '{sourceBundlePath}'.");
            }

            var targetField = am.GetBaseField(afileInst, target);
            targetField["m_Script"].AsString = newXmlContent;
            target.SetNewData(targetField);

            byte[] newAssetsFileBytes;
            using (var assetsStream = new MemoryStream())
            {
                using var assetsWriter = new AssetsFileWriter(assetsStream);
                afileInst.file.Write(assetsWriter);
                newAssetsFileBytes = assetsStream.ToArray();
            }

            var dirInfo = bunInst.file.BlockAndDirInfo.DirectoryInfos
                .First(d => d.Name == afileInst.name);
            dirInfo.SetNewData(newAssetsFileBytes);

            byte[] uncompressedBundleBytes;
            using (var uncompressedStream = new MemoryStream())
            {
                using var bundleWriter = new AssetsFileWriter(uncompressedStream);
                bunInst.file.Write(bundleWriter);
                uncompressedBundleBytes = uncompressedStream.ToArray();
            }

            using var readStream = new MemoryStream(uncompressedBundleBytes);
            var repackedBundle = new AssetBundleFile();
            repackedBundle.Read(new AssetsFileReader(readStream));

            using var destStream = File.Open(destBundlePath, FileMode.Create, FileAccess.Write);
            using var destWriter = new AssetsFileWriter(destStream);
            repackedBundle.Pack(destWriter, AssetBundleCompressionType.LZ4);
        }
        finally
        {
            am.UnloadAll();
        }
    }
}
