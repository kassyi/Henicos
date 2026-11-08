# Test Execution Rules (`just`)

> [!IMPORTANT]
> **Always run tests via `just` (or set `DiffEngine_Disabled=true` / `CI=true`).**
> `Verify` snapshot failures launch interactive diff viewers (DiffEngine) by default, which blocks headless/agent runs indefinitely. `just` disables this automatically.

| Command | Scope |
| ----- | ----- |
| `just test` | All normal tests (excluding Stress) |
| `just test-category <Category> [args]` | One `TestCategory` enum value, e.g. `just test-category Stress` |
| `just test-differential-cpu` | Differential tests on Tests.Cpu: each kernel on the managed and native paths against the sequential C# oracle (`Category=Differential`, no GPU) |
| `just test-differential-cuda` | The same differential tests on Tests.Cuda against CUDA (uses the GPU; run through `gpu-lock.js`). Long runs: `just test-stability` (`scripts/human/test-stability.js`), whose `TEST_SEED` adds one input seed per iteration |
| `just fuzz-diff [profile] [minutes] *args` | Differential kernel fuzzer (`profile`: `M`/`managed`, `C`/`cpu` [default], or `G`/`all`; extra arguments such as `--allow-issue <n>` and `--swarm-focus <mode>` are passed to `fuzz-differential.js`; wraps GPU execution in `gpu-lock.js`) |
| `just coverage-diff [base]` | `just test` with coverage, then the lines changed since `base` (default `main`) that no test executed |
| `just mutate [gpumath\|compiler\|compiler-cuda] [since]` | Stryker.NET mutation test of the host code (`gpumath`: `Kassyi.GSharp.Stdlib.GpuMath`, `compiler`: `Kassyi.GSharp.Compiler` with `Tests.Cpu` minus the Differential tests that only run kernels generated at build time and so execute no compiler code (the ones that run the generator in process, `SpillOrderMutatingGetterTests` and `MathDifferentialTests`, stay), `compiler-cuda`: the files of the same compiler that branch on the CUDA dialect, with the `Tests.Cuda` snapshots; Tests.Cpu already kills most mutants elsewhere), optionally only the files changed after `since`. Runs for hours and uses the GPU (`gpumath`); report in `tmp/stryker/<phase>`. Stryker does not write the AOT artifacts under `obj/` (generator-invariants §7), so the build is usable after the run as it is. The test host heap is capped at `mutate_heap_limit` of the justfile, 8 GB (`GSharpTestHeapHardLimit`, in bytes; set it to override), so a mutant that allocates without bound fails its tests instead of exhausting memory |

The GPU is shared by every agent and worktree. Run any command that uses it (`just test-category Runtime`, a `dotnet test` of a GPU suite, a benchmark) as `node scripts/agent/gpu-lock.js -- "<command>"`; it waits for the lock `tmp/gpu-test-lock` and releases it when the command ends.

Test assemblies discard console output (`TestConsole.Silence()`); set `GSHARP_TEST_VERBOSE=1` to see `Logger` / benchmark output.

Pick the command matching the domain you modified. Methodology (TDD, snapshot hierarchy) lives in [test-architecture.md](test-architecture.md).

## Memory budgets

Every test assembly has a memory budget for its whole test host in `tests/memory-budgets.yml` (`<test project name>: <MB>`, one line per test project at column 0; `MemoryBudgetFileTests` fails when a test project has no line or a line names no test project). The watchdog of `Kassyi.GSharp.Tests.Infrastructure` (`MemoryBudget`) runs in every test host and samples the peak memory of the process every 250 ms and at the end of every test: the peak private memory (commit charge) on Windows, the peak resident memory (`VmHWM`) on Linux. Native allocations count.

