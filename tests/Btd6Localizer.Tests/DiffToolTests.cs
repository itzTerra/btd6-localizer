using Btd6Localizer;
using Xunit;

namespace Btd6Localizer.Tests;

public class DiffToolTests
{
    private static LocData Data(params (string Section, (string Id, string Text)[] Entries)[] sections) =>
        new(sections
            .Select(s => new LocSection(
                s.Section,
                s.Entries.Select(e => new LocEntry(e.Id, e.Text)).ToList()))
            .ToList());

    [Fact]
    public void Diff_IdenticalFiles_ReturnsNoLines()
    {
        var a = Data(("Input", new[] { ("Click", "Click") }));
        var b = Data(("Input", new[] { ("Click", "Click") }));

        var result = DiffTool.Diff(a, b);

        Assert.Empty(result);
    }

    [Fact]
    public void Diff_AddedEntry_ReturnsAdditionLine()
    {
        var a = Data(("Input", new[] { ("Click", "Click") }));
        var b = Data(("Input", new[] { ("Click", "Click"), ("Tap", "Tap") }));

        var result = DiffTool.Diff(a, b);

        Assert.Equal(new[] { "+[Input] Tap: Tap" }, result);
    }

    [Fact]
    public void Diff_RemovedEntry_ReturnsRemovalLine()
    {
        var a = Data(("Input", new[] { ("Click", "Click"), ("Tap", "Tap") }));
        var b = Data(("Input", new[] { ("Click", "Click") }));

        var result = DiffTool.Diff(a, b);

        Assert.Equal(new[] { "-[Input] Tap: Tap" }, result);
    }

    [Fact]
    public void Diff_ChangedEntry_ReturnsRemovalThenAddition()
    {
        var a = Data(("Input", new[] { ("Click", "Click") }));
        var b = Data(("Input", new[] { ("Click", "Klicka") }));

        var result = DiffTool.Diff(a, b);

        Assert.Equal(new[] { "-[Input] Click: Click", "+[Input] Click: Klicka" }, result);
    }

    [Fact]
    public void Diff_SectionOnlyInSecondFile_AppearsInFirstAppearanceOrder()
    {
        var a = Data(("Input", new[] { ("Click", "Click") }));
        var b = Data(
            ("Input", new[] { ("Click", "Click") }),
            ("TowerNames", new[] { ("DartMonkey", "Dart Monkey") }));

        var result = DiffTool.Diff(a, b);

        Assert.Equal(new[] { "+[TowerNames] DartMonkey: Dart Monkey" }, result);
    }

    [Fact]
    public void Diff_SectionOnlyInFirstFile_AppearsAsRemovals()
    {
        var a = Data(
            ("Input", new[] { ("Click", "Click") }),
            ("TowerNames", new[] { ("DartMonkey", "Dart Monkey") }));
        var b = Data(("Input", new[] { ("Click", "Click") }));

        var result = DiffTool.Diff(a, b);

        Assert.Equal(new[] { "-[TowerNames] DartMonkey: Dart Monkey" }, result);
    }
}
