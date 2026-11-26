// The command line of the job queue: parses the arguments and runs the mode. The entry script
// (scripts/human/job-queue.js) checks the Node version and loads this module.

import * as path from "node:path";
import {
  DefaultPolicy,
  DefaultPort,
  MemoryVariable,
  Policies,
  PolicyVariable,
  PortVariable,
  Shells,
  SlotsVariable,
  CommandVariable,
  UsageExitCode,
  isPolicy,
  isShellName,
  type Policy,
  type ShellName,
} from "./constants.ts";
import { parseJobRef, type JobRef } from "./job-id.ts";
import { run, usageError, type RunOptions } from "./client.ts";
import {
  accuracy,
  cancel,
  pause,
  place,
  resolveJobRef,
  resume,
  startNow,
  status,
  stop,
  wait,
} from "./client-commands.ts";
import { gpuSlotsProblem, loadConfig } from "./config.ts";
import { MachineProfile } from "./machine.ts";
import { PluginHost } from "./plugin-host.ts";
import { remoteReset } from "./remote/control.ts";
import { remoteSpecsFromConfig } from "./remote/spec.ts";
import { CommandShell } from "./shell.ts";
import { prefix, resolveStateDirectory, resolveWorktree } from "./system.ts";
import { watch } from "./watch.ts";
import { serve } from "./server.ts";

