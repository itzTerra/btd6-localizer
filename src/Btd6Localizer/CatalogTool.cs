using AddressablesTools;
using AddressablesTools.Catalog;
using AddressablesTools.Classes;

namespace Btd6Localizer;

public static class CatalogTool
{
    public static int ZeroOutCrcForBundles(
        string sourceCatalogPath, string destCatalogPath, IReadOnlySet<string> bundleFileNames)
    {
        var bytes = File.ReadAllBytes(sourceCatalogPath);
        var catalog = AddressablesCatalogFileParser.FromBinaryData(bytes);

        var patchedCount = 0;
        foreach (var bucket in catalog.Resources.Values)
        {
            foreach (var location in bucket)
            {
                if (location.Data is not WrappedSerializedObject { Object: AssetBundleRequestOptions options })
                {
                    continue;
                }

                var matches = bundleFileNames.Any(name =>
                    location.InternalId.Contains(name, StringComparison.OrdinalIgnoreCase));

                if (matches)
                {
                    options.Crc = 0;
                    patchedCount++;
                }
            }
        }

        if (patchedCount == 0)
        {
            throw new Btd6LocalizerException(
                $"No catalog.bin entries matched bundle name(s): {string.Join(", ", bundleFileNames)}. " +
                "The catalog format or path layout may have changed since this tool was built.");
        }

        File.WriteAllBytes(destCatalogPath, AddressablesCatalogFileParser.ToBinaryData(catalog));
        return patchedCount;
    }
}
