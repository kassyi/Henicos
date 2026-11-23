// Self-test checks for `provision-remote-worker`: profile validation, hardening script, autoinstall YAML,
// non-.NET custom profiles, legacy `dotnetChannel` fallback, and `remotes.local.json` generation.

import * as assert from "node:assert";
import * as fs from "node:fs";
import { buildAutoinstallMetaData, buildAutoinstallUserData } from "./autoinstall.ts";
import { buildHardeningShellScript, buildSshdMatchConfig } from "./hardening.ts";
import { validateProfile } from "./profile.ts";
import { buildRemotesLocalJson } from "./remotes-json.ts";

export function runProvisionerSelfTest(exampleProfilePath: string): void {
  const sampleRaw = JSON.parse(fs.readFileSync(exampleProfilePath, "utf8")) as Record<string, unknown>;
  const profile = validateProfile(sampleRaw);
  const fakeKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITestKeyForSelfTestOnly test@local";

  assert.throws(() => validateProfile({ ...sampleRaw, runnerUser: "root" }));
  assert.throws(() => validateProfile({ ...sampleRaw, runnerUser: sampleRaw.adminUser }));
  assert.throws(() => validateProfile({ ...sampleRaw, blockedLanCidrs: ["not-a-cidr"] }));
  assert.throws(() => validateProfile({ ...sampleRaw, connectHost: "-oProxyCommand=evil" }));
  assert.throws(() => validateProfile({ ...sampleRaw, packages: ["clang; rm -rf /"] }));
  assert.throws(() => validateProfile({ ...sampleRaw, setupCommands: ["echo ok\nrm -rf /"] }));
  assert.throws(() => validateProfile({ ...sampleRaw, cacheDirs: ["../etc"] }));
  assert.throws(() => validateProfile({ ...sampleRaw, workspace: "~/../etc" }));

  const sshd = buildSshdMatchConfig(profile);
  assert.ok(sshd.includes("Match User jobrunner"));
  assert.ok(sshd.includes("AuthorizedKeysFile /etc/ssh/authorized_keys/%u"));
  assert.ok(sshd.includes("AllowTcpForwarding no"));
  assert.ok(sshd.includes("AllowAgentForwarding no"));

  const script = buildHardeningShellScript(profile, fakeKey, true);
  assert.ok(script.includes('chown root:root "/etc/ssh/authorized_keys/$RUNNER_USER"'));
  assert.ok(script.includes("--uid-owner ${RUNNER_UID} -j REJECT --reject-with icmp-port-unreachable"));
  assert.ok(script.includes("ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -j REJECT"));
  assert.ok(script.includes("BEGIN GSHARP-JOBRUNNER-ISOLATION"));
  assert.ok(script.includes("/home/$RUNNER_USER/henicos"));
  assert.ok(script.includes("/home/$RUNNER_USER/.nuget/packages"));

  const lanOnlyScript = buildHardeningShellScript(
    validateProfile({ ...sampleRaw, blockAllEgress: false }),
    fakeKey,
    false,
  );
  assert.ok(lanOnlyScript.includes("-d 192.168.0.0/16 -j REJECT"));

  const userData = buildAutoinstallUserData(profile, fakeKey);
  assert.ok(userData.startsWith("#cloud-config\nautoinstall:\n"));
  assert.ok(userData.includes("name: direct"), "uses hardware-agnostic direct storage layout for bare-metal & VM");
  assert.ok(userData.includes("    - clang"));
  assert.ok(userData.includes("    - ninja-build"));
  assert.ok(userData.includes("dotnet-install.sh --channel 10.0"));
  assert.ok(userData.includes("just.systems/install.sh"));
  assert.ok(userData.includes("jobrunner-harden-worker.sh"));

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
  assert.ok(rustUserData.includes("echo rustup-install"));
  assert.ok(!rustUserData.includes("dotnet-install.sh"), "non-.NET profile omits .NET setup");
  assert.ok(!rustUserData.includes("    - clang"), "non-.NET profile omits undeclared packages");
  const rustScript = buildHardeningShellScript(rustProfile, fakeKey, true);
  assert.ok(rustScript.includes("/home/$RUNNER_USER/rust-repo"));
  assert.ok(rustScript.includes("/home/$RUNNER_USER/.cargo/registry"));
  assert.ok(!rustScript.includes(".nuget/packages"));

  const legacyRaw: Record<string, unknown> = { ...sampleRaw, dotnetChannel: "9.0" };
  delete legacyRaw.setupCommands;
  const legacyUserData = buildAutoinstallUserData(validateProfile(legacyRaw), fakeKey);
  assert.ok(legacyUserData.includes("dotnet-install.sh --channel 9.0"));

  const metaData = buildAutoinstallMetaData(profile);
  assert.ok(metaData.includes("local-hostname: ubuntu-server"));

  const remotes = buildRemotesLocalJson(profile);
  assert.strictEqual(remotes.remotes.ubuntu?.host, "jobrunner@ubuntu-server.lan");
  assert.ok(remotes.remotes.ubuntu?.sshOptions.includes("StrictHostKeyChecking=yes"));
  assert.ok(remotes.remotes.ubuntu?.sshOptions.includes("ServerAliveInterval=5"));
  assert.strictEqual(remotes.remotes.ubuntu?.probe.argv[0], "ssh");

  process.stdout.write("provision-remote-worker self-test: passed\n");
}