const Usage = [
  "Usage:",
  "  node scripts/human/job-queue.js run [--weight <n>] [--gpu | --gpu-exclusive] [--local | --remote]",
  "                                       [--after <id> [--keep-if-failed]] [--shell <name>] [--timeout-minutes <n>]",
  '                                       -- "<command>"',
  "  node scripts/human/job-queue.js status [<id>] [--here] [--json | --watch | --accuracy]",
  "  node scripts/human/job-queue.js wait <id> [--json]",
  "  node scripts/human/job-queue.js cancel <id>",
  "  node scripts/human/job-queue.js cancel-all",
  "  node scripts/human/job-queue.js move <id> (--first | --before <id> | --reset)",
  "  node scripts/human/job-queue.js set-weight <id> <n>",
  "  node scripts/human/job-queue.js start <id>",
  "  node scripts/human/job-queue.js pause [--until-idle]",
  "  node scripts/human/job-queue.js resume",
  "  node scripts/human/job-queue.js stop",
  "  node scripts/human/job-queue.js remote-reset [<name>] [--clean]",
  "  node scripts/human/job-queue.js serve [--slots <n>] [--memory-mb <n>] [--policy hrrn|backfill|fifo]",
  "                                        [--gpu-slots <n>] [--config <path>]",
  "  node scripts/human/job-queue.js --self-test",
  "  node scripts/human/job-queue.js --screenshots <dir>",
  "",
  "An <id> is the full number, or the last four digits as the queue shows them: 8739, j8739 or 18739 (the digits name",
  "the running or waiting job that ends so, else the newest that ended; two running or waiting jobs that do, and the",
  "scheduler asks for the full number).",
  "",
  "run         Waits for a slot of the job queue, runs the command in this directory, gives the slot back.",
  "            --weight <n>           Slots the job takes (default 1; a test run also takes its memory budget).",
  "            --gpu                  Wait for the turn at the GPU (one job at a time, in order), then take the GPU",
  "                                   lock (gpu-lock.js); the command's slots are then granted at once. The",
  "                                   plugins decide whether the run may share the GPU with other shared runs.",
  "            --gpu-exclusive        --gpu, and the run uses the GPU alone, whatever the plugins decide.",
  "            --local                Run on this machine; disable transparent remote CPU offload.",
  "            --remote               Require execution on a configured remote CPU worker (fails if ineligible).",
  "            --after <id>           Start only after job <id> has ended. When it failed, was cancelled or its",
  "                                   client was lost, this job is cancelled too (exit 4), unless --keep-if-failed.",
  "            --shell <name>         bash, pwsh, powershell, cmd or sh (default: the caller's: Git Bash when MSYSTEM",
  "                                   or SHELL is set, else PowerShell on Windows, /bin/sh elsewhere).",
  "            --timeout-minutes <n>  Give up waiting after n minutes and exit with 3 (default 0 = wait forever); with",
  "                                   --gpu, once for the turn at the GPU and once more for the GPU lock.",
  "status      Lists the running and waiting jobs with their ids, the jobs ahead and the expected start and end;",
  "            with <id>, only that job (also ended remote jobs with quarantined artifacts or redirected",
  "            --results-directory; exit 1 when ended without quarantine or unknown); --json prints the same as JSON.",
  "            --here lists only the jobs of the worktree of the current directory (with --json too).",
  "            --accuracy prints how close the expected run times were, per source (own runs, similar commands).",
  "            --watch shows the queue live in the terminal, one line per job with its timeline: arrows select a job,",
  "            c cancels it, s starts, m moves and w re-weights a waiting one, Space pauses or resumes the queue,",
  "            ? lists the keys, q quits (just queue-watch). Below the jobs, the agent chats that wait for you",
  "            (chat-status.js); a hides them.",
  "wait        Waits until job <id> has ended (also across a scheduler restart) and exits with its outcome: 0",
  "            succeeded, 1 failed, 4 cancelled, 5 lost (its client died; the result is unknown), 2 for an unknown",
  "            id. Run it in the background to be told when a job ends; --json prints {id, outcome}.",
  "cancel      Cancels one job: a waiting job leaves the queue, a running job is stopped. Its run exits with 4",
  "            (0 when its command had already succeeded). A GPU holder is cancelled with the --gpu run it serves.",
  "cancel-all  Cancels every job and empties the queue.",
  "move        Puts a waiting job ahead of the policy order: --first before every waiting job, --before <id> right",
  "            before another waiting job; --reset returns it to the policy order.",
  "set-weight  Changes the slots a waiting job takes.",
  "start       Starts a waiting job now, beside the running ones, over the slots and the memory pool (like a GPU",
  "            holder). Not for a job waiting for the GPU (one job at a time: move it --first) or for its --after job.",
  "pause       The scheduler starts no waiting job (no slots, no GPU turns) until resume; running jobs finish, and a",
  "            job queued meanwhile waits in its place. start <id> still works. The pause survives a scheduler",
  "            restart. --until-idle then waits until no job runs and exits 0 (pause --until-idle && stop).",
  "resume      Lifts the pause; the waiting jobs are granted as usual.",
  "stop        Stops the scheduler; running jobs keep running and register with the next one.",
  "remote-reset",
  "            Terminates stuck build/test processes on a remote worker (or all enabled remotes) over SSH and clears",
  "            manifest caches; with --clean, also wipes bin/, obj/, TestResults/, and tmp/ for a clean rebuild.",
  "serve       Runs the scheduler in the foreground (run starts it on demand). --slots: the CPU pool (default one",
  "            slot per 3 cores, at least 2, or config pools); --memory-mb: the memory pool for test runs (default",
  "            half the physical memory, or config pools); --gpu-slots: the shared GPU turns that run at a time",
  "            (default config resources.gpu.slots, 1; from 1 up to the seats of the GPU lock; a second shared",
  "            turn starts only after the probe of resources.gpu.probe has passed); --config: the configuration",
  "            file (default: JOB_QUEUE_CONFIG, else job-queue.config.json upward from the entry script).",
  "--screenshots",
  "            Draws the live view from a fixed sample status as SVG files into <dir> (wide.svg, small.svg,",
  "            selected.svg, help.svg; no scheduler). Regenerates the images of the README after a change to the view.",
  "",
  `Environment: ${PortVariable} (default ${DefaultPort}), ${SlotsVariable} (--slots), ${MemoryVariable}`,
  `(--memory-mb), ${PolicyVariable} (default ${DefaultPolicy}; the scheduler keeps the policy it started with).`,
  "Quote the command as one string when it needs pipes or redirection. Steps that belong together (a build and the",
  'test run of what it built) go into one job: run -- "dotnet build X && dotnet test X --no-build". Under --gpu,',
  "run builds a plain dotnet build / dotnet test command first as a job of its own, without the GPU turn.",
].join("\n");

const Modes = [
  "run",
  "status",
  "wait",
  "cancel",
  "cancel-all",
  "move",
  "set-weight",
  "start",
  "pause",
  "resume",
  "stop",
  "remote-reset",
  "serve",
] as const;
type Mode = (typeof Modes)[number] | "self-test" | "screenshots";

