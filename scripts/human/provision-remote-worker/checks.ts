// Self-test checks for `provision-remote-worker`: profile validation, key injection guards, hardening script,
// UFW rule patching, autoinstall YAML escaping, `remotes.local.json` generation, and CLI arg handling.

import * as assert from "node:assert";
import { spawnSync } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import type { QueueConfig } from "../job-queue/config.ts";
import { buildSshArgs, remoteSpecsFromConfig } from "../job-queue/remote/spec.ts";
import { buildAutoinstallMetaData, buildAutoinstallUserData } from "./autoinstall.ts";
import { CliUsageError, main, parseArgs } from "./cli.ts";
import { buildHardeningShellScript, buildSshdMatchConfig } from "./hardening.ts";
import { resolvePublicKey, validateProfile } from "./profile.ts";
import { buildRemotesLocalJson } from "./remotes-json.ts";

function decodeSetupCommandsFromUserData(userData: string): string[] {
  const decoded: string[] = [];
  const prefix = "\"curtin in-target -- bash -c 'echo ";
  const suffix = " | base64 -d | bash -euo pipefail'\"";
  for (const line of userData.split("\n")) {
    const trimmed = line.trim();
    if (trimmed.startsWith("- ") && trimmed.includes(prefix) && trimmed.endsWith(suffix)) {
      const start = trimmed.indexOf(prefix) + prefix.length;
      const end = trimmed.length - suffix.length;
      const b64 = trimmed.slice(start, end);
      decoded.push(Buffer.from(b64, "base64").toString("utf8"));
    }
  }
  return decoded;
}

function findWorkingPython(): string | null {
  for (const bin of ["python3", "python"]) {
    const probe = spawnSync(bin, ["-I", "-c", "import re, sys; print(sys.version_info[0])"], {
      encoding: "utf8",
      windowsHide: true,
    });
    if (!probe.error && probe.status === 0 && probe.stdout.trim() === "3") {
      return bin;
    }
  }
  return null;
}

function extractPatchUfwPython(script: string): string {
  const start = script.indexOf("<<'__GSHARP_PY__'\n");
  const end = script.indexOf("\n__GSHARP_PY__", start);
  assert.ok(start >= 0 && end > start, "hardening script embeds patch_ufw_rules python block");
  return script.slice(start + "<<'__GSHARP_PY__'\n".length, end);
}

function sshOptionKeys(args: readonly string[]): string[] {
  const keys: string[] = [];
  for (let i = 0; i < args.length; i++) {
    if (args[i] === "--") {
      break;
    }
    if (args[i] === "-o" && i + 1 < args.length) {
      const opt = args[++i] as string;
      const eq = opt.indexOf("=");
      keys.push(eq >= 0 ? opt.slice(0, eq).toLowerCase() : opt.toLowerCase());
    }
  }
  return keys;
}

