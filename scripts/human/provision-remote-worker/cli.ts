// CLI orchestrator for `scripts/human/provision-remote-worker.js`.

import { spawnSync } from "node:child_process";
import * as fs from "node:fs";
import { createRequire } from "node:module";
import * as path from "node:path";
import { buildAutoinstallMetaData, buildAutoinstallUserData } from "./autoinstall.ts";
import { runProvisionerSelfTest } from "./checks.ts";
import { buildHardeningShellScript } from "./hardening.ts";
import { buildSeedIso } from "./iso.ts";
import { resolvePublicKey, validateProfile } from "./profile.ts";
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
const DefaultRemotesPath = path.join(DefaultStateDir, "remotes.local.json");

const Usage = [
  "Usage:",
  "  node scripts/human/provision-remote-worker.js --init-profile [--out <file>]",
  "  node scripts/human/provision-remote-worker.js --apply [--profile <file>] [--write-remotes] [--no-migrate-cache]",
  "  node scripts/human/provision-remote-worker.js --emit-autoinstall [--profile <file>] --out <dir> [--build-iso] [--iso-via-ssh]",
  "  node scripts/human/provision-remote-worker.js --write-remotes [--profile <file>] [--out <file>]",
  "  node scripts/human/provision-remote-worker.js --self-test",
  "",
  "Options:",
  "  --init-profile      Copy worker-profile.example.json to tmp/job-queue/worker-profile.json (or --out).",
  "  --apply             Apply idempotent jobrunner isolation + UFW LAN egress block to an existing Ubuntu 24.04 host",
  "                      via interactive `ssh -t <admin>@<host>` (sudo password prompted by remote sshd).",
  "  --emit-autoinstall  Generate Ubuntu 24.04 Subiquity autoinstall user-data & meta-data in --out <dir> for",
  "                      bare-metal or VM (Hyper-V / Proxmox / QEMU / VMware) unattended installation.",
  "  --build-iso         Also pack user-data & meta-data into <out>/cidata.iso (uses xorriso/genisoimage/cloud-localds).",
  "  --iso-via-ssh       When building cidata.iso on Windows without local xorriso, pack it via the configured Ubuntu host.",
  "  --write-remotes     Write/update <mainCheckout>/tmp/job-queue/remotes.local.json from the profile.",
  "  --no-migrate-cache  With --apply, do not copy existing workspace/cacheDirs from admin user to runner user.",
  "  --profile <file>    Path to worker-profile.json (default: <mainCheckout>/tmp/job-queue/worker-profile.json).",
  "  --out <path>        Output file or directory.",
  "  --self-test         Run built-in unit checks without touching any remote host.",
].join("\n");

interface CliOptions {
  mode: "" | "init-profile" | "apply" | "emit-autoinstall" | "write-remotes" | "self-test";
  profile: string;
  out: string;
  buildIso: boolean;
  isoViaSsh: boolean;
  writeRemotes: boolean;
  migrateCache: boolean;
}

function parseArgs(argv: readonly string[]): CliOptions {
  const o: CliOptions = {
    mode: "",
    profile: DefaultProfilePath,
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
        process.stdout.write(`${Usage}\n`);
        process.exit(0);
        break;
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
        o.profile = path.resolve(argv[++i] ?? "");
        break;
      case "--out":
        o.out = path.resolve(argv[++i] ?? "");
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
        process.stderr.write(`Unknown argument: ${String(a)}\n${Usage}\n`);
        process.exit(2);
    }
  }
  if (o.mode === "") {
    process.stderr.write(`${Usage}\n`);
    process.exit(2);
  }
  return o;
}

export function main(argv: readonly string[]): void {
  const opts = parseArgs(argv);
  if (opts.mode === "self-test") {
    runProvisionerSelfTest(ExampleProfilePath);
    return;
  }
  if (opts.mode === "init-profile") {
    const dest = opts.out || DefaultProfilePath;
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
    const dest = opts.out || DefaultRemotesPath;
    fs.mkdirSync(path.dirname(dest), { recursive: true });
    fs.writeFileSync(dest, `${JSON.stringify(buildRemotesLocalJson(profile), null, 2)}\n`, "utf8");
    process.stdout.write(`wrote ${dest}\n`);
    return;
  }

  const pubKey = resolvePublicKey(profile);

  if (opts.mode === "emit-autoinstall") {
    const outDir = opts.out || path.join(DefaultStateDir, "autoinstall-seed");
    fs.mkdirSync(outDir, { recursive: true });
    fs.writeFileSync(path.join(outDir, "user-data"), buildAutoinstallUserData(profile, pubKey), "utf8");
    fs.writeFileSync(path.join(outDir, "meta-data"), buildAutoinstallMetaData(profile), "utf8");
    fs.writeFileSync(path.join(outDir, "harden-worker.sh"), buildHardeningShellScript(profile, pubKey, false), "utf8");
    process.stdout.write(`wrote autoinstall seed files to ${outDir}\n`);
    if (opts.buildIso) {
      const isoPath = buildSeedIso(outDir, profile, opts.isoViaSsh);
      process.stdout.write(`built seed ISO: ${isoPath}\n`);
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
    if (res.status !== 0) {
      process.exit(res.status ?? 1);
    }
    if (opts.writeRemotes) {
      fs.mkdirSync(path.dirname(DefaultRemotesPath), { recursive: true });
      fs.writeFileSync(DefaultRemotesPath, `${JSON.stringify(buildRemotesLocalJson(profile), null, 2)}\n`, "utf8");
      process.stdout.write(`updated ${DefaultRemotesPath}\n`);
    }
  }
}