interface Options extends RunOptions {
  mode: Mode | "";
  stateDir: string;
  configPath: string;
  commandFromEnv: boolean;
  json: boolean;
  watch: boolean;
  /** status --here: only the jobs of the caller's worktree. */
  here: boolean;
  accuracy: boolean;
  untilIdle: boolean;
  /** serve --gpu-slots: the shared slots of the GPU resource; 0 for the configuration's. */
  gpuSlots: number;
  /** What was typed for the job a mode acts on (status, wait, cancel, move, set-weight, start); null for none. */
  jobRef: JobRef | null;
  /** The full id of jobRef: set by parseArgs for a full number, by main (resolveJobRef) for the last digits; 0 for none. */
  jobId: number;
  /** run --after: what was typed; `after` holds the full id once main has resolved it. */
  afterRef: JobRef | null;
  moveFirst: boolean;
  beforeRef: JobRef | null;
  moveBefore: number;
  moveReset: boolean;
  newWeight: number;
  /** --screenshots: the directory the SVG files go to. */
  screenshotDir: string;
  /** remote-reset [<name>]: optional remote worker name (empty = all enabled remotes). */
  remoteTarget: string;
  /** remote-reset --clean: wipe remote bin/, obj/, TestResults/, tmp/. */
  clean: boolean;
}

function parsePositive(name: string, text: string | undefined, allowZero = false): number {
  const value = Number(text);
  if (text === undefined || !Number.isInteger(value) || value < 0 || (!allowZero && value === 0)) {
    usageError(`${name} needs a ${allowZero ? "non-negative" : "positive"} integer.`);
  }
  return value;
}

/** A job id typed as j8739, 8739 or the full number; the last digits are resolved later (resolveJobRef). */
function parseRef(name: string, text: string | undefined): JobRef {
  const ref = text === undefined ? null : parseJobRef(text);
  if (ref === null) {
    usageError(`${name} needs a job id: the full number, or its last four digits as in j8739.`);
  }
  return ref;
}

function isMode(text: string): text is (typeof Modes)[number] {
  return (Modes as readonly string[]).includes(text);
}

