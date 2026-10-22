# Agent Scripts (`scripts/`)

Every script under `scripts/` and `.agents/skills/*/scripts/` is run by AI agents, and some agents (Gemini-family models)
cannot run bash or PowerShell. Write them so that any agent can run them.

## 1. Language

- **Node.js, built-ins only**: `.js` run as `node <path>`. Use only Node built-in modules (`fs`, `path`, `child_process`,
  ...); no npm packages, no `package.json`. Start with `#!/usr/bin/env node` and `'use strict';` (CommonJS).
- **No `.sh` and no `.ps1`**. The one exception is `scripts/Install-DevTools.ps1`, which installs Node itself through
  winget on Windows.
- **Logic the compiler owns goes in .NET, not in a script.** When a tool needs the compiler's own rules (name encoding,
  symbol files, diagnostics metadata), add a mode to `src/Compiler/Kassyi.GSharp.Compiler.Generators` that links the
  compiler source, and call it with `dotnet run --project src/Compiler/Kassyi.GSharp.Compiler.Generators -- <mode>`.
  Copying those rules into JavaScript creates a second definition that drifts. Example: the `demangle` mode
  (`LogDemangler.cs`) decodes with `GSharpNameEncoding.cs`. Reuse the compiler helpers listed in
  [helpers.md](../skills/gsharp-transpiler-guidelines/references/helpers.md) there as well.
- `justfile` recipes may stay shell; they call the scripts above.

## 2. Conventions

- Resolve the repository root from the script location (`path.resolve(__dirname, '..')`), not from the current directory.
- Options are `--kebab-case`; support `--help`. Exit with a non-zero code on failure.
- Begin with a header comment: purpose and usage.
- Never change a file's line endings or BOM when rewriting it.
- Write temporary output under `tmp/` or the agent's scratchpad, never in the repository root.

## 3. Catalog

| Command | Purpose |
| ----- | ----- |
| `node scripts/check-internal-names.js` | Lists `__gsharp_*` names in snapshot output that no YAML, Prelude, NativeApi YAML or source spelling explains |
| `node scripts/manage-diagnostics.js --next-id` / `--check-gaps` / `--check-docs` | Next free `GSHxxxx` ID, gaps in the IDs, diagnostics without a doc page |
| `node scripts/test-doc-snippet.js <snippet> [--assemblies A,B]` | Runs a doc snippet as a DocTest with exactly the given project references (`just doctest`) |
| `node scripts/format-markdown-table.js [path]` | Compacts Markdown tables (default `docs/spec`) |
| `node scripts/sync-comprehensive-spec.js` | Syncs the comprehensive example in the spec with its kernel and snapshot |
| `node scripts/update_usings.js` | Promotes `using` directives used in 3+ files to `GlobalUsings.cs` |
| `node .agents/skills/gsharp-transpiler-guidelines/scripts/check-helpers.js` | Checks that the helper catalog names only existing members |
| `dotnet run --project src/Compiler/Kassyi.GSharp.Compiler.Generators -- demangle <log> [symbols]` | Turns `__gsharp_*` names in a log back into C# names (`just gsharp-demangle`) |

Add a row when you add a script.