- When the peak passes the budget, every test that was running at that moment fails with `MemoryBudgetExceededException`, the peak, the budget and the names of the running tests. One of them caused the growth; while tests run in parallel the memory of one test cannot be told apart from the others.
- `MemoryBudgetFinalCheck.WholeRunPeak_StaysWithinBudget`, compiled into every test assembly, runs after every other test (a collection with `DisableParallelization`, ordered last by `MemoryBudgetCollectionOrderer`) and fails when the peak of the run passed the budget, also between tests (fixtures, disposal). A run filtered to some categories skips it unless the filter keeps `Category=Unit`.
- `tests/Directory.Build.props` caps the managed heap at twice the budget (`System.GC.HeapHardLimit`), so growth faster than the sampling still cannot take the machine down. `GSharpTestHeapHardLimit` set from outside (`just mutate`) takes precedence.
- A breach is a finding, not a number to raise: find the test that grew (start from the running tests in the message) and fix the growth. Raise a budget only when the growth is intended, and say why in the PR.

To measure and update the budgets: set `GSHARP_TEST_MEMORY_REPORT=<dir>` (under `tmp/`), and `GSHARP_TEST_MEMORY_BUDGET_MB=1000000` so that no breach stops the run; each test host then appends `<peak MB>\t<GC heap peak MB>\t<budget MB>\t<tests run>\t<metric>` to `<dir>/<assembly>.tsv` when its final check runs (a run filtered so that the final check is skipped writes nothing). Run the suite three times each: on Windows as `just test` (Debug, GPU tests included) and as CI runs it (`-c Release --filter "Category!=GpuExecution"`, which includes the Stress and Benchmark tests), and on Linux (WSL) as CI runs it; take the largest peak per assembly, multiply by 1.5 and round up to a multiple of 64 MB. `GSHARP_TEST_MEMORY_BUDGET_MB` alone overrides the budget of every assembly of the run.

## CI (GitHub Actions)

`.github/workflows/gpu-tests.yml` ("Hybrid CI") runs the tests without CUDA on GitHub-hosted runners. The repository is public, so these runners have no minute limit.

- **Ubuntu** and **Windows** run side by side for every pull request (every base branch, so a stacked PR is tested too) and every push to `main`. A failure on one OS does not cancel the other.
- Each OS runs three jobs in parallel, each building only what it tests: `Tests.Cpu`, `Tests.Cuda` and `Other tests` (every other project of the solution, including the Integration tests). Every job uploads its `.trx` files as `test-results-<os>-<suite>` (kept 7 days); `node scripts/human/ci-test-durations.js <dir>` lists the slowest tests, classes and collections from them.
- A new run for the same pull request cancels the one still running. A change to `docs/**`, `.agents/**` or Markdown files alone runs nothing.

What an agent does:

- An agent reviews every pull request and posts the review as one comment ([pr-review-pairing](../skills/pr-review-pairing/SKILL.md)). The review starts with `git merge-tree --write-tree origin/<base> origin/<head>` and `gh pr checks <n>`; a conflict or a failing check is a blocking item. After posting it, the agent adds the `agent-reviewed` label.
- The user samples pull requests and adds the `human-reviewed` label to the ones they reviewed themselves. A pull request whose description marks a breaking change, or whose review raises a question of the semantics charter, waits for the user before it is merged.
- When a pull request carries either label, read every review comment (`gh pr view <n> --comments` and `gh api repos/<owner>/<repo>/pulls/<n>/comments`), reply on the pull request with a decision per item (fixed here, or filed as an issue with its milestone), make the fixes, and push them.
- A pull request is ready to merge when it carries `agent-reviewed` or `human-reviewed`, every review item is fixed or filed, and both its Ubuntu and Windows runs are green. The agent then merges it.
- When you hand the user a merge order, list only pull requests whose runs are green on both OS, or say which ones still wait for them.
- Do not close and reopen a pull request to start CI again; it does not always pick up a newer `main`. To test against a newer `main`, push one merge of `main` into the branch. Re-running from the Actions page reuses the old merge.
- If the run on a push to `main` fails, fix `main` first.
