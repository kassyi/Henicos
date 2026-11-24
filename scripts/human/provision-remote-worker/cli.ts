// CLI orchestrator for `scripts/human/provision-remote-worker.js`.

import { spawnSync } from "node:child_process";
import * as fs from "node:fs";
import { createRequire } from "node:module";
import * as path from "node:path";
import { buildAutoinstallMetaData, buildAutoinstallUserData } from "./autoinstall.ts";
import { runProvisionerSelfTest } from "./checks.ts";
import { buildHardeningShellScript } from "./hardening.ts";
import { buildSeedIso } from "./iso.ts";
import { resolvePublicKey, validateProfile, type WorkerProfile } from "./profile.ts";
import { buildRemotesLocalJson } from "./remotes-json.ts";

const require = createRequire(import.meta.url);
const { mainCheckout } = require("../job-queue/lib/main-checkout.js") as {
  mainCheckout(startDir: string, fallbackRoot: string): string;
};

const HumanScriptsDir = path.resolve(import.meta.dirname, "..");
const RepoRoot = path.resolve(HumanScriptsDir, "..", "..");
const ExampleProfilePath = path.join(HumanScriptsDir, "job-queue", "worker-profile.example.json");
const DefaultStateDir = path.join(mainCheckout(HumanScriptsDir, RepoRoot), "tmp", "job-queue");
const DefaultProfilePath = path.join(DefaultStateDir, "worker-profile.json");

const Usage = [
  "Usage:",
  "  node scripts/human/provision-remote-worker.js --init-profile [--out <file>]",
  "  node scripts/human/provision-remote-worker.js --apply [--profile <file>] [--write-remotes] [--no-migrate-cache]",
  "  node scripts/human/provision-remote-worker.js --emit-autoinstall [--profile <file>] [--out <dir>]",
  "    [--build-iso] [--iso-via-ssh] [--write-remotes]",
  "  node scripts/human/provision-remote-worker.js --write-remotes [--profile <file>] [--out <file>]",
  "  node scripts/human/provision-remote-worker.js --self-test",
  "",
  "Options:",
  "  --init-profile      Copy worker-profile.example.json to tmp/job-queue/worker-profile.json (or --out).",
  "  --apply             Apply idempotent jobrunner isolation + UFW egress block to Ubuntu 24.04 via",
  "                      interactive `ssh -t <admin>@<host>` (sudo password prompted by remote sshd).",
  "  --emit-autoinstall  Generate Ubuntu 24.04 Subiquity autoinstall user-data & meta-data in --out <dir>",
  "                      for bare-metal or VM (Hyper-V / Proxmox / QEMU / VMware) unattended setup.",
  "  --build-iso         Also pack user-data & meta-data into <out>/cidata.iso",
  "                      (uses xorriso / genisoimage / mkisofs / cloud-localds).",
  "  --iso-via-ssh       When building cidata.iso without local ISO tools, pack it via the Ubuntu host.",
  "  --write-remotes     Write/update <mainCheckout>/tmp/job-queue/remotes.local.json from the profile.",
  "  --no-migrate-cache  With --apply, do not copy existing workspace/cacheDirs from admin to runner user.",
  "  --profile <file>    Path to worker-profile.json (default: <mainCheckout>/tmp/job-queue/worker-profile.json).",
  "  --out <path>        Output file or directory.",
  "  --self-test         Run built-in unit checks without touching any remote host.",
].join("\n");

export interface CliOptions {
  help: boolean;
  mode: "" | "init-profile" | "apply" | "emit-autoinstall" | "write-remotes" | "self-test";
  profile: string;
  out: string;
  buildIso: boolean;
  isoViaSsh: boolean;
  writeRemotes: boolean;
  migrateCache: boolean;
}

export interface CliPaths {
  readonly stateDir?: string;
  readonly remotesPath?: string;
}

export class CliUsageError extends Error {}

function readOptionValue(argv: readonly string[], index: number, flag: string): string {
  if (index + 1 >= argv.length) {
    throw new CliUsageError(`missing value for ${flag}`);
  }
  const value = argv[index + 1] as string;
  if (value === "") {
    throw new CliUsageError(`missing value for ${flag}`);
  }
  return value;
}

