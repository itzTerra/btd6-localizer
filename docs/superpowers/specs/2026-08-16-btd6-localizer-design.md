# BTD6 Localizer — Design Spec

Date: 2026-08-16

## Purpose

BTD6 (Bloons TD 6) ships its localization strings inside a Unity Addressables
asset bundle (`localization_assets_all_<hash>.bundle`), one `TextAsset` per
language, formatted as a simple `<LocData>` XML document. There is no
officially supported way to add a new language, but an existing language slot
(e.g. Swedish) can be overwritten with a different language's text without
modding the game, as long as the replacement bundle and the Addressables
`catalog.bin` integrity data stay consistent.

This tool automates that process: extracting the current localization content
for translation, and safely applying a translated XML file back into a real
BTD6 installation. Actual translation of the extracted text is out of scope —
this tool only moves data in and out of the game's files correctly.

## Background / why this approach

During manual investigation (this session) we established:

- The bundle can be edited by replacing a language's `TextAsset` content and
  re-serializing the bundle, but naive re-serialization (tried via UnityPy,
  a Python library) produces a bundle that is byte-different from Unity's own
  output even with **zero content changes**, and BTD6 crashes on launch
  (`ArgumentException: The scene is invalid` from Unity's Addressables scene
  loader) when loading it.
- Switching to `AssetsTools.NET` (a .NET library, the same engine behind the
  UABE/UABEA modding tools) to re-serialize the bundle produces the same
  crash on an unchanged re-serialization — ruling out the specific writer
  implementation as the cause.
- The actual cause is `catalog.bin`: Unity Addressables records a CRC per
  bundle in the catalog and refuses to load a bundle whose bytes don't match
  it, even though the *content* is logically identical. Any re-serialization
  changes the file's bytes at the container level, invalidating the CRC.
- The fix is to also patch `catalog.bin`, setting the CRC to `0` for the
  affected bundle's entries (which Unity's Addressables treats as "skip
  check"), using the `AddressablesTools` library (also by the UABE/UABEA
  author, NuGet package `AssetsTools.NET.Addressables`). This was verified
  end-to-end this session: patched catalog + original bundle launches fine;
  patched catalog + translated bundle launches fine and shows the translated
  text.
- `System.IO.Hashing` is a required transitive dependency for
  `AddressablesTools`' binary catalog writer that does not get pulled in
  automatically and must be referenced explicitly.
- BTD6 ships two identical (text-wise) copies of the localization bundle,
  under `StreamingAssets/aa/StandaloneWindows64/Full/` and `.../Half/`
  (presumably a texture-quality variant split); both must be patched for the
  fix to be reliable regardless of the user's graphics settings, though only
  `Full` needs to be read from for extraction since content is identical.

This spec is for a clean, reusable, cross-platform tool that encodes this
validated process, rather than the ad-hoc scripts written during
investigation.

## Non-goals

- Translating text. The tool moves XML files in and out of the game; a human
  (or a separate LLM-driven process) produces the actual translated content.
- Supporting platforms other than the Unity `StandaloneWindows64` Addressables
  build target (i.e. this targets the Windows build of BTD6, run via Wine or
  natively — not mobile or console builds, which use different asset
  layouts).
- A restore/rollback action — timestamped backups make manual restore
  straightforward, and an automated restore path adds scope without a clear
  need yet.
- Adding a genuinely new language (not present in the shipped bundle) — only
  overwriting an existing language slot is supported, consistent with what
  was validated.

## Tech stack

- **.NET 8**, C# console application. Cross-platform by default; matches the
  only validated working approach (AssetsTools.NET-based repacking).
- `AssetsTools.NET` — bundle reading/writing.
- `AssetsTools.NET.Addressables` — `catalog.bin` reading/writing and CRC
  patching.
- `System.IO.Hashing` — explicit dependency required by the above at runtime.
- `Spectre.Console` — interactive prompts and arrow-key selection menus for
  the no-args / missing-args interactive mode.

## Distribution

Two thin bootstrap scripts at the repo root are the only things an end user
needs to run:

- `run.sh` (Linux/macOS, also works under Git Bash on Windows)
- `run.ps1` (native Windows / PowerShell)

