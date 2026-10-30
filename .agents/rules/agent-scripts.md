# Agent Scripts (`scripts/`)

Every script under `scripts/agent/` and `.agents/skills/*/scripts/` is run by AI agents, and some agents (Gemini-family models)
cannot run bash or PowerShell. Write them so that any agent can run them. The scripts under `scripts/human/` are run by people,
but follow the same language and convention rules (§1, §3).

## 1. Language

- **Node.js, built-ins only**: `.js` run as `node <path>`. Use only Node built-in modules (`fs`, `path`, `child_process`,
  ...); no npm packages, no `package.json`. Start with `#!/usr/bin/env node` and `'use strict';` (CommonJS).
- **No `.sh` and no `.ps1`**. The one exception is `scripts/human/Install-DevTools.ps1`, which installs Node itself through
  winget on Windows.
- **Logic the compiler owns goes in .NET, not in a script.** When a tool needs the compiler's own rules (name encoding,
  symbol files, diagnostics metadata), add a mode to `src/Compiler/Kassyi.GSharp.Compiler.Generators` that links the
  compiler source, and call it with `dotnet run --project src/Compiler/Kassyi.GSharp.Compiler.Generators -- <mode>`.
  Copying those rules into JavaScript creates a second definition that drifts. Example: the `demangle` mode
  (`LogDemangler.cs`) decodes with `GSharpNameEncoding.cs`. Reuse the compiler helpers listed in
  [helpers.md](../skills/gsharp-transpiler-guidelines/references/helpers.md) there as well.
- `justfile` recipes may stay shell; they call the scripts above.

## 2. Folders

- `scripts/agent/`: checks and tools that an agent uses inside its own procedures (internal-name check, diagnostics registry,
  spec sync, the GPU lock).
- `scripts/human/`: what a person runs to prepare the development environment, in CI, or through a `just` recipe. Anything
  that a `just` recipe calls goes here.
- A new script goes in the folder of its caller. A script that both an agent procedure and a `just` recipe call is a `just`
  recipe's script, so it goes in `human/`.

## 3. Conventions

- Resolve the repository root from the script location (`path.resolve(__dirname, '..')`), not from the current directory.
- Options are `--kebab-case`; support `--help`. Exit with a non-zero code on failure.
- Begin with a header comment: purpose and usage.
- Never change a file's line endings or BOM when rewriting it.
- Write temporary output under `tmp/` or the agent's scratchpad, never in the repository root.

## 4. Catalog

| Command | Purpose |
| ----- | ----- |
| `node scripts/agent/check-internal-names.js` | Lists `__gsharp_*` names in snapshot output that no YAML, Prelude, NativeApi YAML or source spelling explains |
| `node scripts/agent/manage-diagnostics.js --next-id` / `--check-gaps` / `--check-docs` | Next free `GSHxxxx` ID, gaps in the IDs, diagnostics without a doc page |
| `node scripts/human/test-doc-snippet.js <snippet> [--assemblies A,B]` | Runs a doc snippet as a DocTest with exactly the given project references (`just doctest`) |
| `node scripts/human/coverage-diff.js [--run] [--base <ref>] [--coverage <file\|dir>]` | Lists the lines changed since `<ref>` (default `main`) that no test executed, from the merged cobertura reports (`just coverage-diff`) |
| `node scripts/human/bug-curve.js --out <file.svg> [--repo <owner/name>] [--bucket auto\|day\|week\|month] [--issues <file.tsv>]` | Draws the open issues of milestones M1–M3 per day as a stacked area SVG, with the issues opened and closed per day, week or month as bars (`auto` picks the finest that stays readable) and date ticks spaced by days, months or years; `--issues` reads a TSV instead of calling `gh` (CI: `bug-curve.yml` publishes it to the `metrics` branch for README.md) |
| `node scripts/human/ci-solution-filter.js [--exclude <project>]... [--out <file.slnf>]` | Writes a solution filter of `Henicos.slnx` without the given projects (CI: the `Other tests` jobs of `gpu-tests.yml` build and test everything but Tests.Cpu and Tests.Cuda, so a new project is covered without a workflow change) |
| `node scripts/human/ci-test-durations.js <file.trx\|dir>... [--top <n>] [--source <dir>] [--no-source]` | Prints the test time per assembly, per xunit collection (read from the `[Collection(...)]` attributes of the sources), per test class, and the slowest tests, from `.trx` files (CI: the `test-results-<os>-<suite>` artifacts of `gpu-tests.yml`, kept 7 days) |
| `node scripts/agent/format-markdown-table.js [path]` | Compacts Markdown tables (default `docs/spec`) |
| `node scripts/agent/sync-comprehensive-spec.js` | Syncs the comprehensive example in the spec with its kernel and snapshot |
| `node scripts/agent/update_usings.js` | Promotes `using` directives used in 3+ files to `GlobalUsings.cs` |
| `node scripts/agent/gpu-lock.js [--poll-seconds <n>] [--timeout-minutes <n>] -- "<command>"` | Runs the command while holding the shared GPU lock `<main checkout>/tmp/gpu-test-lock` (the same for every worktree); waits while another run holds it, shows the owner on a timeout, takes over a lock whose owner (same host and platform) and the command it started have both exited, releases only a lock it created, also on Ctrl+C; `--lock-path <dir>` is for tests of the script only |
| `node .agents/skills/gsharp-transpiler-guidelines/scripts/check-helpers.js` | Checks that the helper catalog names only existing members |
| `dotnet run --project src/Compiler/Kassyi.GSharp.Compiler.Generators -- demangle <log> [symbols]` | Turns `__gsharp_*` names in a log back into C# names (`just gsharp-demangle`) |
| `dotnet run tools/collect-env/collect-env.cs [--json] [--output <file>]` | Prints the bug-report environment (OS, GPU, CUDA, host C++, .NET, checkout) with no user name, host name, serial or profile path (`just env-info`). `/issue.prompt` Step 2 runs it for bug reports |

`tools/collect-env/` is a user-facing native tool (a .NET file-based app, published as a NativeAOT executable for people without the SDK or a clone), so the Node-only rule above does not apply to it. It must not reference the compiler projects.

Add a row when you add a script.
