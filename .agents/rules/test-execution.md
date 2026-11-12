# Test Execution Rules (`just`)

> [!IMPORTANT]
> **Always run tests via `just` (or set `DiffEngine_Disabled=true` / `CI=true`).**
> `Verify` snapshot failures launch interactive diff viewers (DiffEngine) by default, which blocks headless/agent runs indefinitely. `just` disables this automatically.

| Command | Scope |
| ----- | ----- |
| `just test` | All normal tests (excluding Stress, Benchmark and GpuTrap) |
| `just test-category <Category> [args]` | One `TestCategory` enum value, e.g. `just test-category Stress` |
| `just test-differential-cpu` | Differential tests on Tests.Cpu: each kernel on the managed and native paths against the sequential C# oracle (`Category=Differential`, no GPU) |
| `just test-differential-cuda` | The same differential tests on Tests.Cuda against CUDA (uses the GPU; run through `gpu-lock.js`). Long runs: `just test-stability` (`scripts/human/test-stability.js`), whose `TEST_SEED` adds one input seed per iteration |
| `just test-gpu-trap [pause_ms] [args]` | Tests.Cuda tests that raise a real GPU fault (`Category=GpuTrap`: a CUDA `trap;` in a child process) and the CUDA cells of the differential fault seeds that are a known exception divergence on CUDA (a check CUDA does not make, such as a bounds or null check of a release build, so the launch may touch an illegal address), which every other run skips (#1457). A failing CUDA thread records its error and exits without a fault (sm_70 and later): a waiter at an mbarrier exits through `__gsharp_wait_check`, and the threads of a block that waits at the grid barrier leave it and exit. A thread still traps only when the launch has no error record, when other launches hold both its own record and the shared record, or before sm_70. Sets `GSHARP_CUDA_TRAP_TESTS=1`; the children run one at a time, `pause_ms` (default 3000) apart. Run through `gpu-lock.js`. On Windows each fault logs nvlddmkm Event 153, resets the GPU engine and writes a live kernel dump, and a series of them has frozen the machine, so run it only when a change touches the device error channel or the trap path |
| `just fuzz-diff [profile] [minutes] *args` | Differential kernel fuzzer (`profile`: `M`/`managed`, `C`/`cpu` [default], or `G`/`all`; extra arguments such as `--allow-issue <n>` and `--swarm-focus <mode>` (for example `warp-shuffle`, `barrier`, `iterator`, `suspending-call`, `split-points`) are passed to `fuzz-differential.js`; wraps GPU execution in `gpu-lock.js`) |
| `just test-memory <project> [args]` | One run of a test project with the memory report, `dotnet-counters` and a gcdump at the end; prints the peak, the tests that raised it and the types left on the heap (see Memory budgets) |
| `just coverage-diff [base]` | `just test` with coverage, then the lines changed since `base` (default `main`) that no test executed |
| `just mutate [gpumath\|compiler\|compiler-cuda] [since]` | Stryker.NET mutation test of the host code (`gpumath`: `Kassyi.GSharp.Stdlib.GpuMath`, `compiler`: `Kassyi.GSharp.Compiler` with `Tests.Cpu` minus the Differential tests that only run kernels generated at build time and so execute no compiler code (the ones that run the generator in process, `SpillOrderMutatingGetterTests` and `MathDifferentialTests`, stay), `compiler-cuda`: the files of the same compiler that branch on the CUDA dialect, with the `Tests.Cuda` snapshots; Tests.Cpu already kills most mutants elsewhere), optionally only the files changed after `since`. Runs for hours and uses the GPU (`gpumath`); report in `tmp/stryker/<phase>`. Stryker does not write the AOT artifacts under `obj/` (generator-invariants §7), so the build is usable after the run as it is. The test host heap is capped at `mutate_heap_limit` of the justfile, 8 GB (`GSharpTestHeapHardLimit`, in bytes; set it to override), so a mutant that allocates without bound fails its tests instead of exhausting memory |

The GPU is shared by every agent and worktree. Run any command that uses it (`just test-category Runtime`, a `dotnet test` of a GPU suite, a benchmark) as `node scripts/agent/gpu-lock.js -- "<command>"`; it waits for the lock `tmp/gpu-test-lock` and releases it when the command ends.

A test that raises a real GPU fault carries `[Category(TestCategory.GpuTrap)]`, runs its launch in a child process (`CudaTrapRuns.RunIsolated`, or `CudaTrapGate.RunPaced` around the start of the child) and skips unless `GSHARP_CUDA_TRAP_TESTS=1`, so `just test`, CI and a plain `dotnet test` raise no fault. A differential case keeps its fault seeds in the default run; a CUDA cell that is a known exception divergence is skipped, and the report lists those seeds. A test that expects a CUDA device error that does not trap (the other differential fault seeds included) runs in the test host, in `[Collection(TestCollections.DeviceExclusive)]`: the process has one device error slot, so the error of a test running beside another one could be thrown by the other test's synchronization.

Kernel pools after GPU faults on Windows: a plain CUDA program whose kernel ends in `trap;` grew the nonpaged pool by 474 MB and the paged pool by 1,098 MB over 200 runs (unevenly), and the pools were not freed when the processes exited, only by a reboot; over nine days of test runs on the same machine (about 10,600 Event 153 entries) the pools reached 8.3 GB nonpaged and 11.1 GB paged, and a reboot returned them to 1.8 GB and 2.1 GB. Whether the driver or the live kernel dump that Windows writes per fault holds the memory is open, and faults on an illegal address were not measured (NVIDIA bug 7077284, https://developer.nvidia.com/bugs/7077284 , needs an NVIDIA developer login). A plain CUDA trap logs one nvlddmkm Event 153 entry; a G# test process logs two per fault. `gpu-lock.js` reads the pools and the Event 153 entries while the command starts and before it releases the lock (`scripts/human/gpu-fault-watch.js`, about 0.1 s each), prints them with the entries of the run, appends them to `tmp/gpu-pool-log.tsv` of the main checkout, and warns when the nonpaged pool is above 6 GB before the run or the pools grew by more than 1 GB; `GSHARP_GPU_POOL_WATCH=0` turns it off. `just test-stability` and `just long-checks` write the same readings into their summaries. When the warning suggests a reboot, tell the user, and do not start another run that faults.

Test assemblies discard console output (`TestConsole.Silence()`); set `GSHARP_TEST_VERBOSE=1` to see `Logger` / benchmark output.

Pick the command matching the domain you modified. Methodology (TDD, snapshot hierarchy) lives in [test-architecture.md](test-architecture.md).

## Memory budgets

Every test assembly has a memory budget for its whole test host in `tests/memory-budgets.yml`. The watchdog of
`Kassyi.GSharp.Tests.Infrastructure` (`MemoryBudget`) runs in every test host and samples the peak memory of the process
every 250 ms and at the start and end of every test: the peak private memory (commit charge) on Windows, the peak
resident memory (`VmHWM`) on Linux. Native allocations count.

- **Two budgets per assembly**: `<test project name>: <MB>[, stress <MB>]`, one line per test project at column 0
  (`MemoryBudgetFileTests` fails when a test project has no line or a line names no test project). The first value
  holds for a run without Stress and Benchmark tests (`just test`); the `stress` value takes over when the first test
  with `[Category(TestCategory.Stress)]` or `[Category(TestCategory.Benchmark)]` starts (the CI filter
  `Category!=GpuExecution` runs them). The build configuration does not change the peaks measurably, so it does not
  select a budget.
- **Breach**: every test that was running when the peak passed the budget fails with `MemoryBudgetExceededException`.
  The message gives the peak, the budget and its profile, the running tests (one of them caused the growth), the five
  tests that raised the peak most (growth of the high-water mark between their start and end; approximate while tests
  run in parallel) and the memory snapshot taken at the breach.
- **Snapshot at the breach**: `dotnet-gcdump collect` of the test host plus its `dotnet-gcdump report` (types by size),
  run one-shot through `dotnet dnx` at a pinned version, into `tmp/test-memory/<assembly>-<time>-<pid>/`.
  `GSHARP_TEST_MEMORY_DUMP=full` writes a full dump (`dotnet-dump collect --type Full`) instead; `none` writes nothing.
  A tool that fails or does not finish in time is named in the message; the failing test waits at most about three
  minutes for it.
- **Final check**: `MemoryBudgetFinalCheck.WholeRunPeak_StaysWithinBudget`, compiled into every test assembly, runs
  after every other test (a `DisableParallelization` collection, ordered last by `MemoryBudgetCollectionOrderer`) and
  fails when the peak of the run passed the budget, also between tests (fixtures, disposal). A run filtered to some
  categories skips it unless the filter keeps `Category=Unit`.
- **Backstop**: `tests/Directory.Build.props` caps the managed heap at twice the larger budget
  (`System.GC.HeapHardLimit`). `GSharpTestHeapHardLimit` set from outside (`just mutate`) takes precedence. Under the
  limit, `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` is the limit, not the machine: a test that checks the
  machine's free memory uses `ProcessMemory.ReadAvailablePhysicalBytes()`.
- **Dead hosts**: the `just` test recipes and CI pass `--blame-crash --blame-crash-dump-type mini`, so a host that dies
  (for example at the heap limit) leaves a mini dump under its `TestResults` directory. CI uploads `tmp/test-memory/`
  and those dumps as the `memory-diagnostics-<os>-<suite>` artifact when a job fails.
- A breach is a finding, not a number to raise: find the test that grew and fix the growth. Raise a budget only when the
  growth is intended, and say why in the PR.

To look into one suite: `just test-memory <project> [-c <configuration>] [--filter <expr>]`
(`scripts/human/test-memory.js`) runs it once with the report on and no budget, samples the host with `dotnet-counters`,
takes a gcdump at the end of the run and prints the peak, the tests that raised it, the counter maxima and the types
left on the heap. Run GPU suites under `gpu-lock.js`.

To measure and update the budgets: set `GSHARP_TEST_MEMORY_REPORT=<dir>` (under `tmp/`) and
`GSHARP_TEST_MEMORY_BUDGET_MB=1000000` (no breach); each test host appends
`<peak MB>\t<GC heap peak MB>\t<budget MB>\t<tests run>\t<profile>\t<metric>` to `<dir>/<assembly>.tsv`, and its top
growth to `<assembly>.top.tsv`, when its final check runs. Run the suite three times each on Windows as `just test` and
as CI runs it (`-c Release --filter "Category!=GpuExecution"` with `GSHARP_TEST_NO_CUDA=1`), and on Linux (WSL) as CI
runs it. Per assembly, the first budget is 1.2 times the largest peak of the `default` profile lines and the `stress`
budget 1.2 times the largest peak of the `stress` lines, each rounded up to a multiple of 64 MB.
`GSHARP_TEST_MEMORY_BUDGET_MB` alone replaces both budgets of every assembly of the run.

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