export function parseArgs(argv: string[]): Options {
  const separator = argv.indexOf("--");
  const own = separator < 0 ? argv : argv.slice(0, separator);
  const command = separator < 0 ? [] : argv.slice(separator + 1);
  const policy = process.env[PolicyVariable];
  const options: Options = {
    mode: "",
    weight: 0,
    gpu: false,
    timeoutMinutes: 0,
    slots: Number(process.env[SlotsVariable]) || 0,
    memoryMb: Number(process.env[MemoryVariable]) || 0,
    policy: isPolicy(policy) ? policy : DefaultPolicy,
    port: Number(process.env[PortVariable]) || DefaultPort,
    commandFromEnv: false,
    shell: "",
    stateDir: "",
    configPath: "",
    gpuSlots: 0,
    json: false,
    watch: false,
    here: false,
    accuracy: false,
    untilIdle: false,
    jobRef: null,
    jobId: 0,
    after: 0,
    afterRef: null,
    keepIfFailed: false,
    moveFirst: false,
    beforeRef: null,
    moveBefore: 0,
    moveReset: false,
    newWeight: 0,
    screenshotDir: "",
    remoteTarget: "",
    clean: false,
    command: command.join(" "),
    // Several words after `--` are argv that the calling shell has already split: re-quote them for the shell that
    // runs them (see run), instead of joining them into a different command.
    commandWords: command.length > 1 ? command : null,
  };
  for (let i = 0; i < own.length; i++) {
    const arg = own[i] as string;
    if (isMode(arg)) {
      if (options.mode !== "") {
        usageError(`Unexpected ${arg}.`);
      }
      options.mode = arg;
      if (arg === "wait" || arg === "cancel" || arg === "move" || arg === "set-weight" || arg === "start") {
        options.jobRef = parseRef(arg, own[++i]);
        options.jobId = options.jobRef.kind === "exact" ? options.jobRef.id : 0;
      }
      if (arg === "set-weight") {
        options.newWeight = parsePositive("set-weight <id>", own[++i]);
      }
      continue;
    }
    if (options.mode === "status" && options.jobRef === null && parseJobRef(arg) !== null) {
      options.jobRef = parseRef("status", arg); // status <id>, before or after the options
      options.jobId = options.jobRef.kind === "exact" ? options.jobRef.id : 0;
      continue;
    }
    if (options.mode === "remote-reset" && options.remoteTarget === "" && !arg.startsWith("-")) {
      options.remoteTarget = arg;
      continue;
    }
    switch (arg) {
      case "--help":
      case "-h":
        process.stdout.write(`${Usage}\n`);
        process.exit(0);
        break;
      case "--self-test":
        options.mode = "self-test";
        break;
      case "--screenshots":
        options.mode = "screenshots";
        options.screenshotDir = own[++i] ?? "";
        if (options.screenshotDir === "") {
          usageError("--screenshots needs a directory.");
        }
        break;
      case "--weight":
        options.weight = parsePositive("--weight", own[++i]);
        break;
      case "--gpu":
        options.gpu = true;
        break;
      case "--gpu-exclusive":
        options.gpu = true;
        options.gpuExclusive = true;
        break;
      case "--local":
        options.local = true;
        break;
      case "--remote":
        options.remote = true;
        break;
      case "--clean":
        options.clean = true;
        break;
      case "--after":
        options.afterRef = parseRef("--after", own[++i]);
        options.after = options.afterRef.kind === "exact" ? options.afterRef.id : 0;
        break;
      case "--keep-if-failed":
        options.keepIfFailed = true;
        break;
      case "--first":
        options.moveFirst = true;
        break;
      case "--before":
        options.beforeRef = parseRef("--before", own[++i]);
        options.moveBefore = options.beforeRef.kind === "exact" ? options.beforeRef.id : 0;
        break;
      case "--reset":
        options.moveReset = true;
        break;
      case "--json":
        options.json = true;
        break;
      case "--watch":
        options.watch = true;
        break;
      case "--here":
        options.here = true;
        break;
      case "--accuracy":
        options.accuracy = true;
        break;
      case "--until-idle":
        options.untilIdle = true;
        break;
      case "--timeout-minutes":
        options.timeoutMinutes = parsePositive("--timeout-minutes", own[++i], true);
        break;
      case "--slots":
        options.slots = parsePositive("--slots", own[++i]);
        break;
      case "--memory-mb":
        options.memoryMb = parsePositive("--memory-mb", own[++i]);
        break;
      case "--gpu-slots":
        options.gpuSlots = parsePositive("--gpu-slots", own[++i]);
        break;
      case "--policy": {
        const value = own[++i];
        if (!isPolicy(value)) {
          usageError(`--policy needs ${Policies.join(", ")}.`);
        }
        options.policy = value as Policy;
        break;
      }
      case "--port": // for the self-test
        options.port = parsePositive("--port", own[++i]);
        break;
      case "--state-dir": // serve: a scheduler started through WMI does not inherit GSHARP_JOB_QUEUE_STATE_DIR
        options.stateDir = own[++i] ?? "";
        break;
      case "--config":
        options.configPath = own[++i] ?? "";
        if (options.configPath === "") {
          usageError("--config needs a path.");
        }
        break;
      case "--command-from-env": // set by the GPU wrapper, which cannot quote the command for the shell
        options.commandFromEnv = true;
        break;
      case "--shell": {
        const value = own[++i] ?? "";
        if (!isShellName(value)) {
          usageError(`--shell needs ${Shells.join(", ")}.`);
        }
        options.shell = value as ShellName;
        break;
      }
      default:
        usageError(`Unknown argument: ${arg}`);
    }
  }
  if (options.mode === "") {
    usageError(`Give a mode: ${Modes.join(", ")}.`);
  }
  if (
    options.mode === "move" &&
    Number(options.moveFirst) + Number(options.beforeRef !== null) + Number(options.moveReset) !== 1
  ) {
    usageError("move needs one of --first, --before <id> or --reset.");
  }
  if (options.watch && (options.mode !== "status" || options.json || options.jobRef !== null)) {
    usageError("--watch goes with status alone (no <id>, no --json).");
  }
  if (options.here && (options.mode !== "status" || options.watch || options.accuracy || options.jobRef !== null)) {
    usageError("--here goes with status alone (no <id>, no --watch, no --accuracy).");
  }
  if (options.accuracy && (options.mode !== "status" || options.watch || options.jobRef !== null)) {
    usageError("--accuracy goes with status alone (no <id>, no --watch).");
  }
  if (options.untilIdle && options.mode !== "pause") {
    usageError("--until-idle goes with pause alone.");
  }
  if (options.clean && options.mode !== "remote-reset") {
    usageError("--clean goes with remote-reset alone.");
  }
  if (options.local && options.remote) {
    usageError("--local and --remote cannot be used together.");
  }
  if (options.remote && options.gpu) {
    usageError("--remote cannot be used with --gpu or --gpu-exclusive.");
  }
  if (options.keepIfFailed && options.afterRef === null) {
    usageError("--keep-if-failed needs --after <id>.");
  }
  if (options.commandFromEnv) {
    options.command = process.env[CommandVariable] ?? "";
  }
  if (options.mode === "run" && options.command.trim() === "") {
    usageError('No command given. Put it after "--".');
  }
  return options;
}