Both scripts:

1. Check whether `dotnet` (a .NET SDK, not just a runtime) is available and
   new enough.
2. If not, print exactly what will be installed and the exact command
   (`winget install Microsoft.DotNet.SDK.8` on Windows; the detected
   distro's package manager — `apt`, `dnf`, or `pacman` — on Linux), then
   wait for an explicit y/n confirmation before running it. If no supported
   package manager is detected, print manual install instructions and exit.
3. Forward all arguments to `dotnet run --project src/Btd6Localizer -- <args>`.
   NuGet package restore happens automatically as a normal part of
   `dotnet run`/`dotnet build` and does not require a separate confirmation
   (project-level dependency restore, not a system-level install).

## CLI interface

```
run.sh extract <btd6_dir> <xml_output_dir>
run.sh apply <translation_file.xml> <btd6_dir> --lang <LanguageName> [--yes]
run.sh diff <file1.xml> <file2.xml>
run.sh                         # no args at all → interactive mode
```

If a required argument for a given action is missing or invalid, the tool
does not error out immediately — it falls back to an interactive prompt
(Spectre.Console) for just the missing/invalid piece(s), so `run.sh apply`
with no further args behaves the same as full interactive mode, and
`run.sh apply file.xml /path/to/btd6` (missing `--lang`) only prompts for the
language.

### `extract <btd6_dir> <xml_output_dir>`

1. Search `<btd6_dir>` for
   `StreamingAssets/aa/StandaloneWindows64/Full/localization_assets_all_*.bundle`.
   If not found, search more broadly under `<btd6_dir>` for any
   `localization_assets_all_*.bundle` as a fallback (in case Ninja Kiwi
   changes the path layout in a future update), and error clearly if still
   not found.
2. Load the bundle (read-only) and enumerate every `TextAsset` inside it.
3. Write each one's content to `<xml_output_dir>/<LanguageName>.xml`
   (creating the directory if needed), preserving the original XML exactly
   as stored (this is already the native `<LocData>` format).
4. Print a summary: how many languages were extracted and to where.

### `apply <translation_file.xml> <btd6_dir> --lang <LanguageName> [--yes]`

1. Parse and validate `<translation_file.xml>` against the expected
   `<LocData><Language>...</Language></LocData>` structure. Fail with a clear
   message before touching anything else if it's malformed.
2. Locate the `Full` bundle the same way `extract` does, and additionally
   look for a sibling `Half` directory with the same bundle filename pattern
   (patch it too if present; it's fine if it's absent).
3. Determine the set of language slot names actually present in the bundle
   (by listing its `TextAsset`s). If `--lang` is missing or not among them,
   list the available names and, in interactive mode, let the user pick via
   arrow keys; in non-interactive mode, error out listing the valid options.
4. Locate `catalog.bin` (under `StreamingAssets/aa/catalog.bin` relative to
   `<btd6_dir>`, matching the layout observed this session).
5. Print a summary: which language slot will be overwritten, which bundle
   file(s) and `catalog.bin` will be modified, and the backup paths that will
   be created. Require an explicit y/n confirmation unless `--yes` was
   passed.
6. Stage all work in temp files first:
   - For each bundle file (Full, and Half if present): replace the target
     language's `TextAsset` content with the translation file's content,
     re-serialize (write-uncompressed-then-pack-LZ4, per the validated
     two-phase approach), producing a staged output file.
   - For `catalog.bin`: parse it, find every `AssetBundleRequestOptions`
     entry whose `InternalId` references the target bundle filename(s), set
     `Crc = 0` on each, and re-serialize to a staged output file.
7. Only after all staging succeeds: for each real file that will be
   overwritten (each bundle file, `catalog.bin`), copy it to
   `<original>.backup-<ISO8601 timestamp>` first, then copy the staged
   replacement over it.
8. Print a final summary of what was changed and where the backups are.

If any staging step fails, nothing in the real install is touched, and a
clear error explains what failed.

### `diff <file1.xml> <file2.xml>`

1. Parse both files into `section -> id -> text` maps.
2. For each section (union of both files' sections, in the order they first
   appear across the two files), compare entries by id:
   - Present in `file2` but not `file1`: print as an addition (`+`).
   - Present in `file1` but not `file2`: print as a removal (`-`).
   - Present in both but with different text: print as a removal of the old
     line followed by an addition of the new line (standard unified-diff
     style for a changed line).
   - Identical: omitted (not printed), consistent with `diff -u` behaviour.
3. Output goes to stdout.

### Interactive mode

Triggered whenever an action is invoked without all of its required
arguments resolved (including bare `run.sh` with no arguments at all).

1. If no action was given, prompt with an arrow-key menu: `extract`, `apply`,
   `diff`.
2. For each remaining missing argument, prompt appropriately:
   - Paths: free-text input, validated to exist (and, where relevant, to
     contain the expected bundle/catalog structure) before proceeding;
     re-prompt on invalid input.
   - `--lang`: arrow-key selection populated from the language slots actually
     found in the target bundle (requires the bundle path to already be
     known, so this prompt always comes after the `btd6_dir` prompt in
     `apply`).
3. Once all arguments are resolved, proceed exactly as the non-interactive
   path would, including the confirmation step in `apply` (interactive mode
   does not imply `--yes`).

## Safety & error handling

- **Staged writes**: every action that modifies files writes to temporary
  locations first; the real install is only touched in a final copy step
  once all staging has succeeded. A failure at any staging step leaves the
  real install completely untouched.
- **Timestamped backups, unconditionally**: every real file `apply` is about
  to overwrite is copied to `<file>.backup-<ISO8601 timestamp>` first, every
  run — never skipped, even if a backup already exists from a previous run.
- **Confirmation gate**: `apply` always prints a summary and requires
  explicit y/n confirmation before writing to the real install, unless
  `--yes` is passed.
- **Fail fast, before any writes**: malformed translation XML, a `--lang`
  that doesn't match any language slot in the bundle (non-interactive mode),
  or a `<btd6_dir>` that doesn't contain a recognizable bundle/catalog
  structure — all of these produce a clear, specific error message and exit
  before any file is touched.
- **No automated restore**: rollback is manual (copy the relevant
  `.backup-*` file back over the live file), which is straightforward given
  the timestamped backups.

## Project layout

```
btd6-localizer/
  run.sh
  run.ps1
  README.md
  docs/superpowers/specs/2026-08-16-btd6-localizer-design.md
  src/
    Btd6Localizer/
      Btd6Localizer.csproj
      Program.cs           # CLI parsing, action dispatch, interactive-mode orchestration
      BundleTool.cs         # extract + apply's bundle repacking (wraps AssetsTools.NET)
      CatalogTool.cs        # catalog.bin CRC patching (wraps AddressablesTools)
      LocXml.cs             # parse/serialize the <LocData> format; shared by all actions
      DiffTool.cs           # diff action's comparison logic
  tests/
    Btd6Localizer.Tests/    # unit tests for LocXml parsing and DiffTool
```

`LocXml.cs` and `DiffTool.cs` are pure logic (no file/bundle I/O) and are
unit tested directly. `BundleTool.cs` and `CatalogTool.cs` wrap third-party
libraries doing binary I/O against real game files and are validated via
manual/integration testing against a real BTD6 install rather than unit
tests, consistent with how this was verified during investigation.

## Documentation

`README.md` at the repo root: what the tool does, prerequisites (nothing —
the wrapper scripts bootstrap the SDK), and example invocations for each
action, including the interactive mode.

## Open questions / risks

- The exact bundle path layout
  (`StreamingAssets/aa/StandaloneWindows64/Full/...`) was observed on one
  BTD6 build (Epic Games, Windows, build 11249) and may differ on other
  storefronts or after major updates; the fallback broad search in `extract`
  and `apply` mitigates this somewhat but isn't foolproof.
- `catalog.bin`'s binary format (Binv1/Binv1.1/Binv2/Binv3 per
  AddressablesTools) is versioned; a future Unity/Addressables upgrade in
  BTD6 could ship a format this library doesn't yet support. This would
  surface as a clear parse error from `AddressablesTools`, not a silent
  failure or crash.
- This tool provides no protection against a game update overwriting the
  patched files — that's expected and out of scope (see Non-goals); the
  README should say so plainly.
