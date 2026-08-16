using System.Xml;
using System.Xml.Linq;

namespace Btd6Localizer;

public sealed record LocEntry(string Id, string Text);

public sealed record LocSection(string Name, IReadOnlyList<LocEntry> Entries);

public sealed record LocData(IReadOnlyList<LocSection> Sections);

public static class LocXml
{
    public static LocData Parse(string xml)
    {
        if (!TryParse(xml, out var data, out var error))
        {
            throw new FormatException(error);
        }

        return data;
    }

    public static bool TryParse(string xml, out LocData data, out string error)
    {
        data = new LocData(Array.Empty<LocSection>());
        error = string.Empty;

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            error = $"Not valid XML: {ex.Message}";
            return false;
        }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != "LocData")
        {
            error = "Root element must be <LocData>.";
            return false;
        }

        var language = root.Element("Language");
        if (language is null)
        {
            error = "<LocData> must contain a <Language> element.";
            return false;
        }

        var sections = new List<LocSection>();
        foreach (var sectionEl in language.Elements())
        {
            var entries = new List<LocEntry>();
            foreach (var t in sectionEl.Elements("T"))
            {
                var id = t.Attribute("id")?.Value;
                if (id is null)
                {
                    error = $"<T> element in section <{sectionEl.Name.LocalName}> is missing an 'id' attribute.";
                    return false;
                }

                entries.Add(new LocEntry(id, t.Value));
            }

            sections.Add(new LocSection(sectionEl.Name.LocalName, entries));
        }

        data = new LocData(sections);
        return true;
    }
}
