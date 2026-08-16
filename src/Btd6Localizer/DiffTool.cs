namespace Btd6Localizer;

public static class DiffTool
{
    public static IReadOnlyList<string> Diff(LocData file1, LocData file2)
    {
        var sections1 = file1.Sections.ToDictionary(s => s.Name);
        var sections2 = file2.Sections.ToDictionary(s => s.Name);

        var sectionOrder = OrderedUnion(
            file1.Sections.Select(s => s.Name),
            file2.Sections.Select(s => s.Name));

        var lines = new List<string>();

        foreach (var sectionName in sectionOrder)
        {
            sections1.TryGetValue(sectionName, out var section1);
            sections2.TryGetValue(sectionName, out var section2);

            var entries1 = (section1?.Entries ?? Array.Empty<LocEntry>())
                .ToDictionary(e => e.Id, e => e.Text);
            var entries2 = (section2?.Entries ?? Array.Empty<LocEntry>())
                .ToDictionary(e => e.Id, e => e.Text);

            var idOrder = OrderedUnion(
                (section1?.Entries ?? Array.Empty<LocEntry>()).Select(e => e.Id),
                (section2?.Entries ?? Array.Empty<LocEntry>()).Select(e => e.Id));

            foreach (var id in idOrder)
            {
                var has1 = entries1.TryGetValue(id, out var text1);
                var has2 = entries2.TryGetValue(id, out var text2);

                if (has1 && !has2)
                {
                    lines.Add($"-[{sectionName}] {id}: {text1}");
                }
                else if (!has1 && has2)
                {
                    lines.Add($"+[{sectionName}] {id}: {text2}");
                }
                else if (has1 && has2 && text1 != text2)
                {
                    lines.Add($"-[{sectionName}] {id}: {text1}");
                    lines.Add($"+[{sectionName}] {id}: {text2}");
                }
            }
        }

        return lines;
    }

    private static List<string> OrderedUnion(IEnumerable<string> first, IEnumerable<string> second)
    {
        var seen = new HashSet<string>();
        var result = new List<string>();

        foreach (var item in first.Concat(second))
        {
            if (seen.Add(item))
            {
                result.Add(item);
            }
        }

        return result;
    }
}
