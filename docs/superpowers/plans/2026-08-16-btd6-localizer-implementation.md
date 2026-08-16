# BTD6 Localizer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a cross-platform .NET 8 console tool that extracts BTD6's
in-bundle localization XML for translation and safely re-injects a translated
XML file back into a real BTD6 install (bundle + `catalog.bin` CRC patch),
with staged writes, timestamped backups, and an interactive fallback mode.

**Architecture:** A single console project (`src/Btd6Localizer`) with four
pure/wrapper modules — `LocXml` (parse the `<LocData>` XML format),
`DiffTool` (pure section/id/text comparison), `BundleTool` (AssetsTools.NET
wrapper for reading/repacking the Unity bundle), `CatalogTool`
(AddressablesTools wrapper for `catalog.bin` CRC patching) — orchestrated by
`Program.cs`, which does CLI parsing, action dispatch, and Spectre.Console
interactive prompts for any missing arguments. `LocXml` and `DiffTool` are
pure and unit-tested; `BundleTool` and `CatalogTool` touch real binary game
files and are verified by hand against a real BTD6 install, per the spec.

**Tech Stack:** .NET 8 (C#), AssetsTools.NET, AssetsTools.NET.Addressables
(AddressablesTools), System.IO.Hashing, Spectre.Console, xUnit.

**Spec:** [docs/superpowers/specs/2026-08-16-btd6-localizer-design.md](../specs/2026-08-16-btd6-localizer-design.md)

## Global Constraints

- Target `.NET 8`, C# console app, cross-platform.
- Only the Unity `StandaloneWindows64` Addressables build target is
  supported (Windows build of BTD6, run natively or via Wine) — not
  mobile/console layouts.
- No restore/rollback action — manual restore from timestamped backups only.
- No support for adding a genuinely new language — only overwriting an
  existing language slot.
- Every action that modifies files stages all writes to temp locations
  first; the real install is only touched after all staging succeeds.
- Every real file `apply` overwrites is copied to
  `<file>.backup-<ISO8601 timestamp>` first, unconditionally, every run.
- `apply` always prints a summary and requires explicit y/n confirmation
  before writing to the real install, unless `--yes` is passed.
- Malformed input, an unknown `--lang`, or an unrecognizable `<btd6_dir>`
  must fail with a clear, specific message before any file is touched.
- If a required argument is missing/invalid, fall back to an interactive
  Spectre.Console prompt for just that piece, rather than erroring
  immediately.
- Two bundle copies must be patched when present: `.../Full/` and
  `.../Half/` under `StreamingAssets/aa/StandaloneWindows64/`; only `Full`
  is read from for extraction.

## Confirmed reference: `<LocData>` XML schema

Sample localization XML recovered from the investigation session's
scratchpad (`English.xml`, `Swedish.xml`, `Czech.xml`) confirms the real
on-disk schema used by every `TextAsset` in the bundle:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<LocData>
  <Language>
    <Input>
      <T id="Click">Click</T>
      <T id="Tap">Tap</T>
    </Input>
    <TowerNames>
      <T id="DartMonkey">Dart Monkey</T>
      <T id="BoomerangMonkey">Boomerang Monkey</T>
    </TowerNames>
  </Language>
</LocData>
```

- Root `<LocData>` has exactly one `<Language>` child.
- Every direct child element of `<Language>` is a "section" — its tag name
  (e.g. `Input`, `TowerNames`) is the section name; section names are not a
  fixed enum, so parsing must treat them generically.
- Each section contains `<T id="...">text</T>` children — `id` is the
  entry's key, the element's text content is the translatable value.
- The XML declaration quote style varies between files (`"..."` vs.
  `'...'`) — this is irrelevant to parsing and must NOT be normalized by
  `extract` (which preserves original bytes exactly) or `apply` (which
  passes the translation file's raw content straight through).

This schema drives `LocXml.cs` (Task 2) and is the basis for all test
fixtures in this plan.

---

## Task 1: Repo and solution scaffolding

**Files:**
- Create: `Btd6Localizer.sln`
- Create: `src/Btd6Localizer/Btd6Localizer.csproj`
- Create: `src/Btd6Localizer/Program.cs` (minimal placeholder)
- Create: `tests/Btd6Localizer.Tests/Btd6Localizer.Tests.csproj`
- Create: `tests/Btd6Localizer.Tests/PlaceholderTests.cs` (deleted in Task 2)
- Create: `.gitignore`

**Interfaces:**
- Produces: a buildable, testable solution skeleton that every later task
  adds files into. No public API yet.

- [ ] **Step 1: Create the console project**

```bash
dotnet new sln -n Btd6Localizer
dotnet new console -o src/Btd6Localizer -n Btd6Localizer
dotnet sln add src/Btd6Localizer/Btd6Localizer.csproj
```

- [ ] **Step 2: Edit the csproj for nullable/implicit usings and set the root namespace**

Edit `src/Btd6Localizer/Btd6Localizer.csproj` to:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Btd6Localizer</RootNamespace>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>

</Project>
```

- [ ] **Step 3: Add the third-party package references**

```bash
dotnet add src/Btd6Localizer/Btd6Localizer.csproj package AssetsTools.NET
dotnet add src/Btd6Localizer/Btd6Localizer.csproj package AssetsTools.NET.Addressables
dotnet add src/Btd6Localizer/Btd6Localizer.csproj package System.IO.Hashing
dotnet add src/Btd6Localizer/Btd6Localizer.csproj package Spectre.Console
```

Do not pin exact versions by hand — let `dotnet add package` resolve and
record the current latest compatible version in the csproj so the
restored packages actually exist on NuGet.

- [ ] **Step 4: Verify the console project builds and runs**

Run: `dotnet run --project src/Btd6Localizer`
Expected: prints the default "Hello, World!" console template output with
no errors. This confirms all four package references restore successfully
before any tool code depends on them.

- [ ] **Step 5: Create the test project and wire it into the solution**

```bash
dotnet new xunit -o tests/Btd6Localizer.Tests -n Btd6Localizer.Tests
dotnet sln add tests/Btd6Localizer.Tests/Btd6Localizer.Tests.csproj
dotnet add tests/Btd6Localizer.Tests/Btd6Localizer.Tests.csproj reference src/Btd6Localizer/Btd6Localizer.csproj
```

- [ ] **Step 6: Run the default generated test to confirm the harness works**

Run: `dotnet test`
Expected: 1 test passes (the template's `Test1` placeholder).

- [ ] **Step 7: Add a `.gitignore` for standard .NET build output**

```
bin/
obj/
*.user
```

- [ ] **Step 8: Commit**

```bash
git add Btd6Localizer.sln src/Btd6Localizer tests/Btd6Localizer.Tests .gitignore
git commit -m "chore: scaffold Btd6Localizer console app and test project"
```

---

## Task 2: `LocXml.cs` — parse the `<LocData>` format (TDD)

**Files:**
- Create: `src/Btd6Localizer/LocXml.cs`
- Test: `tests/Btd6Localizer.Tests/LocXmlTests.cs`
- Modify: delete `tests/Btd6Localizer.Tests/PlaceholderTests.cs` (the
  generated `UnitTest1.cs` from Task 1)

**Interfaces:**
- Consumes: nothing (pure, `System.Xml.Linq` only).
- Produces: `LocEntry(string Id, string Text)`, `LocSection(string Name,
  IReadOnlyList<LocEntry> Entries)`, `LocData(IReadOnlyList<LocSection>
  Sections)`, `LocXml.Parse(string xml) -> LocData` (throws
  `FormatException` on invalid input), `LocXml.TryParse(string xml, out
  LocData data, out string error) -> bool`. Used by Task 4 (`DiffTool`) and
  Task 10 (`apply`'s validation step).

- [ ] **Step 1: Delete the generated placeholder test**

```bash
rm tests/Btd6Localizer.Tests/UnitTest1.cs
```

(If Task 1's `dotnet new xunit` generated a different filename, delete
whatever `.cs` file it produced instead.)

- [ ] **Step 2: Write the failing tests**

Create `tests/Btd6Localizer.Tests/LocXmlTests.cs`:

```csharp
using Btd6Localizer;
using Xunit;

namespace Btd6Localizer.Tests;

public class LocXmlTests
{
    [Fact]
    public void Parse_ValidDocument_ReturnsSectionsAndEntriesInOrder()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <LocData>
              <Language>
                <Input>
                  <T id="Click">Click</T>
                  <T id="Tap">Tap</T>
                </Input>
                <TowerNames>
                  <T id="DartMonkey">Dart Monkey</T>
                </TowerNames>
              </Language>
            </LocData>
            """;

        var data = LocXml.Parse(xml);

        Assert.Equal(2, data.Sections.Count);
        Assert.Equal("Input", data.Sections[0].Name);
        Assert.Equal(new[] { "Click", "Tap" }, data.Sections[0].Entries.Select(e => e.Id));
        Assert.Equal("Tap", data.Sections[0].Entries[1].Text);
        Assert.Equal("TowerNames", data.Sections[1].Name);
        Assert.Equal("Dart Monkey", data.Sections[1].Entries[0].Text);
    }

    [Fact]
    public void TryParse_NotXml_ReturnsFalseWithError()
    {
        var ok = LocXml.TryParse("not xml at all", out _, out var error);

        Assert.False(ok);
        Assert.Contains("valid XML", error);
    }

    [Fact]
    public void TryParse_MissingLanguageElement_ReturnsFalseWithError()
    {
        var ok = LocXml.TryParse("<LocData></LocData>", out _, out var error);

        Assert.False(ok);
        Assert.Contains("Language", error);
    }

    [Fact]
    public void TryParse_WrongRootElement_ReturnsFalseWithError()
    {
        var ok = LocXml.TryParse("<NotLocData></NotLocData>", out _, out var error);

        Assert.False(ok);
        Assert.Contains("LocData", error);
    }

    [Fact]
    public void TryParse_MissingIdAttribute_ReturnsFalseWithError()
    {
        const string xml = "<LocData><Language><Input><T>Click</T></Input></Language></LocData>";

        var ok = LocXml.TryParse(xml, out _, out var error);

        Assert.False(ok);
        Assert.Contains("id", error);
    }

    [Fact]
    public void Parse_InvalidDocument_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => LocXml.Parse("<LocData></LocData>"));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter LocXmlTests`
Expected: FAIL — compile error, `LocXml` does not exist yet.

- [ ] **Step 4: Implement `LocXml.cs`**

Create `src/Btd6Localizer/LocXml.cs`:

```csharp
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
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter LocXmlTests`
Expected: PASS — all 6 tests green.

- [ ] **Step 6: Commit**

```bash
git add src/Btd6Localizer/LocXml.cs tests/Btd6Localizer.Tests/LocXmlTests.cs
git rm tests/Btd6Localizer.Tests/UnitTest1.cs
git commit -m "feat: parse the LocData XML format"
```

---

## Task 3: `DiffTool.cs` — pure section/id/text comparison (TDD)

**Files:**
- Create: `src/Btd6Localizer/DiffTool.cs`
- Test: `tests/Btd6Localizer.Tests/DiffToolTests.cs`

**Interfaces:**
- Consumes: `LocData`, `LocSection`, `LocEntry` from Task 2 (`LocXml.cs`).
- Produces: `DiffTool.Diff(LocData file1, LocData file2) ->
  IReadOnlyList<string>`. Used by Task 5 (`diff` CLI wiring).

Output format (one line per changed entry, unified-diff style):
- Entry only in `file2`: `+[Section] id: text`
- Entry only in `file1`: `-[Section] id: text`
- Entry in both with different text: a `-` line with the old text
  immediately followed by a `+` line with the new text.
- Identical entries: omitted entirely.
- Section order: union of both files' sections, in the order each section
  first appears scanning `file1` then `file2`. Entry order within a
  section follows the same first-appearance rule.

- [ ] **Step 1: Write the failing tests**

Create `tests/Btd6Localizer.Tests/DiffToolTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter DiffToolTests`
Expected: FAIL — compile error, `DiffTool` does not exist yet.

- [ ] **Step 3: Implement `DiffTool.cs`**

Create `src/Btd6Localizer/DiffTool.cs`:

```csharp
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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter DiffToolTests`
Expected: PASS — all 6 tests green.

- [ ] **Step 5: Commit**

```bash
git add src/Btd6Localizer/DiffTool.cs tests/Btd6Localizer.Tests/DiffToolTests.cs
git commit -m "feat: add pure LocData diff logic"
```

---

## Task 4: CLI skeleton, argument parsing, and `diff` wiring

**Files:**
- Modify: `src/Btd6Localizer/Program.cs` (replace template content entirely)

**Interfaces:**
- Consumes: `LocXml.TryParse` (Task 2), `DiffTool.Diff` (Task 3).
- Produces: `Btd6LocalizerException` (used by every later task for
  user-facing errors), `ParsedArgs` shape and the `RunDiff`/`RunExtract`/
  `RunApply` dispatch entry points that Tasks 7, 10, and 11 extend.
  `RunExtract` and `RunApply` are stubbed to throw `NotImplementedException`
  in this task and implemented in Tasks 7 and 10.

This task has no dedicated unit tests — `Program.cs` is the CLI entry point
and is exercised via `dotnet run` manual checks, consistent with the
spec's testing split (pure logic is unit-tested; orchestration/I/O is
manually verified).

- [ ] **Step 1: Replace `Program.cs` with the CLI skeleton**

Create/overwrite `src/Btd6Localizer/Program.cs`:

```csharp
using Spectre.Console;

namespace Btd6Localizer;

public sealed class Btd6LocalizerException : Exception
{
    public Btd6LocalizerException(string message) : base(message)
    {
    }
}

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            Run(args);
            return 0;
        }
        catch (Btd6LocalizerException ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    private static void Run(string[] args)
    {
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : PromptForAction();
        var rest = args.Length > 0 ? args[1..] : Array.Empty<string>();

        switch (action)
        {
            case "extract":
                RunExtract(rest);
                break;
            case "apply":
                RunApply(rest);
                break;
            case "diff":
                RunDiff(rest);
                break;
            default:
                throw new Btd6LocalizerException(
                    $"Unknown action '{action}'. Valid actions: extract, apply, diff.");
        }
    }

    private static string PromptForAction() =>
        AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddChoices("extract", "apply", "diff"));

    private static void RunDiff(string[] args)
    {
        var file1 = args.Length > 0 ? args[0] : PromptForExistingFilePath("Path to the first XML file:");
        var file2 = args.Length > 1 ? args[1] : PromptForExistingFilePath("Path to the second XML file:");

        var data1 = ParseLocXmlFileOrThrow(file1);
        var data2 = ParseLocXmlFileOrThrow(file2);

        foreach (var line in DiffTool.Diff(data1, data2))
        {
            Console.WriteLine(line);
        }
    }

    private static LocData ParseLocXmlFileOrThrow(string path)
    {
        if (!File.Exists(path))
        {
            throw new Btd6LocalizerException($"File not found: '{path}'.");
        }

        var xml = File.ReadAllText(path);
        if (!LocXml.TryParse(xml, out var data, out var error))
        {
            throw new Btd6LocalizerException($"'{path}' is not a valid LocData XML file: {error}");
        }

        return data;
    }

    private static string PromptForExistingFilePath(string promptText)
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>(promptText)
                .Validate(path => File.Exists(path)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("File does not exist. Try again.")));
    }

    private static void RunExtract(string[] args)
    {
        throw new NotImplementedException("Implemented in a later task.");
    }

    private static void RunApply(string[] args)
    {
        throw new NotImplementedException("Implemented in a later task.");
    }
}
```

- [ ] **Step 2: Manually verify `diff` end-to-end**

Create two small sample files in the scratchpad and run diff against them:

```bash
mkdir -p /tmp/btd6-diff-check
cat > /tmp/btd6-diff-check/a.xml <<'EOF'
<LocData><Language><Input><T id="Click">Click</T></Input></Language></LocData>
EOF
cat > /tmp/btd6-diff-check/b.xml <<'EOF'
<LocData><Language><Input><T id="Click">Klicka</T></Input></Language></LocData>
EOF
dotnet run --project src/Btd6Localizer -- diff /tmp/btd6-diff-check/a.xml /tmp/btd6-diff-check/b.xml
```

Expected output:
```
-[Input] Click: Click
+[Input] Click: Klicka
```

- [ ] **Step 3: Manually verify the missing-file error path**

Run: `dotnet run --project src/Btd6Localizer -- diff /tmp/btd6-diff-check/a.xml /nonexistent.xml`
Expected: prints `Error: File not found: '/nonexistent.xml'.` and exits
with a non-zero code (check with `echo $?` on bash or `$LASTEXITCODE` on
PowerShell).

- [ ] **Step 4: Commit**

```bash
git add src/Btd6Localizer/Program.cs
git commit -m "feat: add CLI skeleton, argument dispatch, and diff action"
```

---

## Task 5: `BundleTool.cs` — read localization TextAssets from a bundle

**Files:**
- Create: `src/Btd6Localizer/BundleTool.cs`

**Interfaces:**
- Consumes: `AssetsManager`, `AssetClassID.TextAsset` from
  `AssetsTools.NET`/`AssetsTools.NET.Extra` (Task 1's package reference).
- Produces: `LocalizationEntry(string LanguageName, string XmlContent)`,
  `BundleTool.ReadAllLanguages(string bundlePath) ->
  IReadOnlyList<LocalizationEntry>`,
  `BundleTool.ListLanguageNames(string bundlePath) -> IReadOnlyList<string>`.
  Used by Task 6 (`extract` wiring) and Task 9 (`apply`'s language-name
  validation/prompt).

This wraps a real third-party binary-asset library. There is no local BTD6
install to test against automatically in this environment, so this task's
correctness is verified by hand against a real BTD6 install in Step 4 —
this mirrors how the spec itself validated this exact approach during
investigation (see the spec's "Background" section).

- [ ] **Step 1: Implement `BundleTool.cs`**

Create `src/Btd6Localizer/BundleTool.cs`:

```csharp
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
```

- [ ] **Step 2: Build and fix any API mismatches**

Run: `dotnet build src/Btd6Localizer`

If `AssetsManager`, `LoadBundleFile`, `LoadAssetsFileFromBundle`,
`GetAssetsOfType`, `AssetClassID.TextAsset`, or `GetBaseField` don't match
the installed `AssetsTools.NET` package's actual signatures, use your
IDE's "Go to Definition" (or `ilspycmd` on the restored NuGet DLL under
`~/.nuget/packages/assetstools.net/`) to find the real names and adjust
this file accordingly. The shape of the fix should stay the same: load
the bundle, load the one embedded assets file, enumerate `TextAsset`
objects, read `m_Name` and `m_Script` off each one's base field.

Expected after fixing: builds with no errors.

- [ ] **Step 3: Manually verify against a real BTD6 install**

Add a temporary throwaway call (e.g. a one-off `Console.WriteLine` loop in
`Main`, removed before committing, or a scratch `.csx`/test project) that
calls `BundleTool.ReadAllLanguages` against the real bundle path on this
machine:
`<btd6_dir>/StreamingAssets/aa/StandaloneWindows64/Full/localization_assets_all_*.bundle`

Expected: prints one entry per language (English, Swedish, Czech, etc.),
and the `XmlContent` for `English` matches the known-good sample recovered
earlier in this session (starts with `<?xml version="1.0"
encoding="UTF-8"?><LocData><Language><Input>...`).

- [ ] **Step 4: Commit**

```bash
git add src/Btd6Localizer/BundleTool.cs
git commit -m "feat: read localization TextAssets out of a BTD6 bundle"
```

---

## Task 6: Wire up the `extract` action

**Files:**
- Modify: `src/Btd6Localizer/Program.cs`

**Interfaces:**
- Consumes: `BundleTool.ReadAllLanguages` (Task 5).
- Produces: a working `extract <btd6_dir> <xml_output_dir>` CLI action and
  the reusable `FindBundlePath(string btd6Dir, string variant) -> string`
  helper that Task 9 (`apply`) also uses for both `Full` and `Half`.

- [ ] **Step 1: Add bundle-path discovery and the `extract` handler**

In `src/Btd6Localizer/Program.cs`, replace the `RunExtract` stub and add
a shared path-finding helper:

```csharp
    private static void RunExtract(string[] args)
    {
        var btd6Dir = args.Length > 0
            ? RequireExistingDirectory(args[0], "BTD6 install directory")
            : PromptForExistingDirectory("Path to the BTD6 install directory:");
        var outputDir = args.Length > 1
            ? args[1]
            : AnsiConsole.Ask<string>("Path to write extracted XML files to:");

        var bundlePath = FindBundlePath(btd6Dir, "Full");

        var languages = BundleTool.ReadAllLanguages(bundlePath);

        Directory.CreateDirectory(outputDir);
        foreach (var entry in languages)
        {
            var outPath = Path.Combine(outputDir, $"{entry.LanguageName}.xml");
            File.WriteAllText(outPath, entry.XmlContent);
        }

        AnsiConsole.MarkupLineInterpolated(
            $"[green]Extracted {languages.Count} language(s) to '{outputDir}'.[/]");
    }

    private static string FindBundlePath(string btd6Dir, string variant)
    {
        var expectedDir = Path.Combine(
            btd6Dir, "StreamingAssets", "aa", "StandaloneWindows64", variant);

        if (Directory.Exists(expectedDir))
        {
            var direct = Directory.GetFiles(expectedDir, "localization_assets_all_*.bundle");
            if (direct.Length > 0)
            {
                return direct[0];
            }
        }

        var fallback = Directory.GetFiles(
            btd6Dir, "localization_assets_all_*.bundle", SearchOption.AllDirectories)
            .Where(p => p.Contains(Path.DirectorySeparatorChar + variant + Path.DirectorySeparatorChar)
                     || p.Contains(Path.AltDirectorySeparatorChar + variant + Path.AltDirectorySeparatorChar))
            .ToArray();

        if (fallback.Length > 0)
        {
            return fallback[0];
        }

        throw new Btd6LocalizerException(
            $"Could not find a '{variant}' localization_assets_all_*.bundle under '{btd6Dir}'. " +
            "Make sure this is a BTD6 StandaloneWindows64 install directory.");
    }

    private static string RequireExistingDirectory(string path, string description)
    {
        if (!Directory.Exists(path))
        {
            throw new Btd6LocalizerException($"{description} does not exist: '{path}'.");
        }

        return path;
    }

    private static string PromptForExistingDirectory(string promptText)
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>(promptText)
                .Validate(path => Directory.Exists(path)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Directory does not exist. Try again.")));
    }
```

Note: `FindBundlePath`'s non-interactive path throws
`Btd6LocalizerException` rather than prompting — the interactive fallback
for an invalid `btd6_dir` happens one level up, at the `btd6Dir` argument
prompt itself (`PromptForExistingDirectory` only checks the directory
exists, not that it contains a bundle; tightening that validation is
covered in Task 11, which adds the "and, where relevant, contain the
expected bundle/catalog structure" check from the spec's interactive-mode
section).

- [ ] **Step 2: Build**

Run: `dotnet build src/Btd6Localizer`
Expected: builds with no errors.

- [ ] **Step 3: Manually verify against a real BTD6 install**

Run: `dotnet run --project src/Btd6Localizer -- extract "<real btd6 dir>" /tmp/btd6-extract-out`

Expected: prints `Extracted N language(s) to '/tmp/btd6-extract-out'.`, and
`/tmp/btd6-extract-out/English.xml` byte-for-byte matches the known-good
`English.xml` sample from the investigation session.

- [ ] **Step 4: Manually verify the not-found error path**

Run: `dotnet run --project src/Btd6Localizer -- extract /tmp /tmp/whatever`
Expected: prints a clear `Error: Could not find a 'Full' localization_assets_all_*.bundle under '/tmp'...` message and exits non-zero.

- [ ] **Step 5: Commit**

```bash
git add src/Btd6Localizer/Program.cs
git commit -m "feat: wire up the extract action"
```

---

## Task 7: `CatalogTool.cs` — patch `catalog.bin` bundle CRCs

**Files:**
- Create: `src/Btd6Localizer/CatalogTool.cs`

**Interfaces:**
- Consumes: `AddressablesTools`/`AddressablesTools.Catalog` types from the
  `AssetsTools.NET.Addressables` package (Task 1).
- Produces: `CatalogTool.ZeroOutCrcForBundles(string sourceCatalogPath,
  string destCatalogPath, IReadOnlySet<string> bundleFileNames) -> int`
  (returns the number of entries patched). Used by Task 10 (`apply`
  wiring).

Like Task 5, this wraps a third-party binary library and is verified by
hand against a real `catalog.bin`, consistent with the spec's testing
strategy for this file.

- [ ] **Step 1: Implement `CatalogTool.cs`**

Create `src/Btd6Localizer/CatalogTool.cs`:

```csharp
using AddressablesTools;
using AddressablesTools.Catalog;

namespace Btd6Localizer;

public static class CatalogTool
{
    public static int ZeroOutCrcForBundles(
        string sourceCatalogPath, string destCatalogPath, IReadOnlySet<string> bundleFileNames)
    {
        var bytes = File.ReadAllBytes(sourceCatalogPath);
        var catalog = ContentCatalogData.Read(bytes);

        var patchedCount = 0;
        foreach (var bucket in catalog.Resources.Values)
        {
            foreach (var location in bucket)
            {
                if (location.Data is not AssetBundleRequestOptions options)
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

        File.WriteAllBytes(destCatalogPath, catalog.Write());
        return patchedCount;
    }
}
```

- [ ] **Step 2: Build and fix any API mismatches**

Run: `dotnet build src/Btd6Localizer`

If `ContentCatalogData.Read`/`.Write`, `Resources`, `AssetBundleRequestOptions`,
or `InternalId`/`Crc` don't match the installed `AssetsTools.NET.Addressables`
package's actual signatures, use "Go to Definition" or decompile the
restored NuGet DLL (`~/.nuget/packages/assetstools.net.addressables/`) to
find the real API and adjust this file. The shape of the fix should stay
the same: read the catalog, find every location whose `Data` is bundle
request options and whose internal id references one of the target bundle
filenames, zero its CRC, re-serialize.

Expected after fixing: builds with no errors.

- [ ] **Step 3: Manually verify against a real `catalog.bin`**

Add a temporary throwaway call against the real
`<btd6_dir>/StreamingAssets/aa/catalog.bin`, targeting the actual
`localization_assets_all_<hash>.bundle` filename found in Task 6's Step 3.

Expected: returns a patched count > 0, and the written catalog file is
non-empty and loadable by re-reading it with `ContentCatalogData.Read`
(sanity round-trip: read → write → read again should not throw).

- [ ] **Step 4: Commit**

```bash
git add src/Btd6Localizer/CatalogTool.cs
git commit -m "feat: patch catalog.bin bundle CRCs to zero"
```

---

## Task 8: `BundleTool.cs` — replace a language and repack the bundle

**Files:**
- Modify: `src/Btd6Localizer/BundleTool.cs`

**Interfaces:**
- Consumes: `AssetsReplacerFromMemory`, `BundleReplacerFromMemory`,
  `AssetBundleFile`, `AssetBundleCompressionType.LZ4` from
  `AssetsTools.NET`.
- Produces: `BundleTool.ReplaceLanguage(string sourceBundlePath, string
  destBundlePath, string languageName, string newXmlContent)`. Throws
  `Btd6LocalizerException` if `languageName` isn't found. Used by Task 9
  (`apply` wiring).

- [ ] **Step 1: Add `ReplaceLanguage` to `BundleTool.cs`**

Append to `src/Btd6Localizer/BundleTool.cs`:

```csharp
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
            var newAssetBytes = targetField.WriteToByteArray();
            var assetReplacer = new AssetsReplacerFromMemory(target, newAssetBytes);

            byte[] newAssetsFileBytes;
            using (var assetsStream = new MemoryStream())
            {
                using var assetsWriter = new AssetsFileWriter(assetsStream);
                afileInst.file.Write(assetsWriter, 0, new List<AssetsReplacer> { assetReplacer });
                newAssetsFileBytes = assetsStream.ToArray();
            }

            var bundleReplacer = new BundleReplacerFromMemory(
                afileInst.name, afileInst.name, true, newAssetsFileBytes, -1);

            using var uncompressedStream = new MemoryStream();
            using (var bundleWriter = new AssetsFileWriter(uncompressedStream))
            {
                bunInst.file.Write(bundleWriter, new List<BundleReplacer> { bundleReplacer });
            }

            uncompressedStream.Position = 0;
            var repackedBundle = new AssetBundleFile();
            repackedBundle.Read(new AssetsFileReader(uncompressedStream));

            using var destStream = File.Open(destBundlePath, FileMode.Create, FileAccess.Write);
            using var destWriter = new AssetsFileWriter(destStream);
            repackedBundle.Pack(repackedBundle.Reader, destWriter, AssetBundleCompressionType.LZ4);
        }
        finally
        {
            am.UnloadAll();
        }
    }
```

This follows the validated two-phase approach from the spec's Background
section: write the modified bundle uncompressed to a `MemoryStream` first,
then re-read and `Pack(..., AssetBundleCompressionType.LZ4)` it — never
serialize compressed in one pass.

- [ ] **Step 2: Build and fix any API mismatches**

Run: `dotnet build src/Btd6Localizer`

Same caveat as Task 5/7: if `AssetsReplacerFromMemory`,
`BundleReplacerFromMemory`, `AssetsFileWriter`, `AssetsFileReader`,
`AssetBundleFile.Read`/`.Pack`, or `WriteToByteArray` don't match the
installed package's real signatures, fix names via "Go to Definition" /
decompiled source, keeping the two-phase write-then-pack shape intact.

Expected after fixing: builds with no errors.

- [ ] **Step 3: Manually verify round-trip correctness against a real bundle**

Using the real `Full` bundle path from Task 6:

1. Extract `English` via the `extract` action (Task 6) to get its exact
   original content.
2. Call `BundleTool.ReplaceLanguage(fullBundlePath, "/tmp/repacked.bundle",
   "English", <that same unmodified content>)` — a no-op content
   replacement.
3. Call `BundleTool.ReadAllLanguages("/tmp/repacked.bundle")` and confirm
   the `English` entry's `XmlContent` is unchanged and every other
   language is still present with unchanged content.

Expected: repacked bundle round-trips cleanly with no data loss. (Full
in-game launch verification — confirming BTD6 actually loads this
repacked bundle without the `ArgumentException: The scene is invalid`
crash described in the spec — happens in Task 14's end-to-end checklist,
once `catalog.bin` patching, from Task 7, is wired in together with this.)

- [ ] **Step 4: Commit**

```bash
git add src/Btd6Localizer/BundleTool.cs
git commit -m "feat: replace a language's TextAsset content and repack the bundle"
```

---

## Task 9: Wire up the `apply` action

**Files:**
- Modify: `src/Btd6Localizer/Program.cs`

**Interfaces:**
- Consumes: `LocXml.TryParse` (Task 2), `FindBundlePath` (Task 6),
  `BundleTool.ListLanguageNames`/`ReplaceLanguage` (Tasks 5, 8),
  `CatalogTool.ZeroOutCrcForBundles` (Task 7).
- Produces: a working `apply <translation_file.xml> <btd6_dir> --lang
  <LanguageName> [--yes]` CLI action with staged writes, timestamped
  backups, and a confirmation gate.

- [ ] **Step 1: Add flag parsing and the `apply` handler**

In `src/Btd6Localizer/Program.cs`, replace the `RunApply` stub:

```csharp
    private static void RunApply(string[] args)
    {
        var argList = args.ToList();
        var yes = argList.Remove("--yes");
        var lang = ExtractFlagValue(argList, "--lang");
        var positional = argList.ToArray();

        var translationFile = positional.Length > 0
            ? positional[0]
            : PromptForExistingFilePath("Path to the translated XML file:");
        var btd6Dir = positional.Length > 1
            ? RequireExistingDirectory(positional[1], "BTD6 install directory")
            : PromptForExistingDirectory("Path to the BTD6 install directory:");

        if (!File.Exists(translationFile))
        {
            throw new Btd6LocalizerException($"File not found: '{translationFile}'.");
        }

        var translationXml = File.ReadAllText(translationFile);
        if (!LocXml.TryParse(translationXml, out _, out var parseError))
        {
            throw new Btd6LocalizerException(
                $"'{translationFile}' is not a valid LocData XML file: {parseError}");
        }

        var fullBundlePath = FindBundlePath(btd6Dir, "Full");
        var halfBundlePath = TryFindBundlePath(btd6Dir, "Half");

        var availableLanguages = BundleTool.ListLanguageNames(fullBundlePath);

        if (lang is null || !availableLanguages.Contains(lang))
        {
            if (args.Length == 0 || !args.Contains("--lang"))
            {
                lang = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Which language slot should be overwritten?")
                        .AddChoices(availableLanguages));
            }
            else
            {
                throw new Btd6LocalizerException(
                    $"'--lang {lang}' is not a valid language slot. Available: {string.Join(", ", availableLanguages)}");
            }
        }

        var catalogPath = Path.Combine(btd6Dir, "StreamingAssets", "aa", "catalog.bin");
        if (!File.Exists(catalogPath))
        {
            throw new Btd6LocalizerException($"catalog.bin not found at expected path: '{catalogPath}'.");
        }

        var bundlePaths = halfBundlePath is null
            ? new[] { fullBundlePath }
            : new[] { fullBundlePath, halfBundlePath };

        AnsiConsole.MarkupLineInterpolated($"[yellow]About to overwrite language slot '{lang}' using '{translationFile}'.[/]");
        foreach (var path in bundlePaths)
        {
            AnsiConsole.MarkupLineInterpolated($"  Bundle: {path} (backup: {path}.backup-<timestamp>)");
        }
        AnsiConsole.MarkupLineInterpolated($"  Catalog: {catalogPath} (backup: {catalogPath}.backup-<timestamp>)");

        if (!yes && !AnsiConsole.Confirm("Proceed?"))
        {
            AnsiConsole.MarkupLine("[grey]Aborted.[/]");
            return;
        }

        var stagingDir = Directory.CreateTempSubdirectory("btd6-localizer-").FullName;
        try
        {
            var stagedBundlePaths = new List<(string Real, string Staged)>();
            foreach (var realPath in bundlePaths)
            {
                var stagedPath = Path.Combine(stagingDir, Path.GetFileName(realPath));
                BundleTool.ReplaceLanguage(realPath, stagedPath, lang!, translationXml);
                stagedBundlePaths.Add((realPath, stagedPath));
            }

            var bundleFileNames = bundlePaths.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase)!;
            var stagedCatalogPath = Path.Combine(stagingDir, "catalog.bin");
            CatalogTool.ZeroOutCrcForBundles(catalogPath, stagedCatalogPath, bundleFileNames!);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");

            foreach (var (realPath, stagedPath) in stagedBundlePaths)
            {
                File.Copy(realPath, $"{realPath}.backup-{timestamp}", overwrite: true);
                File.Copy(stagedPath, realPath, overwrite: true);
            }

            File.Copy(catalogPath, $"{catalogPath}.backup-{timestamp}", overwrite: true);
            File.Copy(stagedCatalogPath, catalogPath, overwrite: true);

            AnsiConsole.MarkupLineInterpolated(
                $"[green]Applied '{lang}' to {stagedBundlePaths.Count} bundle(s) and catalog.bin. Backups written with suffix '.backup-{timestamp}'.[/]");
        }
        finally
        {
            Directory.Delete(stagingDir, recursive: true);
        }
    }

    private static string? ExtractFlagValue(List<string> args, string flagName)
    {
        var index = args.IndexOf(flagName);
        if (index < 0 || index + 1 >= args.Count)
        {
            return null;
        }

        var value = args[index + 1];
        args.RemoveAt(index + 1);
        args.RemoveAt(index);
        return value;
    }

    private static string? TryFindBundlePath(string btd6Dir, string variant)
    {
        try
        {
            return FindBundlePath(btd6Dir, variant);
        }
        catch (Btd6LocalizerException)
        {
            return null;
        }
    }
```

Note on staging: every bundle/catalog rewrite (`BundleTool.ReplaceLanguage`,
`CatalogTool.ZeroOutCrcForBundles`) happens against paths inside
`stagingDir` before any real file is touched. If any of those calls throw,
the `finally` block cleans up the staging directory and no real file has
been copied over yet — satisfying the spec's "if any staging step fails,
nothing in the real install is touched" requirement.

- [ ] **Step 2: Build**

Run: `dotnet build src/Btd6Localizer`
Expected: builds with no errors.

- [ ] **Step 3: Manually verify the full `apply` flow against a real, backed-up BTD6 install**

**Before running this step, make an independent full copy of the BTD6
install directory outside of this tool's own backup mechanism** — this
step writes real files.

1. Run: `dotnet run --project src/Btd6Localizer -- apply /tmp/btd6-extract-out/English.xml "<real btd6 dir>" --lang Swedish`
2. Confirm the summary lists the correct bundle path(s), `catalog.bin`
   path, and backup filenames, then answer `y`.
3. Confirm `.backup-<timestamp>` files now exist next to the real bundle(s)
   and `catalog.bin`.
4. Launch BTD6 and confirm it starts without the
   `ArgumentException: The scene is invalid` crash, and that switching the
   in-game language to the Swedish slot now shows English text.
5. Restore from the `.backup-*` files to return the install to its
   original state.

Expected: all of the above hold. This is the end-to-end confirmation that
Task 7's catalog patch and Task 8's bundle repack combine correctly — the
per-task manual checks in Tasks 7/8 only verified each piece in isolation.

- [ ] **Step 4: Manually verify the confirmation gate and `--yes`**

- Run apply without `--yes` and answer `n` at the prompt — expected:
  prints `Aborted.` and no files change.
- Run apply with `--yes` — expected: no confirmation prompt appears, and
  files are changed immediately.

- [ ] **Step 5: Manually verify the fail-fast paths**

- Run apply with a malformed XML file (e.g. `echo not-xml >
  /tmp/bad.xml`) — expected: fails with a clear parse error before any
  prompt or file write.
- Run apply with `--lang DoesNotExist` — expected: fails listing the
  actual available language names, before any prompt or file write.

- [ ] **Step 6: Commit**

```bash
git add src/Btd6Localizer/Program.cs
git commit -m "feat: wire up the apply action with staged writes and backups"
```

---

## Task 10: Interactive mode polish

**Files:**
- Modify: `src/Btd6Localizer/Program.cs`

**Interfaces:**
- Consumes: everything wired in Tasks 4, 6, 9.
- Produces: no new public API — tightens existing prompts so that a
  `btd6_dir` prompt (used by both `extract` and `apply`) validates the
  directory actually contains a locatable bundle before accepting it, per
  the spec's interactive-mode requirement ("validated to exist and, where
  relevant, to contain the expected bundle/catalog structure").

- [ ] **Step 1: Tighten `PromptForExistingDirectory` to validate bundle presence**

Replace the single shared `PromptForExistingDirectory` helper in
`src/Btd6Localizer/Program.cs` with a version that re-prompts until the
directory both exists and contains a `Full` bundle:

```csharp
    private static string PromptForExistingDirectory(string promptText)
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>(promptText)
                .Validate(path =>
                {
                    if (!Directory.Exists(path))
                    {
                        return ValidationResult.Error("Directory does not exist. Try again.");
                    }

                    try
                    {
                        FindBundlePath(path, "Full");
                    }
                    catch (Btd6LocalizerException ex)
                    {
                        return ValidationResult.Error(ex.Message);
                    }

                    return ValidationResult.Success();
                }));
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Btd6Localizer`
Expected: builds with no errors.

- [ ] **Step 3: Manually verify the full interactive walkthrough**

Run: `dotnet run --project src/Btd6Localizer` (no arguments at all).

Expected: arrow-key menu for `extract`/`apply`/`diff` appears; picking
`apply` prompts for the translation file path (re-prompting on a
nonexistent path), then the BTD6 directory (re-prompting on a directory
without a locatable bundle), then an arrow-key language selection
populated from the real bundle's language slots, then the same
confirmation-gate summary as the non-interactive path from Task 9.

Also verify the partial case from the spec: `dotnet run --project
src/Btd6Localizer -- apply <file.xml> <btd6_dir>` (no `--lang`) prompts
only for the language, skipping the file/directory prompts since those
were already supplied.

- [ ] **Step 4: Commit**

```bash
git add src/Btd6Localizer/Program.cs
git commit -m "feat: validate bundle presence during interactive directory prompts"
```

---

## Task 11: `run.sh` bootstrap script

**Files:**
- Create: `run.sh`

**Interfaces:**
- Consumes: nothing from this project's C# code — a pure shell wrapper
  around `dotnet run`.
- Produces: the `./run.sh <args>` entry point end users invoke.

- [ ] **Step 1: Write `run.sh`**

Create `run.sh` at the repo root:

```bash
#!/usr/bin/env bash
set -euo pipefail

MIN_DOTNET_MAJOR=8

have_dotnet() {
    command -v dotnet >/dev/null 2>&1
}

dotnet_major_version() {
    dotnet --version 2>/dev/null | cut -d. -f1
}

confirm() {
    local reply
    read -r -p "$1 [y/N] " reply
    [[ "$reply" =~ ^[Yy]$ ]]
}

install_dotnet_linux() {
    if command -v apt >/dev/null 2>&1; then
        echo "About to run: sudo apt update && sudo apt install -y dotnet-sdk-8.0"
        confirm "Proceed?" || exit 1
        sudo apt update && sudo apt install -y dotnet-sdk-8.0
    elif command -v dnf >/dev/null 2>&1; then
        echo "About to run: sudo dnf install -y dotnet-sdk-8.0"
        confirm "Proceed?" || exit 1
        sudo dnf install -y dotnet-sdk-8.0
    elif command -v pacman >/dev/null 2>&1; then
        echo "About to run: sudo pacman -S --noconfirm dotnet-sdk"
        confirm "Proceed?" || exit 1
        sudo pacman -S --noconfirm dotnet-sdk
    else
        echo "No supported package manager (apt, dnf, pacman) found."
        echo "Install the .NET 8 SDK manually: https://dotnet.microsoft.com/download/dotnet/8.0"
        exit 1
    fi
}

install_dotnet_windows() {
    if command -v winget >/dev/null 2>&1; then
        echo "About to run: winget install Microsoft.DotNet.SDK.8"
        confirm "Proceed?" || exit 1
        winget install Microsoft.DotNet.SDK.8
    else
        echo "winget was not found."
        echo "Install the .NET 8 SDK manually: https://dotnet.microsoft.com/download/dotnet/8.0"
        exit 1
    fi
}

install_dotnet() {
    case "$(uname -s)" in
        Linux*)
            install_dotnet_linux
            ;;
        MINGW*|MSYS*|CYGWIN*)
            install_dotnet_windows
            ;;
        *)
            echo "No supported automatic installer for this platform ($(uname -s))."
            echo "Install the .NET 8 SDK manually: https://dotnet.microsoft.com/download/dotnet/8.0"
            exit 1
            ;;
    esac
}

if ! have_dotnet || [[ "$(dotnet_major_version)" -lt "$MIN_DOTNET_MAJOR" ]]; then
    echo ".NET SDK $MIN_DOTNET_MAJOR or newer is required but was not found."
    install_dotnet
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec dotnet run --project "$SCRIPT_DIR/src/Btd6Localizer" -- "$@"
```

- [ ] **Step 2: Make it executable**

```bash
chmod +x run.sh
```

- [ ] **Step 3: Manually verify**

Run: `./run.sh diff /tmp/btd6-diff-check/a.xml /tmp/btd6-diff-check/b.xml`
(reusing the fixture files from Task 4's Step 2, or recreate them if the
scratchpad was cleared)

Expected: since `dotnet` is already installed and new enough, it skips
straight to forwarding args and prints the same diff output as Task 4
Step 2, with no install prompts.

- [ ] **Step 4: Commit**

```bash
git add run.sh
git commit -m "feat: add run.sh bootstrap script"
```

---

## Task 12: `run.ps1` bootstrap script

**Files:**
- Create: `run.ps1`

**Interfaces:**
- Consumes: nothing from this project's C# code.
- Produces: the `.\run.ps1 <args>` entry point for native Windows/PowerShell users.

- [ ] **Step 1: Write `run.ps1`**

Create `run.ps1` at the repo root:

```powershell
#Requires -Version 5.1
$ErrorActionPreference = "Stop"

function Get-DotnetMajorVersion {
    try {
        $verString = & dotnet --version 2>$null
        if (-not $verString) { return -1 }
        return [int]($verString.Split('.')[0])
    } catch {
        return -1
    }
}

$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
$major = if ($dotnetCmd) { Get-DotnetMajorVersion } else { -1 }

if ($major -lt 8) {
    Write-Host ".NET SDK 8 or newer is required but was not found."

    $wingetCmd = Get-Command winget -ErrorAction SilentlyContinue
    if ($wingetCmd) {
        Write-Host "About to run: winget install Microsoft.DotNet.SDK.8"
        $reply = Read-Host "Proceed? [y/N]"
        if ($reply -notmatch '^[Yy]$') {
            exit 1
        }
        winget install Microsoft.DotNet.SDK.8
    } else {
        Write-Host "winget was not found."
        Write-Host "Install the .NET 8 SDK manually: https://dotnet.microsoft.com/download/dotnet/8.0"
        exit 1
    }
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
dotnet run --project (Join-Path $scriptDir "src/Btd6Localizer") -- @args
```

- [ ] **Step 2: Manually verify (on Windows)**

Run: `.\run.ps1 diff C:\path\to\a.xml C:\path\to\b.xml` (using fixtures
from Task 4)

Expected: same diff output as Task 4 Step 2, no install prompts (since
`dotnet` is already installed on this machine per Task 1's verification).

- [ ] **Step 3: Commit**

```bash
git add run.ps1
git commit -m "feat: add run.ps1 bootstrap script"
```

---

## Task 13: `README.md`

**Files:**
- Create: `README.md`

**Interfaces:**
- Consumes: nothing — documentation only.
- Produces: the repo's root-level usage documentation.

- [ ] **Step 1: Write `README.md`**

Create `README.md` at the repo root:

```markdown
# BTD6 Localizer

A command-line tool for extracting Bloons TD 6's localization strings for
translation, and safely applying a translated file back into a real BTD6
install by overwriting an existing (unused) language slot.

This tool only moves XML data in and out of BTD6's files correctly — it
does not translate anything. Translation is a separate, manual (or
LLM-assisted) step you do on the extracted `.xml` files.

## How it works

BTD6 ships its localization strings inside a Unity Addressables asset
bundle, one XML file per language. There's no supported way to add a new
language, but an existing slot (e.g. Swedish) can be overwritten with a
different language's text, as long as the bundle and the Addressables
`catalog.bin` integrity data stay consistent. This tool handles both parts
together, safely.

## Prerequisites

None to install by hand — `run.sh` / `run.ps1` check for the .NET 8 SDK
and offer to install it for you if it's missing.

## Usage

```
./run.sh extract <btd6_dir> <xml_output_dir>
./run.sh apply <translation_file.xml> <btd6_dir> --lang <LanguageName> [--yes]
./run.sh diff <file1.xml> <file2.xml>
./run.sh                         # no args at all -> interactive mode
```

On Windows (PowerShell), use `run.ps1` instead of `run.sh` with the same
arguments.

If you leave out a required argument, the tool prompts for just the
missing piece instead of erroring out — so `./run.sh apply
translated.xml /path/to/btd6` (no `--lang`) will only ask you to pick a
language.

### `extract`

Reads every language's XML out of a BTD6 install and writes one file per
language (`English.xml`, `Swedish.xml`, ...) into the output directory.

```
./run.sh extract "C:/Program Files/Epic Games/BloonsTD6" ./extracted
```

### `apply`

Overwrites one language slot in a real BTD6 install with a translated XML
file. Always prints a summary and asks for confirmation first (skip the
prompt with `--yes`). Every real file it's about to change is backed up
first, unconditionally, to `<file>.backup-<timestamp>`.

```
./run.sh apply ./translated/MyLanguage.xml "C:/Program Files/Epic Games/BloonsTD6" --lang Swedish
```

### `diff`

Compares two localization XML files section by section, id by id, and
prints a unified-diff-style summary of what changed.

```
./run.sh diff ./extracted/English.xml ./translated/MyLanguage.xml
```

## Safety

- Every write is staged in a temp location first; if anything goes wrong,
  nothing in your real BTD6 install is touched.
- Every file `apply` is about to overwrite is backed up first, every time,
  even if a backup from a previous run already exists.
- `apply` never writes to your install without an explicit "yes" (or
  `--yes`).

## Restoring a backup

There's no automated rollback. To undo an `apply`, copy the relevant
`.backup-<timestamp>` file back over the live file it came from.

## Limitations

- Only the Windows `StandaloneWindows64` build of BTD6 is supported (native
  or via Wine) — not mobile or console versions.
- Only overwriting an existing language slot is supported; you can't add a
  genuinely new language.
- A BTD6 update can overwrite the files this tool patches. If your
  translation stops appearing after an update, just run `apply` again.
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add README"
```

---

## Task 14: Full end-to-end manual verification checklist

**Files:**
- None (no code changes) — this task is a manual verification pass tying
  together every prior task against a real, disposable-copy BTD6 install.

**Interfaces:**
- Consumes: the complete CLI (`extract`, `apply`, `diff`, interactive
  mode) from Tasks 1–13.
- Produces: nothing new — confirms the whole tool as a system, which no
  individual task's manual check fully covered on its own (each prior
  task's manual step validated one piece in isolation).

- [ ] **Step 1: Run the full automated test suite**

Run: `dotnet test`
Expected: all `LocXmlTests` and `DiffToolTests` pass (12 tests total per
Tasks 2 and 3).

- [ ] **Step 2: Extract from a real install**

Run: `./run.sh extract "<real btd6 dir>" ./manual-check/extracted`
Expected: one `.xml` file per language, `English.xml` byte-identical to
the known-good sample.

- [ ] **Step 3: Round-trip diff**

Run: `./run.sh diff ./manual-check/extracted/English.xml ./manual-check/extracted/Swedish.xml`
Expected: a long list of `-`/`+` line pairs (every translatable string
differs between the two languages) with no errors.

- [ ] **Step 4: Apply, launch, and confirm in-game**

1. Back up the entire BTD6 install directory manually first (outside this
   tool), since this step modifies real files.
2. Run: `./run.sh apply ./manual-check/extracted/English.xml "<real btd6 dir>" --lang Swedish`
3. Confirm at the prompt.
4. Launch BTD6. Confirm it starts without crashing.
5. In-game, switch the language setting to the Swedish slot. Confirm the
   UI now shows English text.
6. Restore the independent backup from Step 4.1 to return the install to
   its original state.

Expected: all of the above hold — this is the final confirmation that the
`ArgumentException: The scene is invalid` crash described in the spec's
Background section does not occur with this tool's output.

- [ ] **Step 5: Confirm interactive mode end-to-end**

Run: `./run.sh` with no arguments and walk through `extract`, then
separately `apply`, using only the interactive prompts (no CLI args at
all). Confirm both complete successfully and match the behavior verified
in Tasks 6, 9, and 10.

No commit for this task — it's verification only. If any step surfaces a
bug, fix it as part of whichever task's file owns the broken behavior, and
re-run this checklist from Step 1.
