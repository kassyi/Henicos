// Constants and environment variable names of the job queue, shared by the scheduler, the server and the client.

import * as path from "node:path";

/** The entry script (scripts/human/job-queue.js): the scheduler and the GPU wrapper start it again. */
export const EntryScript = path.join(import.meta.dirname, "..", "job-queue.js");
/** The GPU lock, which the --gpu path takes before it asks for its CPU slot. */
export const GpuLockScript = path.join(import.meta.dirname, "..", "gpu-lock.js");

/** Version 3 adds placement (move, set-weight), dependencies (--after, await) and the exit code in "done". */
export const ProtocolVersion = 3;
/** Version 2 clients (checkouts from before version 3) send a subset of version 3; the scheduler serves them too. */
export const BaseProtocolVersion = 2;
export const AcceptedProtocolVersions: readonly number[] = [BaseProtocolVersion, ProtocolVersion];

export const DefaultPort = 47117;

export const IdleExitMs = 30 * 60 * 1000;
export const NoticeIntervalMs = 5 * 60 * 1000;
export const StartupGraceMs = 1500;
export const OrphanPollMs = 2000;
export const LogRotateBytes = 5 * 1024 * 1024;
export const CancelledExitCode = 4;
export const UsageExitCode = 2;
export const TimedOutExitCode = 3;
/** wait: the job's client died and its command's result is unknown. */
export const LostExitCode = 5;

export const Policies = ["fifo", "backfill", "hrrn"] as const;
export type Policy = (typeof Policies)[number];
export const DefaultPolicy: Policy = "hrrn";

// the typical run time of a command with no recorded run, for its response ratio
export const UnknownTypicalMs = 60 * 1000;
export const HistoryRunsPerCommand = 9;
export const HistoryCommands = 500;
export const RegrantMs = 5 * 1000; // how often the scheduler re-orders and grants while jobs wait
export const ForecastNoticeMs = 60 * 1000; // a waiting client is told again when its expected start moved this much
// forecast: the rest of a running job that has run longer than any recorded run
export const OverrunGuessMs = 30 * 1000;
export const LongestRunMs = 7 * 24 * 60 * 60 * 1000; // run times and starts beyond this are taken as damaged input
export const ForecastSteps = 2000; // the forecast replays at most this many steps; jobs it does not reach get no times
export const KeptOutcomes = 500; // the outcomes of ended jobs kept for --after (also across a restart)
/** How long a restarted scheduler waits for a job of the earlier one to come back before it counts as lost. */
export const ExpectedJobsMs = 60 * 1000;

export const JobVariable = "GSHARP_JOB_QUEUE_JOB";
export const InGpuLockVariable = "GSHARP_JOB_QUEUE_IN_GPU_LOCK";
/** Set by gpu-lock.js for the command it runs (the lock directory): that command already holds the GPU lock. */
export const GpuLockHeldVariable = "GSHARP_GPU_LOCK_HELD";
export const CommandVariable = "GSHARP_JOB_QUEUE_COMMAND";
export const MsbuildArgsVariable = "GSHARP_JOB_QUEUE_MSBUILD_ARGS";
export const CpusVariable = "GSHARP_JOB_QUEUE_CPUS";
export const StateDirectoryVariable = "GSHARP_JOB_QUEUE_STATE_DIR";
export const ShellVariable = "GSHARP_JOB_QUEUE_SHELL";
export const PolicyVariable = "GSHARP_JOB_QUEUE_POLICY";
export const PortVariable = "GSHARP_JOB_QUEUE_PORT";
export const SlotsVariable = "GSHARP_JOB_QUEUE_SLOTS";
export const MemoryVariable = "GSHARP_JOB_QUEUE_MEMORY_MB";

export const Shells = ["bash", "pwsh", "powershell", "cmd", "sh"] as const;
export type ShellName = (typeof Shells)[number];

export function isShellName(text: string): text is ShellName {
  return (Shells as readonly string[]).includes(text);
}

export function isPolicy(text: string | undefined): text is Policy {
  return text !== undefined && (Policies as readonly string[]).includes(text);
}