export function runProvisionerSelfTest(exampleProfilePath: string): void {
  const sampleRaw = JSON.parse(fs.readFileSync(exampleProfilePath, "utf8")) as Record<string, unknown>;
  const profile = validateProfile(sampleRaw);
  const fakeKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITestKeyForSelfTestOnly test@local";
  const sampleNet = sampleRaw.network as Record<string, unknown>;

  assert.throws(() => validateProfile({ ...sampleRaw, runnerUser: "root" }));
  assert.throws(() => validateProfile({ ...sampleRaw, runnerUser: sampleRaw.adminUser }));
  assert.throws(() => validateProfile({ ...sampleRaw, blockedLanCidrs: ["not-a-cidr"] }));
  assert.throws(() => validateProfile({ ...sampleRaw, blockedLanCidrs: ["999.0.0.1/24"] }));
  assert.throws(() => validateProfile({ ...sampleRaw, blockedLanCidrs: ["192.0.2.1/33"] }));
  assert.throws(() => validateProfile({ ...sampleRaw, network: { ...sampleNet, gateway4: "999.999.999.999" } }));
  assert.throws(() =>
    validateProfile({
      ...sampleRaw,
      network: { ...sampleNet, nameservers: ["1.1.1.1]\n        foo: [injected"] },
    }),
  );
  assert.throws(() => validateProfile({ ...sampleRaw, network: { ...sampleNet, nameservers: ["not-an-ip"] } }));
  assert.throws(() => validateProfile({ ...sampleRaw, network: { ...sampleNet, nameservers: [123] } }));
  assert.throws(() => validateProfile({ ...sampleRaw, network: { ...sampleNet, nameservers: "1.1.1.1" } }));
  assert.throws(() => validateProfile({ ...sampleRaw, connectHost: "-oProxyCommand=evil" }));
  assert.throws(() => validateProfile({ ...sampleRaw, packages: ["clang; rm -rf /"] }));
  assert.throws(() => validateProfile({ ...sampleRaw, setupCommands: ["echo ok\nrm -rf /"] }));
  assert.throws(() => validateProfile({ ...sampleRaw, cacheDirs: ["../etc"] }));
  assert.throws(() => validateProfile({ ...sampleRaw, workspace: "~/../etc" }));

  const ipv6DnsProfile = validateProfile({
    ...sampleRaw,
    network: { ...sampleNet, nameservers: ["192.0.2.1", "2001:4860:4860::8888"] },
  });
  const ipv6DnsUserData = buildAutoinstallUserData(ipv6DnsProfile, fakeKey);
  assert.ok(ipv6DnsUserData.includes('addresses: ["192.0.2.1", "2001:4860:4860::8888"]'));

  const tmpRoot = fs.mkdtempSync(path.join(os.tmpdir(), "gsharp-provisioner-test-"));
  try {
    const keyFile = path.join(tmpRoot, "id_test.pub");
    fs.writeFileSync(keyFile, `${fakeKey}\n`, "utf8");
    assert.strictEqual(resolvePublicKey({ ...profile, sshPublicKey: fakeKey }), fakeKey);
    assert.strictEqual(resolvePublicKey({ ...profile, sshPublicKey: "", sshPublicKeyPath: keyFile }), fakeKey);

    assert.throws(() =>
      resolvePublicKey({
        ...profile,
        sshPublicKey: "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITestKey\nssh-rsa AAAAB3NzaC1yc2EAAAAAttackerKey",
      }),
    );
    assert.throws(() =>
      resolvePublicKey({
        ...profile,
        sshPublicKey: "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITestKey\n__GSHARP_PUBKEY__",
      }),
    );
    assert.throws(() =>
      resolvePublicKey({
        ...profile,
        sshPublicKey: "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITestKey\rssh-rsa AAAAB3NzaC1yc2E",
      }),
    );
    assert.throws(() =>
      resolvePublicKey({
        ...profile,
        sshPublicKey: "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITestKey\0extra",
      }),
    );
    fs.writeFileSync(keyFile, `${fakeKey}\nssh-rsa AAAAB3NzaC1yc2EAAAAAEVIL\n`, "utf8");
    assert.throws(() => resolvePublicKey({ ...profile, sshPublicKey: "", sshPublicKeyPath: keyFile }));

    const sshd = buildSshdMatchConfig(profile);
    assert.ok(sshd.includes("Match User jobrunner"));
    assert.ok(sshd.includes("AuthorizedKeysFile /etc/ssh/authorized_keys/%u"));
    assert.ok(sshd.includes("AllowTcpForwarding no"));
    assert.ok(sshd.includes("AllowAgentForwarding no"));
    assert.ok(sshd.trimEnd().endsWith("Match all"), "terminates Match User block with Match all");

    const script = buildHardeningShellScript(profile, fakeKey, true);
    assert.ok(script.includes('chown root:root "/etc/ssh/authorized_keys/$RUNNER_USER"'));
    assert.ok(script.includes("--uid-owner ${RUNNER_UID} -j REJECT --reject-with icmp-port-unreachable"));
    assert.ok(script.includes("ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -j REJECT"));
    assert.ok(script.includes("BEGIN GSHARP-JOBRUNNER-ISOLATION"));
    assert.ok(script.includes("/home/$RUNNER_USER/henicos"));
    assert.ok(script.includes("/home/$RUNNER_USER/.nuget/packages"));
    assert.ok(script.includes("Match all"));
    assert.ok(script.includes("[ ! -f /etc/ufw/before.rules ]"), "fails when /etc/ufw/before.rules is absent");
    assert.ok(
      script.includes("command -v ischroot >/dev/null 2>&1 && ischroot"),
      "only tolerates ufw errors in chroot",
    );
    assert.ok(script.includes("  ufw reload >/dev/null\n"), "non-chroot ufw reload does not swallow failure");

    const nestedProfile = validateProfile({ ...sampleRaw, workspace: "~/sub/henicos" });
    const nestedScript = buildHardeningShellScript(nestedProfile, fakeKey, true);
    const mkdirParent = '  mkdir -p "$(dirname "/home/$RUNNER_USER/sub/henicos")"';
    const chownParents = '  chown_runner_parents "$(dirname "/home/$RUNNER_USER/sub/henicos")"';
    const copyCmd = '  cp -a "/home/$ADMIN_USER/sub/henicos" "/home/$RUNNER_USER/sub/henicos"';
    assert.ok(nestedScript.includes(mkdirParent), "creates nested workspace parent before cp -a");
    assert.ok(nestedScript.includes(chownParents), "chowns intermediate parent dirs under /home/$RUNNER_USER");
    assert.ok(
      nestedScript.indexOf(mkdirParent) < nestedScript.indexOf(copyCmd) &&
        nestedScript.indexOf(chownParents) < nestedScript.indexOf(copyCmd),
      "creates and chowns parent dirs before cp -a",
    );
    assert.ok(
      nestedScript.includes('chown_runner_parents "$(dirname "/home/$RUNNER_USER/sub/henicos")"'),
      "chowns parent dirs on non-migrated workspace creation path too",
    );

    const noMigrateNested = buildHardeningShellScript(nestedProfile, fakeKey, false);
    assert.ok(!noMigrateNested.includes("cp -a"));
    assert.ok(noMigrateNested.includes('chown_runner_parents "$(dirname "/home/$RUNNER_USER/sub/henicos")"'));

    const lanOnlyScript = buildHardeningShellScript(
      validateProfile({ ...sampleRaw, blockAllEgress: false }),
      fakeKey,
      false,
    );
    assert.ok(lanOnlyScript.includes("-d 192.168.0.0/16 -j REJECT"));
    assert.ok(lanOnlyScript.includes("-d fc00::/7 -j REJECT"));
    assert.ok(lanOnlyScript.includes("-d fe80::/10 -j REJECT"));

    const pyBin = findWorkingPython();
    const pyCode = extractPatchUfwPython(script);
    assert.ok(!pyCode.includes('rfind("\\nCOMMIT")'), "does not target last COMMIT blindly");
    if (pyBin !== null) {
      const pyScriptPath = path.join(tmpRoot, "patch_ufw.py");
      const rulesPath = path.join(tmpRoot, "before.rules");
      const blockPath = path.join(tmpRoot, "block.rules");
      fs.writeFileSync(pyScriptPath, pyCode, "utf8");
      fs.writeFileSync(
        blockPath,
        "# BEGIN GSHARP-JOBRUNNER-ISOLATION\n-A ufw-before-output -j ACCEPT\n# END GSHARP-JOBRUNNER-ISOLATION\n",
        "utf8",
      );
      const initialRules = [
        "*filter",
        ":ufw-before-output - [0:0]",
        "-A ufw-before-output -o lo -j ACCEPT",
        "COMMIT",
        "",
        "*nat",
        ":POSTROUTING ACCEPT [0:0]",
        "COMMIT",
        "",
      ].join("\n");
      fs.writeFileSync(rulesPath, initialRules, "utf8");
      const runPatch = (): string => {
        const res = spawnSync(pyBin, ["-I", pyScriptPath, rulesPath, blockPath], {
          encoding: "utf8",
          windowsHide: true,
        });
        assert.strictEqual(res.status, 0, res.stderr);
        return fs.readFileSync(rulesPath, "utf8");
      };
      const after1 = runPatch();
      const filterEnd = after1.indexOf("*nat");
      const beginPos = after1.indexOf("# BEGIN GSHARP-JOBRUNNER-ISOLATION");
      assert.ok(beginPos > 0 && beginPos < filterEnd, "inserts rules into *filter before *nat");
      const after2 = runPatch();
      const after3 = runPatch();
      assert.strictEqual(after2, after1, "second patch_ufw_rules run is byte-identical");
      assert.strictEqual(after3, after1, "third patch_ufw_rules run is byte-identical");
    }

    const userData = buildAutoinstallUserData(profile, fakeKey);
    assert.ok(userData.startsWith("#cloud-config\nautoinstall:\n"));
    assert.ok(userData.includes("name: direct"), "uses hardware-agnostic direct storage layout for bare-metal & VM");
    assert.ok(userData.includes("    - clang"));
    assert.ok(userData.includes("    - ninja-build"));
    assert.ok(userData.includes("jobrunner-harden-worker.sh"));
    const decodedSampleCmds = decodeSetupCommandsFromUserData(userData);
    assert.deepStrictEqual(decodedSampleCmds, profile.setupCommands);
    assert.ok(decodedSampleCmds.some((c) => c.includes("dotnet-install.sh --channel 10.0")));
    assert.ok(decodedSampleCmds.some((c) => c.includes("just.systems/install.sh")));

    const trickyCmds = [
      'curl -H "Accept: application/json" https://example.com # inline comment',
      "export FOO=\"$SECRET:/opt/bin\"; echo $(uname -s) 'quoted'",
    ];
    const trickyProfile = validateProfile({ ...sampleRaw, setupCommands: trickyCmds });
    const trickyUserData = buildAutoinstallUserData(trickyProfile, fakeKey);
    assert.ok(!trickyUserData.includes("Accept: application/json"));
    assert.ok(!trickyUserData.includes("$SECRET"));
    assert.ok(!trickyUserData.includes("$(uname -s)"));
    assert.deepStrictEqual(decodeSetupCommandsFromUserData(trickyUserData), trickyCmds);

    const dhcpUserData = buildAutoinstallUserData(validateProfile({ ...sampleRaw, network: { dhcp4: true } }), fakeKey);
    assert.ok(dhcpUserData.includes("        dhcp4: true"));
    assert.ok(!dhcpUserData.includes("gateway4"));

    const rustProfile = validateProfile({
      ...sampleRaw,
      workspace: "~/rust-repo",
      packages: ["build-essential", "pkg-config"],
      setupCommands: ["echo rustup-install"],
      cacheDirs: [".cargo/registry"],
    });
    const rustUserData = buildAutoinstallUserData(rustProfile, fakeKey);
    assert.ok(rustUserData.includes("    - openssh-server"), "always keeps base OS packages");
    assert.ok(rustUserData.includes("    - build-essential"));
    assert.deepStrictEqual(decodeSetupCommandsFromUserData(rustUserData), ["echo rustup-install"]);
    assert.ok(!rustUserData.includes("dotnet-install.sh"), "non-.NET profile omits .NET setup");
    assert.ok(!rustUserData.includes("    - clang"), "non-.NET profile omits undeclared packages");
    const rustScript = buildHardeningShellScript(rustProfile, fakeKey, true);
    assert.ok(rustScript.includes("/home/$RUNNER_USER/rust-repo"));
    assert.ok(rustScript.includes("/home/$RUNNER_USER/.cargo/registry"));
    assert.ok(!rustScript.includes(".nuget/packages"));

    const legacyRaw: Record<string, unknown> = { ...sampleRaw, dotnetChannel: "9.0" };
    delete legacyRaw.setupCommands;
    const legacyUserData = buildAutoinstallUserData(validateProfile(legacyRaw), fakeKey);
    const decodedLegacyCmds = decodeSetupCommandsFromUserData(legacyUserData);
    assert.ok(decodedLegacyCmds.some((c) => c.includes("dotnet-install.sh --channel 9.0")));

    const metaData = buildAutoinstallMetaData(profile);
    assert.ok(metaData.includes("local-hostname: ubuntu-server"));

    const remotes = buildRemotesLocalJson(profile);
    const ubuntuRemote = remotes.remotes.ubuntu;
    assert.ok(ubuntuRemote !== undefined);
    assert.strictEqual(ubuntuRemote.host, "jobrunner@ubuntu-server.lan");
    assert.deepStrictEqual(ubuntuRemote.sshOptions, ["-o", "ConnectTimeout=5"]);
    assert.ok(!ubuntuRemote.sshOptions.includes("StrictHostKeyChecking=yes"));
    assert.strictEqual(ubuntuRemote.probe.argv[0], "ssh");
    assert.deepStrictEqual(ubuntuRemote.probe.argv.slice(0, 4), ["ssh", "-o", "ConnectTimeout=3", "--"]);
    const loadedSpecs = remoteSpecsFromConfig({
      version: 1,
      entry: "scripts/human/job-queue.js",
      plugins: [],
      remotes: remotes.remotes,
    } as QueueConfig);
    assert.strictEqual(loadedSpecs.length, 1);
    const spec = loadedSpecs[0];
    assert.ok(spec !== undefined && spec.probe !== null);
    const fullSshArgs = buildSshArgs(spec, "true");
    const sshKeys = sshOptionKeys(fullSshArgs);
    assert.strictEqual(new Set(sshKeys).size, sshKeys.length, "ssh argv has no duplicate -o options");
    const probeKeys = sshOptionKeys(spec.probe.argv.slice(1));
    assert.strictEqual(new Set(probeKeys).size, probeKeys.length, "probe argv has no duplicate -o options");

    assert.throws(() => parseArgs(["--profile"]), CliUsageError);
    assert.throws(() => parseArgs(["--out"]), CliUsageError);
    assert.throws(() => parseArgs(["--profile", ""]), CliUsageError);
    const parsedOrder1 = parseArgs(["--emit-autoinstall", "--write-remotes"]);
    assert.strictEqual(parsedOrder1.mode, "emit-autoinstall");
    assert.strictEqual(parsedOrder1.writeRemotes, true);
    const parsedOrder2 = parseArgs(["--write-remotes", "--emit-autoinstall"]);
    assert.strictEqual(parsedOrder2.mode, "emit-autoinstall");
    assert.strictEqual(parsedOrder2.writeRemotes, true);

    const cliStateDir = path.join(tmpRoot, "state");
    const cliOutDir = path.join(tmpRoot, "seed");
    const cliProfilePath = path.join(tmpRoot, "profile.json");
    fs.writeFileSync(cliProfilePath, JSON.stringify({ ...sampleRaw, sshPublicKey: fakeKey }, null, 2), "utf8");
    main(["--emit-autoinstall", "--profile", cliProfilePath, "--out", cliOutDir, "--write-remotes"], {
      stateDir: cliStateDir,
    });
    assert.ok(fs.existsSync(path.join(cliOutDir, "user-data")));
    assert.ok(fs.existsSync(path.join(cliOutDir, "meta-data")));
    assert.ok(fs.existsSync(path.join(cliOutDir, "harden-worker.sh")));
    const writtenRemotesPath = path.join(cliStateDir, "remotes.local.json");
    assert.ok(fs.existsSync(writtenRemotesPath), "--emit-autoinstall --write-remotes writes remotes.local.json");
    const writtenRemotes = JSON.parse(fs.readFileSync(writtenRemotesPath, "utf8")) as {
      remotes: { ubuntu?: { host?: string } };
    };
    assert.strictEqual(writtenRemotes.remotes.ubuntu?.host, "jobrunner@ubuntu-server.lan");

    const entryScript = path.resolve(import.meta.dirname, "..", "provision-remote-worker.js");
    const cliMissingArg = spawnSync(process.execPath, [entryScript, "--write-remotes", "--profile"], {
      encoding: "utf8",
      windowsHide: true,
    });
    assert.strictEqual(cliMissingArg.status, 2);
    assert.ok(cliMissingArg.stderr.includes("missing value for --profile"));
  } finally {
    fs.rmSync(tmpRoot, { recursive: true, force: true });
  }

  process.stdout.write("provision-remote-worker self-test: passed\n");
}
