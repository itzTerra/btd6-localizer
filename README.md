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

**Before overwriting a language slot, extract it first and check whether
it already contains something you want to keep.** Overwriting is not
limited to vanilla, unused languages — if you (or a previous run of this
tool) already put a custom translation into a slot, `apply` will silently
replace it like any other language, backups aside.

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