export async function main(argv: string[]): Promise<void> {
  const options = parseArgs(argv);
  let plugins: PluginHost | undefined;
  if (options.mode !== "" && options.mode !== "serve" && options.mode !== "self-test") {
    const loaded = loadConfig({
      mode: options.mode,
      configPath: options.configPath || undefined,
      stateDir: options.stateDir || resolveStateDirectory(),
      warn: prefix,
    });
    if (loaded.path !== null) {
      options.configPath = loaded.path;
    } else if (options.configPath !== "") {
      options.configPath = path.resolve(options.configPath);
    }
    const machine = new MachineProfile(undefined, undefined, loaded.config.pools);
    if (options.slots === 0) {
      options.slots = machine.slots;
    }
    if (options.memoryMb === 0) {
      options.memoryMb = machine.memoryPoolMb;
    }
    options.defaultTestMemoryMb = loaded.config.pools?.defaultTestMemoryMb ?? 0;
    options.remotes = remoteSpecsFromConfig(loaded.config);
    if (
      options.mode === "run" ||
      (options.mode === "status" && options.watch) ||
      options.mode === "screenshots" ||
      options.mode === "remote-reset"
    ) {
      plugins = await PluginHost.load(loaded.config, loaded.configRoot);
    }
  }
  if (options.mode !== "serve" && options.gpuSlots > 0) {
    usageError("--gpu-slots goes with serve alone.");
  }
  if (options.mode === "serve" && options.gpuSlots > 0) {
    const configured = loadConfig({ mode: "serve", configPath: options.configPath || undefined, warn: prefix });
    const problem = gpuSlotsProblem(options.gpuSlots, configured.config);
    if (problem !== null) {
      usageError(problem);
    }
  }
  try {
    // The last digits of an id are resolved by the scheduler, which knows the jobs; wait ends 2 for an unknown job
    const failCode = options.mode === "wait" ? UsageExitCode : 1;
    // wait and run start the scheduler when none listens, as they do for a full number
    const start = options.mode === "wait" || options.mode === "run";
    if (options.jobRef !== null) {
      options.jobId = await resolveJobRef(options, options.jobRef, failCode, start);
    }
    if (options.beforeRef !== null) {
      options.moveBefore = await resolveJobRef(options, options.beforeRef, failCode);
    }
    if (options.afterRef !== null) {
      options.after = await resolveJobRef(options, options.afterRef, failCode, start);
    }
    switch (options.mode) {
      case "run":
        if (plugins === undefined) {
          throw new Error("run: the plugins were not loaded");
        }
        await run(options, plugins);
        break;
      case "status":
        await (options.accuracy
          ? accuracy(options.json)
          : options.watch
            ? watch(options.port, plugins)
            : status(options.port, options.jobId, options.json, options.here ? resolveWorktree(process.cwd()) : null));
        break;
      case "wait":
        await wait(options, options.jobId, options.json);
        break;
      case "cancel":
        await cancel(options.port, options.jobId);
        break;
      case "cancel-all":
        await cancel(options.port, "all");
        break;
      case "move":
        await place(
          options.port,
          options.moveFirst
            ? { type: "move", id: options.jobId, first: true }
            : options.moveReset
              ? { type: "move", id: options.jobId, reset: true }
              : { type: "move", id: options.jobId, before: options.moveBefore },
        );
        break;
      case "set-weight":
        await place(options.port, { type: "set-weight", id: options.jobId, weight: options.newWeight });
        break;
      case "start":
        await startNow(options.port, options.jobId);
        break;
      case "pause":
        await pause(options.port, options.untilIdle);
        break;
      case "resume":
        await resume(options.port);
        break;
      case "stop":
        await stop(options.port);
        break;
      case "remote-reset": {
        const cwd = process.cwd();
        const remoteRules = plugins?.remoteOffload({
          cwd,
          worktree: cwd,
          configRoot: plugins.configRoot,
          shell: CommandShell.resolve(options.shell).name,
          env: process.env,
          log: prefix,
        });
        const code = await remoteReset(
          options.remotes ?? [],
          options.remoteTarget,
          options.clean,
          options.stateDir || resolveStateDirectory(),
          remoteRules,
        );
        if (code !== 0) {
          process.exitCode = code;
        }
        break;
      }
      case "serve":
        serve(options);
        break;
      case "self-test": {
        const { selfTest } = await import("./self-test.ts");
        await selfTest();
        break;
      }
      case "screenshots": {
        const { writeScreenshots } = await import("./screenshot.ts");
        for (const file of writeScreenshots(options.screenshotDir, plugins)) {
          process.stdout.write(`${file}
`);
        }
        break;
      }
      case "":
        break;
    }
  } catch (error) {
    process.stderr.write(`[job-queue] ${error instanceof Error ? (error.stack ?? error.message) : String(error)}\n`);
    process.exit(1);
  }
}