/** Parses CLI arguments without exiting the process; throws `CliUsageError` on invalid usage. */
export function parseArgs(argv: readonly string[], defaultProfilePath = DefaultProfilePath): CliOptions {
  const o: CliOptions = {
    help: false,
    mode: "",
    profile: defaultProfilePath,
    out: "",
    buildIso: false,
    isoViaSsh: false,
    writeRemotes: false,
    migrateCache: true,
  };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    switch (a) {
      case "--help":
      case "-h":
        o.help = true;
        return o;
      case "--init-profile":
        o.mode = "init-profile";
        break;
      case "--apply":
        o.mode = "apply";
        break;
      case "--emit-autoinstall":
        o.mode = "emit-autoinstall";
        break;
      case "--write-remotes":
        if (o.mode === "") {
          o.mode = "write-remotes";
        }
        o.writeRemotes = true;
        break;
      case "--self-test":
        o.mode = "self-test";
        break;
      case "--profile":
        o.profile = path.resolve(readOptionValue(argv, i, "--profile"));
        i++;
        break;
      case "--out":
        o.out = path.resolve(readOptionValue(argv, i, "--out"));
        i++;
        break;
      case "--build-iso":
        o.buildIso = true;
        break;
      case "--iso-via-ssh":
        o.isoViaSsh = true;
        break;
      case "--no-migrate-cache":
        o.migrateCache = false;
        break;
      default:
        throw new CliUsageError(`Unknown argument: ${String(a)}`);
    }
  }
  if (o.mode === "") {
    throw new CliUsageError("");
  }
  return o;
}

function writeRemotesFile(destPath: string, profile: WorkerProfile, verb: string): void {
  fs.mkdirSync(path.dirname(destPath), { recursive: true });
  fs.writeFileSync(destPath, `${JSON.stringify(buildRemotesLocalJson(profile), null, 2)}\n`, "utf8");
  process.stdout.write(`${verb} ${destPath}\n`);
}

export function main(argv: readonly string[], paths?: CliPaths): void {
  const stateDir = paths?.stateDir ?? DefaultStateDir;
  const defaultProfilePath = path.join(stateDir, "worker-profile.json");
  const defaultRemotesPath = paths?.remotesPath ?? path.join(stateDir, "remotes.local.json");
  let opts: CliOptions;
  try {
    opts = parseArgs(argv, defaultProfilePath);
  } catch (error) {
    if (error instanceof CliUsageError) {
      if (error.message !== "") {
        process.stderr.write(`${error.message}\n`);
      }
      process.stderr.write(`${Usage}\n`);
      process.exit(2);
    }
    throw error;
  }
  if (opts.help) {
    process.stdout.write(`${Usage}\n`);
    return;
  }
  if (opts.mode === "self-test") {
    runProvisionerSelfTest(ExampleProfilePath);
    return;
  }
  if (opts.mode === "init-profile") {
    const dest = opts.out || defaultProfilePath;
    fs.mkdirSync(path.dirname(dest), { recursive: true });
    if (fs.existsSync(dest)) {
      process.stdout.write(`profile already exists: ${dest}\n`);
      return;
    }
    fs.copyFileSync(ExampleProfilePath, dest);
    process.stdout.write(`wrote starter worker profile: ${dest}\n`);
    return;
  }

  const profile = validateProfile(JSON.parse(fs.readFileSync(opts.profile, "utf8")));

  if (opts.mode === "write-remotes") {
    writeRemotesFile(opts.out || defaultRemotesPath, profile, "wrote");
    return;
  }

  const pubKey = resolvePublicKey(profile);

  if (opts.mode === "emit-autoinstall") {
    const outDir = opts.out || path.join(stateDir, "autoinstall-seed");
    fs.mkdirSync(outDir, { recursive: true });
    fs.writeFileSync(path.join(outDir, "user-data"), buildAutoinstallUserData(profile, pubKey), "utf8");
    fs.writeFileSync(path.join(outDir, "meta-data"), buildAutoinstallMetaData(profile), "utf8");
    fs.writeFileSync(path.join(outDir, "harden-worker.sh"), buildHardeningShellScript(profile, pubKey, false), "utf8");
    process.stdout.write(`wrote autoinstall seed files to ${outDir}\n`);
    if (opts.buildIso) {
      const isoPath = buildSeedIso(outDir, profile, opts.isoViaSsh);
      process.stdout.write(`built seed ISO: ${isoPath}\n`);
    }
    if (opts.writeRemotes) {
      writeRemotesFile(defaultRemotesPath, profile, "updated");
    }
    return;
  }

  if (opts.mode === "apply") {
    const adminTarget = `${profile.adminUser}@${profile.connectHost}`;
    const scriptB64 = Buffer.from(buildHardeningShellScript(profile, pubKey, opts.migrateCache), "utf8").toString(
      "base64",
    );
    const remoteCmd = `echo '${scriptB64}' | base64 -d | sudo bash -s`;
    process.stdout.write(`applying jobrunner hardening to ${adminTarget} (sudo password may be prompted)...\n`);
    const res = spawnSync(
      "ssh",
      ["-t", "-o", "StrictHostKeyChecking=yes", "-o", "ConnectTimeout=10", "--", adminTarget, remoteCmd],
      { stdio: "inherit" },
    );
    if (res.error) {
      process.stderr.write(`failed to run ssh: ${res.error.message}\n`);
      process.exit(1);
    }
    if (res.status !== 0) {
      process.exit(res.status ?? 1);
    }
    if (opts.writeRemotes) {
      writeRemotesFile(defaultRemotesPath, profile, "updated");
    }
  }
}
