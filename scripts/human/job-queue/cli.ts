// The command line of the job queue: parses the arguments and runs the mode. The entry script
// (scripts/human/job-queue.js) checks the Node version and loads this module.

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
  isPolicy,
  isShellName,
  type Policy,
  type ShellName,
} from "./constants.ts";
import { run, usageError, type RunOptions } from "./client.ts";
import { cancel, place, status, stop, wait } from "./client-commands.ts";
import { MachineProfile } from "./machine.ts";
import { serve } from "./server.ts";

const Usage = [
  "Usage:",
  "  node scripts/human/job-queue.js run [--weight <n>] [--gpu] [--after <id> [--keep-if-failed]] [--shell <name>]",
  '                                       [--timeout-minutes <n>] -- "<command>"',
  "  node scripts/human/job-queue.js status [<id>] [--json]",
  "  node scripts/human/job-queue.js wait <id> [--json]",
  "  node scripts/human/job-queue.js cancel <id>",
  "  node scripts/human/job-queue.js cancel-all",
  "  node scripts/human/job-queue.js move <id> (--first | --before <id> | --reset)",
  "  node scripts/human/job-queue.js set-weight <id> <n>",
  "  node scripts/human/job-queue.js stop",
  "  node scripts/human/job-queue.js serve [--slots <n>] [--memory-mb <n>] [--policy hrrn|backfill|fifo]",
  "  node scripts/human/job-queue.js --self-test",
  "",
  "run         Waits for a slot of the job queue, runs the command in this directory, gives the slot back.",
  "            --weight <n>           Slots the job takes (default 1; a test run also takes its memory budget).",
  "            --gpu                  Wait for the turn at the GPU (one job at a time, in order), then take the GPU",
  "                                   lock (gpu-lock.js); the command's slots are then granted at once.",
  "            --after <id>           Start only after job <id> has ended. When it failed, was cancelled or its",
  "                                   client was lost, this job is cancelled too (exit 4), unless --keep-if-failed.",
  "            --shell <name>         bash, pwsh, powershell, cmd or sh (default: the caller's: Git Bash when MSYSTEM",
  "                                   or SHELL is set, else PowerShell on Windows, /bin/sh elsewhere).",
  "            --timeout-minutes <n>  Give up waiting after n minutes and exit with 3 (default 0 = wait forever); with",
  "                                   --gpu, once for the turn at the GPU and once more for the GPU lock.",
  "status      Lists the running and waiting jobs with their ids, the jobs ahead and the expected start and end;",
  "            with <id>, only that job (exit 1 when it ended or is unknown); --json prints the same as JSON.",
  "wait        Waits until job <id> has ended (also across a scheduler restart) and exits with its outcome: 0",
  "            succeeded, 1 failed, 4 cancelled, 5 lost (its client died; the result is unknown), 2 for an unknown",
  "            id. Run it in the background to be told when a job ends; --json prints {id, outcome}.",
  "cancel      Cancels one job: a waiting job leaves the queue, a running job is stopped. Its run exits with 4.",
  "cancel-all  Cancels every job and empties the queue.",
  "move        Puts a waiting job ahead of the policy order: --first before every waiting job, --before <id> right",
  "            before another waiting job; --reset returns it to the policy order.",
  "set-weight  Changes the slots a waiting job takes.",
  "stop        Stops the scheduler; running jobs keep running and register with the next one.",
  "serve       Runs the scheduler in the foreground (run starts it on demand). --slots: the CPU pool (default one",
  "            slot per 3 cores, at least 2); --memory-mb: the memory the budgets of the running test runs may add up",
  "            to (tests/memory-budgets.yml; default half the physical memory).",
  "",
  `Environment: ${PortVariable} (default ${DefaultPort}), ${SlotsVariable} (--slots), ${MemoryVariable}`,
  `(--memory-mb), ${PolicyVariable} (default ${DefaultPolicy}; the scheduler keeps the policy it started with).`,
  "Quote the command as one string when it needs pipes or redirection. Steps that belong together (a build and the",
  'test run of what it built) go into one job: run -- "dotnet build X && dotnet test X --no-build".',
].join("\n");

const Modes = ["run", "status", "wait", "cancel", "cancel-all", "move", "set-weight", "stop", "serve"] as const;
type Mode = (typeof Modes)[number] | "self-test";

interface Options extends RunOptions {
  mode: Mode | "";
  stateDir: string;
  commandFromEnv: boolean;
  json: boolean;
  /** The job a mode acts on (status, cancel, move, set-weight); 0 for none. */
  jobId: number;
  moveFirst: boolean;
  moveBefore: number;
  moveReset: boolean;
  newWeight: number;
}

function parsePositive(name: string, text: string | undefined, allowZero = false): number {
  const value = Number(text);
  if (text === undefined || !Number.isInteger(value) || value < 0 || (!allowZero && value === 0)) {
    usageError(`${name} needs a ${allowZero ? "non-negative" : "positive"} integer.`);
  }
  return value;
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
    slots: Number(process.env[SlotsVariable]) || new MachineProfile().slots,
    memoryMb: Number(process.env[MemoryVariable]) || new MachineProfile().memoryPoolMb,
    policy: isPolicy(policy) ? policy : DefaultPolicy,
    port: Number(process.env[PortVariable]) || DefaultPort,
    commandFromEnv: false,
    shell: "",
    stateDir: "",
    json: false,
    jobId: 0,
    after: 0,
    keepIfFailed: false,
    moveFirst: false,
    moveBefore: 0,
    moveReset: false,
    newWeight: 0,
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
      if (arg === "wait" || arg === "cancel" || arg === "move" || arg === "set-weight") {
        options.jobId = parsePositive(arg, own[++i]);
      }
      if (arg === "set-weight") {
        options.newWeight = parsePositive("set-weight <id>", own[++i]);
      }
      continue;
    }
    if (options.mode === "status" && options.jobId === 0 && /^\d+$/.test(arg)) {
      options.jobId = parsePositive("status", arg); // status <id>, before or after the options
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
      case "--weight":
        options.weight = parsePositive("--weight", own[++i]);
        break;
      case "--gpu":
        options.gpu = true;
        break;
      case "--after":
        options.after = parsePositive("--after", own[++i]);
        break;
      case "--keep-if-failed":
        options.keepIfFailed = true;
        break;
      case "--first":
        options.moveFirst = true;
        break;
      case "--before":
        options.moveBefore = parsePositive("--before", own[++i]);
        break;
      case "--reset":
        options.moveReset = true;
        break;
      case "--json":
        options.json = true;
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
    Number(options.moveFirst) + Number(options.moveBefore > 0) + Number(options.moveReset) !== 1
  ) {
    usageError("move needs one of --first, --before <id> or --reset.");
  }
  if (options.keepIfFailed && options.after === 0) {
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
  try {
    switch (options.mode) {
      case "run":
        await run(options);
        break;
      case "status":
        await status(options.port, options.jobId, options.json);
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
      case "stop":
        await stop(options.port);
        break;
      case "serve":
        serve(options);
        break;
      case "self-test": {
        const { selfTest } = await import("./self-test.ts");
        await selfTest();
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
