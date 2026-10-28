# Test Execution Rules (`just`)

> [!IMPORTANT]
> **Always run tests via `just` (or set `DiffEngine_Disabled=true` / `CI=true`).**
> `Verify` snapshot failures launch interactive diff viewers (DiffEngine) by default, which blocks headless/agent runs indefinitely. `just` disables this automatically.

| Command | Scope |
| ----- | ----- |
| `just test` | All normal tests (excluding Stress) |
| `just test-syntax` | Syntax parsing / C# transpilation (`Category=Syntax`) |
| `just test-execution` | Real GPU kernel execution (`Category=Execution`) |
| `just test-sadpath` | Diagnostics / error detection (`Category=SadPath\|Diagnostics`) |
| `just test-stress` | High-load / concurrency (`Category=Stress`) |
| `just test-runtime` | JIT engine / transpiler infrastructure (`Category=Runtime\|Transpiler`) |
| `just test-unit` | General unit tests (`Category=Unit`) |
| `just test-alignment` | Memory alignment / struct layout (`Category=Alignment`) |
| `just test-all` | Everything |
| `just coverage-diff [base]` | `just test` with coverage, then the lines changed since `base` (default `main`) that no test executed |
| `just mutate [gpumath\|compiler\|compiler-cuda] [since]` | Stryker.NET mutation test of the host code (`gpumath`: `Kassyi.GSharp.Stdlib.GpuMath`, `compiler`: `Kassyi.GSharp.Compiler` with `Tests.Cpu`, `compiler-cuda`: the same compiler with the `Tests.Cuda` snapshots, whose build takes about an hour), optionally only the files changed after `since`. Runs for hours and uses the GPU (`gpumath`); report in `tmp/stryker/<phase>`. Stryker does not write the AOT artifacts under `obj/` (generator-invariants §7), so the build is usable after the run as it is |

The GPU is shared by every agent and worktree. Run any command that uses it (`just test-execution`, a `dotnet test` of a GPU suite, a benchmark) as `node scripts/agent/gpu-lock.js -- "<command>"`; it waits for the lock `tmp/gpu-test-lock` and releases it when the command ends.

Test assemblies discard console output (`TestConsole.Silence()`); set `GSHARP_TEST_VERBOSE=1` to see `Logger` / benchmark output.

Pick the command matching the domain you modified. Methodology (TDD, snapshot hierarchy) lives in [test-architecture.md](test-architecture.md).

## CI (GitHub Actions)

`.github/workflows/gpu-tests.yml` ("Hybrid CI") runs the tests without CUDA on GitHub-hosted runners. The repository is public, so these runners have no minute limit.

- **Ubuntu** and **Windows** run side by side for every pull request (every base branch, so a stacked PR is tested too) and every push to `main`. A failure on one OS does not cancel the other.
- A new run for the same pull request cancels the one still running. A change to `docs/**`, `.agents/**` or Markdown files alone runs nothing.

What an agent does:

- The user adds the `human-reviewed` label when their review of a pull request is finished. Then read every review comment (`gh pr view <n> --comments` and `gh api repos/<owner>/<repo>/pulls/<n>/comments`), reply on the pull request with a decision per item (fixed here, or filed as an issue with its milestone), make the fixes, and push them.
- A pull request is ready to merge when it is labeled `human-reviewed`, every review item is fixed or filed, and both its Ubuntu and Windows runs are green.
- When you hand the user a merge order, list only pull requests whose runs are green on both OS, or say which ones still wait for them.
- Do not close and reopen a pull request to start CI again; it does not always pick up a newer `main`. To test against a newer `main`, push one merge of `main` into the branch. Re-running from the Actions page reuses the old merge.
- If the run on a push to `main` fails, fix `main` first.
