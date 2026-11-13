#!/usr/bin/env node
"use strict";

// Queues builds and test runs on this machine, so that agents and worktrees running side by side do not oversubscribe
// the CPU and memory. This entry checks the Node version and loads the TypeScript modules of job-queue/, which Node
// runs as they are (type stripping, Node 22.18 / 23.6 and later); `node scripts/human/check-types.js` type-checks them.
//
// A scheduler listens on 127.0.0.1 (port 47117, GSHARP_JOB_QUEUE_PORT) and grants slots of a CPU pool (one per 3
// cores, at least 2; GSHARP_JOB_QUEUE_SLOTS) and, to test runs, their budget of a memory pool (half the physical
// memory, GSHARP_JOB_QUEUE_MEMORY_MB; budgets from tests/memory-budgets.yml). `run` asks for a slot, waits, runs
// the command itself in the caller's directory (incremental builds keep working), and gives the slot back when the
// command ends. The scheduler starts on the first
// `run`, in <main checkout>/tmp/job-queue (so that it never holds a worktree open), and exits after 30 minutes
// without jobs.
//
// Order (--policy, GSHARP_JOB_QUEUE_POLICY): the scheduler records how long each command ran (history.json in the
// state directory, the last 9 runs per command text). With `hrrn` (the default) the waiting jobs are ordered by
// response ratio, (waited + typical run time) / typical run time: a short job goes first, and a long one rises the
// longer it waits, so that none starves. A later job may start in free slots when it does not delay the first job
// (backfilling): it ends before the first job could start, counting every running job at its longest recorded time,
// or it fits in the slots the first job leaves free. `backfill` keeps arrival order with backfilling; `fifo` keeps
// arrival order without it. `move <id> --first` / `--before <id>` put a waiting job ahead of that order (backfilling
// still reserves the first job's start); `set-weight` changes the slots a waiting job takes; `start <id>` grants a
// waiting job at once, over the slots and the memory pool, like a GPU holder (not a GPU turn, not before --after).
//
// Dependencies: `run --after <id>` starts once that job's recorded outcome lets it (succeeded, or any outcome with
// --keep-if-failed); when it failed, was cancelled or its client was lost, the job is cancelled too (exit 4). The
// scheduler keeps the outcomes of the last 500 jobs, also across a restart. `wait <id>` blocks until a job ends and
// exits with its outcome, so that an agent running it in the background is called when the job ends.
//
// Job ids: `run` prints `queued #<id>` as soon as the scheduler took the job, with the jobs ahead of it and its
// expected start and end; `status <id> [--json]` tells them again at any time. Ids count up and are never reused while
// the state directory lives (the next id is saved in jobs-<port>.json and history.json).
//
// Slots follow the command, not only the connection:
//   - The client reports the process id of the command it started. When the client dies but the command still runs
//     (a tool timeout that kills only the client), the scheduler keeps the slot until no process of the command is
//     left (on Windows: the shell or a child of it, matched by creation time, since the shell dies with the client).
//   - A cancelled running job keeps its slot until its client has stopped the command and closed.
//   - When the scheduler dies, a running client reconnects and re-registers its job, and a waiting client queues
//     again with its original arrival time, both with their job id, place and predecessor, so that nothing changes
//     across a restart. For the first seconds after it starts, the scheduler accepts those re-registrations and grants
//     nothing. Jobs whose client was already gone are restored from jobs-<port>.json.
//
// Weight: every job takes 1 slot unless --weight says more; a test run (dotnet test, just test*, the long test
// scripts) also takes the budget of the assemblies it names, or of all of them when it names none, from the memory
// pool, and waits while that does not fit (a run larger than the pool runs alone). A granted job gets
// cores / slots * weight MSBuild nodes: `-m:<n>` is added to each `dotnet build`, `test`, `msbuild`, `publish` and
// `pack` of the command that has no -m of its own, and GSHARP_JOB_QUEUE_MSBUILD_ARGS carries it to other callers.
// The justfile recipes that build or test queue their command through `run` themselves.
//
// GPU: --gpu first waits in the scheduler for its turn at the GPU (one at a time, in the same order as the slots,
// taking no slots), then takes the GPU lock (scripts/agent/gpu-lock.js), under which the command asks for its CPU
// slots and is granted at once, even over the capacity, so that a job that holds the GPU lock never waits for a slot.
//
// Shell: the command runs in the caller's shell: Git Bash on Windows when MSYSTEM or SHELL is set (the Claude Code Bash
// tool), PowerShell otherwise on Windows (the Claude Code PowerShell tool, Antigravity), /bin/sh elsewhere; --shell
// overrides it. A command run by a queued job (GSHARP_JOB_QUEUE_JOB is set) that calls `run` again runs at once,
// without a second slot, with the same MSBuild node limit.
//
// Usage: node scripts/human/job-queue.js --help
//
// Exit codes (run): the exit code of the command; 2 on a usage error or an unknown --after job; 3 when the wait timed
// out; 4 when the job was cancelled (also when its predecessor failed); 130 / 143 on SIGINT / SIGTERM.

const [major, minor] = process.versions.node.split(".").map(Number);
const stripsTypes = major > 23 || (major === 23 && minor >= 6) || (major === 22 && minor >= 18);
if (!stripsTypes || (process.features && process.features.typescript === false)) {
  process.stderr.write(
    `[job-queue] Node ${process.versions.node} cannot run TypeScript files as they are; ` +
      "install Node 22.18, 23.6 or later (scripts/human/Install-DevTools.ps1 on Windows), " +
      "without --no-experimental-strip-types.\n",
  );
  process.exit(1);
}

import("./job-queue/cli.ts").then((cli) => cli.main(process.argv.slice(2)));
