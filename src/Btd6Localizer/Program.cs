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

    private static void RunApply(string[] args)
    {
        throw new NotImplementedException("Implemented in a later task.");
    }
}
