---
trigger: always_on
description: Every build and test command runs through the job queue (scripts/human/job-queue.js); the short form.
---

# Job queue

The machine and its GPU are shared by every agent and worktree. Run every build and test command (`dotnet build`,
`test`, `msbuild`, `publish`, `pack`, `vstest`, Stryker, the `just` test, fuzz, mutate and benchmark recipes, the long
test scripts) through the queue, never directly:

```sh
node scripts/human/job-queue.js run -- "<command>"          # CPU only
node scripts/human/job-queue.js run --gpu -- "<command>"    # the command uses the GPU
node scripts/human/job-queue.js wait <id>                   # until it ends: exit 0 ok, 1 failed, 4 cancelled, 5 lost
node scripts/human/job-queue.js status [<id>]               # the jobs ahead and the expected start and end
```

- `--gpu` only for a command that uses the GPU (a CUDA test project, `just test-category Runtime`, a benchmark): a CPU
  suite under `--gpu` keeps the GPU from the jobs that need it. `just fuzz-diff` and `fuzz-differential.js` take the
  GPU lock themselves: queue them without `--gpu`. `just long-checks` (`scripts/human/long-checks.js`) is a slot-free
  orchestrator that queues each step through `job-queue.js` itself: run it directly without wrapping it in `run`.
- Whether a `--gpu` run shares the GPU or uses it alone is decided for it: only a test run whose filter excludes
  Stress, Benchmark and GpuTrap (as `just test` does) may share; a filter that names tests alone uses the GPU alone.
  `--gpu-exclusive` asks for the GPU alone. Queue non-test timing measurements (scripts, direct `.exe` launches,
  env-prefixed runs) with `--gpu-exclusive` so they never share the GPU with another job.
- A build and the tests of what it built go into one job, as one quoted string:
  `run -- "dotnet build X && dotnet test X --no-build"`. Under `--gpu`, `run` puts the build into a CPU job of its own.
- A test run outside `just` sets `DiffEngine_Disabled=true` (`run -- "DiffEngine_Disabled=true dotnet test X"`): a
  failing Verify snapshot otherwise opens a diff viewer and the run waits for it forever. The `just` recipes set it.
- Write the command for the shell you call `run` from (Git Bash for the Claude Code Bash tool, PowerShell otherwise on
  Windows).
- An `<id>` is the full number or the last four digits as `status` shows them (`j8739`, `8739`).
- Transparent remote CPU offload (configured per-machine in `<mainCheckout>/tmp/job-queue/remotes.local.json`; sample
  in `scripts/human/job-queue/remotes.local.example.json`): self-contained CPU test runs are delta-synced (SHA-256
  verified before each run) and executed over hardened SSH on a free healthy worker (0 local CPU/RAM), spilling over or
  falling back to local when busy or offline. Remote run times are tracked separately (`history-remote-<name>.json`).
  Pass `--local` (or `GSHARP_JOB_QUEUE_OFFLOAD=0`) to stay local, `--remote` to require remote execution, or
  `remote-reset [<name>] [--clean]` to recover a stuck worker.
- A wait can be long: run `run` or `wait <id>` in the background, and the tool calls you when it ends.
- A PreToolUse hook (`check-queued-command.js`) denies a build or test command that does not go through the queue; in
  Claude Code it rewrites a plain one (`dotnet ...`, `just ...` joined by `&&`) into a queued one instead.

Read [job-queue-reference.md](job-queue-reference.md) before using anything else: `--after` dependencies, moving,
cancelling or pausing jobs, `--weight`, slots and memory budgets, the GPU turn, scheduler restarts, the hook's exact
rules.
