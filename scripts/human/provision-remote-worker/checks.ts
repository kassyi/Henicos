// Self-test checks for `provision-remote-worker`: profile validation, key injection guards, hardening script,
// UFW rule patching, autoinstall YAML escaping, `remotes.local.json` generation, and CLI arg handling.

import * as assert from "node:assert";
import { spawnSync } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import type { QueueConfig } from "../job-queue/config.ts";
import { buildSshArgs, remoteSpecsFromConfig, RemoteWorkspaceMarkerFile } from "../job-queue/remote/spec.ts";
import { gitBashPath } from "../job-queue/shell.ts";
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
  for (const reservedUser of [
    "ubuntu",
    "sshd",
    "root",
    "_apt",
    "_rpc",
    "systemd-network",
    "systemd-custom",
    "daemon",
    "nobody",
    "www-data",
    "admin",
    "cloud-init",
  ]) {
    assert.throws(
      () => validateProfile({ ...sampleRaw, adminUser: "ops", runnerUser: reservedUser }),
      `rejects reserved/system runnerUser ${reservedUser}`,
    );
  }
  assert.strictEqual(
    validateProfile({ ...sampleRaw, adminUser: "ubuntu", runnerUser: "jobrunner" }).adminUser,
    "ubuntu",
  );
  assert.throws(() => validateProfile({ ...sampleRaw, blockedLanCidrs: ["not-a-cidr"] }));
  assert.throws(() => validateProfile({ ...sampleRaw, blockedLanCidrs: ["999.0.0.1/24"] }));
  assert.throws(() => validateProfile({ ...sampleRaw, blockedLanCidrs: ["192.0.2.1/33"] }));
  assert.throws(() => validateProfile({ ...sampleRaw, blockedLanCidrs: "192.168.0.0/16" }));
  assert.throws(() => validateProfile({ ...sampleRaw, blockAllEgress: false, blockedLanCidrs: [] }));
  assert.strictEqual(validateProfile({ ...sampleRaw, blockAllEgress: true, blockedLanCidrs: [] }).blockAllEgress, true);
  assert.strictEqual(
    validateProfile({ ...sampleRaw, blockedLanCidrs: ["0.0.0.0/0", "10.0.0.0/08"] }).blockedLanCidrs.length,
    2,
  );
  assert.throws(() => validateProfile({ ...sampleRaw, network: { ...sampleNet, gateway4: "999.999.999.999" } }));
  assert.throws(() => validateProfile({ ...sampleRaw, network: { ...sampleNet, gateway4: "0.0.0.0" } }));
  assert.throws(() =>
    validateProfile({
      ...sampleRaw,
      network: { ...sampleNet, nameservers: ["1.1.1.1]\n        foo: [injected"] },
    }),
  );
  assert.throws(() => validateProfile({ ...sampleRaw, network: { ...sampleNet, nameservers: ["not-an-ip"] } }));
  assert.throws(() => validateProfile({ ...sampleRaw, network: { ...sampleNet, nameservers: [123] } }));
  assert.throws(() => validateProfile({ ...sampleRaw, network: { ...sampleNet, nameservers: "1.1.1.1" } }));
  for (const unspecIp of ["0.0.0.0", "::", "::0", "0:0:0:0:0:0:0:0", "::ffff:0.0.0.0", "::ffff:0:0"]) {
    assert.throws(
      () => validateProfile({ ...sampleRaw, network: { ...sampleNet, nameservers: [unspecIp] } }),
      `rejects unspecified nameserver ${unspecIp}`,
    );
  }
  assert.throws(() => validateProfile({ ...sampleRaw, connectHost: "-oProxyCommand=evil" }));
  assert.throws(() => validateProfile({ ...sampleRaw, packages: ["clang; rm -rf /"] }));
  for (const badPkg of ["ufw-", "openssh-server-", "g++-", "clang+", "g+++"]) {
    assert.throws(() => validateProfile({ ...sampleRaw, packages: [badPkg] }), `rejects invalid package ${badPkg}`);
  }
  assert.deepStrictEqual(validateProfile({ ...sampleRaw, packages: ["g++", "g++-13", "clang"] }).packages, [
    "g++",
    "g++-13",
    "clang",
  ]);
  assert.throws(() => validateProfile({ ...sampleRaw, setupCommands: ["echo ok\nrm -rf /"] }));
  for (const badCacheDir of [
    ".",
    "./",
    "./foo",
    "foo/.",
    "foo/./bar",
    "..",
    "../etc",
    "foo/..",
    "",
    "/",
    "///",
    "/tmp/cache",
    "/var/cache",
    "foo//bar",
    ".nuget//packages",
    "~/cache",
    "-rf",
    "foo/-rf",
    " .nuget/packages",
    ".nuget/packages\n",
    ".ssh",
    ".ssh/keys",
    ".gnupg",
    ".aws",
    ".kube",
    ".docker",
    ".pki",
    ".netrc",
    ".git-credentials",
    ".npmrc",
    ".pypirc",
    ".config",
    ".local",
    ".hidden",
    "sub/.ssh",
    ".nuget/.ssh",
    ".cargo/.hidden",
    ".cargo",
    ".nuget",
  ]) {
    assert.throws(
      () => validateProfile({ ...sampleRaw, cacheDirs: [badCacheDir] }),
      `rejects unsafe cacheDir ${JSON.stringify(badCacheDir)}`,
    );
  }
  assert.deepStrictEqual(
    validateProfile({
      ...sampleRaw,
      cacheDirs: ["caches/nuget", ".m2/repository", ".nuget/packages", ".cargo/registry"],
    }).cacheDirs,
    ["caches/nuget", ".m2/repository", ".nuget/packages", ".cargo/registry"],
  );
  for (const badWorkspace of [
    "~",
    "/",
    "~/",
    "///",
    "~user/henicos",
    "~/.",
    "~/..",
    "~/./a",
    "~/a/.",
    "~/a/..",
    "~/../etc",
    "/.",
    "/..",
    "/a/..",
    "/tmp",
    "-rf",
    "~/a/-rf",
    "~/a//b",
    "~/henicos\n",
    "/etc",
    "/etc/ssh",
    "/usr",
    "/usr/local",
    "/root",
    "/root/ws",
    "/var/log/ws",
    "/var/lib/dpkg/ws",
    "/var/lib/jobrunner",
    "/opt",
    "/opt/henicos",
    "/srv",
    "/srv/henicos",
    "/home/ubuntu/ws",
    "/home/jobrunner",
    "//",
    "//etc/ssh",
    "~/.ssh",
    "~/.gnupg",
    "~/sub/.hidden",
    "/opt/.hidden/ws",
    "/home/jobrunner/.ssh",
    "~/henicos\u0085",
  ]) {
    assert.throws(
      () => validateProfile({ ...sampleRaw, workspace: badWorkspace }),
      `rejects unsafe workspace ${JSON.stringify(badWorkspace)}`,
    );
  }
  for (const goodWorkspace of [
    "~/henicos",
    "~/sub/henicos",
    "~/my..repo",
    "/srv/jobrunner/henicos",
    "/opt/jobrunner/henicos",
    "/home/jobrunner/henicos",
    "/var/lib/jobrunner/henicos",
  ]) {
    assert.strictEqual(validateProfile({ ...sampleRaw, workspace: goodWorkspace }).workspace, goodWorkspace);
  }

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
    for (const badCommentKey of [
      "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITestKey \u0085\u0085ssh-rsa AAAAB3NzaC1yc2E",
      "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITestKey test\u0085\u0085",
      "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITestKey test\u2028extra",
      "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITestKey test\x7f",
    ]) {
      assert.throws(
        () => resolvePublicKey({ ...profile, sshPublicKey: badCommentKey }),
        `rejects pubkey with control/NEL chars: ${JSON.stringify(badCommentKey)}`,
      );
    }
    assert.throws(() =>
      buildAutoinstallUserData(
        profile,
        "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITestKey \u0085\u0085ssh-rsa AAAAB3NzaC1yc2E",
      ),
    );
    assert.throws(() =>
      buildHardeningShellScript(
        profile,
        "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITestKey \u0085\u0085ssh-rsa AAAAB3NzaC1yc2E",
        false,
      ),
    );
    fs.writeFileSync(keyFile, `${fakeKey}\nssh-rsa AAAAB3NzaC1yc2EAAAAAEVIL\n`, "utf8");
    assert.throws(() => resolvePublicKey({ ...profile, sshPublicKey: "", sshPublicKeyPath: keyFile }));

    assert.throws(() => buildHardeningShellScript({ ...profile, workspace: "/etc/ssh" }, fakeKey, false));
    assert.throws(() => buildHardeningShellScript({ ...profile, workspace: "~/.ssh" }, fakeKey, false));
    assert.throws(() => buildHardeningShellScript({ ...profile, cacheDirs: [".ssh"] }, fakeKey, true));
    assert.throws(() => buildHardeningShellScript({ ...profile, cacheDirs: ["."] }, fakeKey, true));
    assert.throws(() =>
      buildHardeningShellScript({ ...profile, blockAllEgress: false, blockedLanCidrs: [] }, fakeKey, false),
    );
    assert.throws(() => buildAutoinstallUserData({ ...profile, packages: ["ufw-"] }, fakeKey));

    const sshd = buildSshdMatchConfig(profile);
    assert.ok(sshd.includes("Match User jobrunner"));
    assert.ok(sshd.includes("AuthorizedKeysFile /etc/ssh/authorized_keys/%u"));
    assert.ok(sshd.includes("AllowTcpForwarding no"));
    assert.ok(sshd.includes("AllowAgentForwarding no"));
    assert.ok(sshd.trimEnd().endsWith("Match all"), "terminates Match User block with Match all");

    const script = buildHardeningShellScript(profile, fakeKey, true);
    assert.ok(script.includes('chown root:root "/etc/ssh/authorized_keys/$RUNNER_USER"'));
    assert.ok(script.includes('chown -h "$RUNNER_USER:$RUNNER_USER" "$dir"'));
    assert.ok(script.includes('chown -h "$RUNNER_USER:$RUNNER_USER" "/home/$RUNNER_USER/henicos"'));
    assert.ok(script.includes('chown -hR "$RUNNER_USER:$RUNNER_USER" "/home/$RUNNER_USER/henicos"'));
    assert.ok(script.includes('chown -hR "$RUNNER_USER:$RUNNER_USER" "/home/$RUNNER_USER/.nuget/packages"'));
    assert.ok(script.includes('[ -L "$target" ]'));
    assert.ok(script.includes("--uid-owner ${RUNNER_UID} -j REJECT --reject-with icmp-port-unreachable"));
    assert.ok(script.includes("ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -j REJECT"));
    assert.ok(script.includes("BEGIN GSHARP-JOBRUNNER-ISOLATION"));
    assert.ok(script.includes("/home/$RUNNER_USER/henicos"));
    assert.ok(
      script.includes(`: > "/home/$RUNNER_USER/henicos/${RemoteWorkspaceMarkerFile}"`) &&
        script.includes(
          `chown -h "$RUNNER_USER:$RUNNER_USER" "/home/$RUNNER_USER/henicos/${RemoteWorkspaceMarkerFile}"`,
        ),
      "creates and chowns workspace marker file",
    );
    assert.ok(script.includes("/home/$RUNNER_USER/.nuget/packages"));
    assert.ok(
      script.lastIndexOf('rm -rf "/home/$RUNNER_USER/.ssh"') > script.indexOf("/home/$RUNNER_USER/.nuget/packages"),
      "removes runner .ssh after cache migration",
    );
    assert.ok(script.includes("for grp in sudo adm systemd-journal docker lxd disk shadow; do"));
    assert.ok(
      script.includes("EXISTING_UID") &&
        script.includes("-lt 1000") &&
        script.includes("-ge 65534") &&
        script.includes("EXISTING_HOME"),
      "rejects pre-existing system or non-standard runner accounts",
    );
    const sshdSectionStart = script.indexOf("# 3. Lock down sshd for runner user");
    const reloadIdx = script.indexOf("systemctl reload ssh");
    const sshdSection = script.slice(sshdSectionStart, reloadIdx);
    assert.ok(
      sshdSection.includes("mktemp") &&
        sshdSection.includes("if ! sshd -t; then") &&
        sshdSection.includes("mv -f") &&
        sshdSection.includes("rm -f") &&
        sshdSection.includes("exit 1"),
      "rolls back 90-jobrunner.conf when sshd -t fails",
    );
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

    const bashBin = gitBashPath();
    const canRunBash =
      (process.platform !== "win32" || bashBin !== "bash") &&
      spawnSync(bashBin, ["-c", "command -v dirname mktemp cp mv rm chmod install >/dev/null"], {
        windowsHide: true,
      }).status === 0;
    if (canRunBash) {
      const syntaxRes = spawnSync(bashBin, ["-n"], { input: script, encoding: "utf8", windowsHide: true });
      assert.strictEqual(syntaxRes.status, 0, syntaxRes.stderr);

      // Item 3 runtime test: symlink rejection & chown -h behavior
      const symHelpersStart = script.indexOf("reject_if_symlink()");
      const symHelpersEnd = script.indexOf("# 1. Create dedicated unprivileged runner user");
      assert.ok(symHelpersStart >= 0 && symHelpersEnd > symHelpersStart);
      const symHelpers = script.slice(symHelpersStart, symHelpersEnd);

      const symBase = path.join(tmpRoot, "sym-probe");
      const symHome = path.join(symBase, "home");
      const symOutside = path.join(symBase, "outside");
      const symRealSub = path.join(symHome, "real_sub");
      const symLinkSub = path.join(symHome, "link_sub");
      fs.mkdirSync(symRealSub, { recursive: true });
      fs.mkdirSync(symOutside, { recursive: true });
      fs.symlinkSync(symOutside, symLinkSub, process.platform === "win32" ? "junction" : "dir");
      const symHomePosix = symHome.replace(/\\/g, "/");
      const symPrelude = [
        "set -euo pipefail",
        'RUNNER_USER="jobrunner"',
        "chown() {",
        '  echo "CHOWN:$*"',
        "}",
        symHelpers.replace('local home_dir="/home/$RUNNER_USER"', `local home_dir="${symHomePosix}"`),
      ].join("\n");

      const badParentRes = spawnSync(
        bashBin,
        ["-c", `${symPrelude}\nreject_symlink_parents "${symHomePosix}/link_sub/ws" "${symHomePosix}"`],
        {
          encoding: "utf8",
          windowsHide: true,
        },
      );
      assert.notStrictEqual(badParentRes.status, 0, "reject_symlink_parents fails on symlink ancestor");
      assert.ok(!badParentRes.stdout.includes("CHOWN:"), "does not invoke chown on symlink ancestor");

      const badChownRes = spawnSync(bashBin, ["-c", `${symPrelude}\nchown_runner_parents "${symHomePosix}/link_sub"`], {
        encoding: "utf8",
        windowsHide: true,
      });
      assert.notStrictEqual(badChownRes.status, 0, "chown_runner_parents refuses symlink dir");
      assert.ok(!badChownRes.stdout.includes("CHOWN:"), "does not invoke chown on symlink dir");

      const goodChownRes = spawnSync(
        bashBin,
        ["-c", `${symPrelude}\nchown_runner_parents "${symHomePosix}/real_sub"`],
        {
          encoding: "utf8",
          windowsHide: true,
        },
      );
      assert.strictEqual(goodChownRes.status, 0, goodChownRes.stderr);
      assert.ok(goodChownRes.stdout.includes("CHOWN:-h jobrunner:jobrunner "), "uses chown -h on real dir");

      // Item 4 runtime test: sshd -t rollback on failure (prior file restored, absent file deleted, pass writes config)
      const sshdBlockStart = script.indexOf("# 3. Lock down sshd for runner user");
      const sshdBlockEnd = script.indexOf("# 4. Configure nproc fork-bomb guard");
      assert.ok(sshdBlockStart >= 0 && sshdBlockEnd > sshdBlockStart);
      const sshdDir = path.join(tmpRoot, "sshd-test");
      fs.mkdirSync(sshdDir, { recursive: true });
      const sshdDirPosix = sshdDir.replace(/\\/g, "/");
      const sshdBlock = script.slice(sshdBlockStart, sshdBlockEnd).replaceAll("/etc/ssh/sshd_config.d", sshdDirPosix);
      const sshdConfFile = path.join(sshdDir, "90-jobrunner.conf");

      const runSshdBlock = (sshdExit: number): { status: number | null; stdout: string } => {
        const harness = [
          "set -euo pipefail",
          `sshd() { return ${sshdExit}; }`,
          'systemctl() { echo "SYSTEMCTL:$*"; }',
          sshdBlock,
        ].join("\n");
        const res = spawnSync(bashBin, ["-c", harness], { encoding: "utf8", windowsHide: true });
        return { status: res.status, stdout: res.stdout };
      };

      fs.writeFileSync(sshdConfFile, "# prior\n", "utf8");
      const failExisting = runSshdBlock(255);
      assert.notStrictEqual(failExisting.status, 0, "sshd -t failure exits non-zero");
      assert.strictEqual(fs.readFileSync(sshdConfFile, "utf8"), "# prior\n", "restores prior 90-jobrunner.conf");
      assert.ok(!failExisting.stdout.includes("SYSTEMCTL:"), "does not reload sshd on failure");

      fs.unlinkSync(sshdConfFile);
      const failAbsent = runSshdBlock(255);
      assert.notStrictEqual(failAbsent.status, 0, "sshd -t failure exits non-zero when no prior file");
      assert.ok(!fs.existsSync(sshdConfFile), "removes broken 90-jobrunner.conf when absent previously");
      assert.ok(!failAbsent.stdout.includes("SYSTEMCTL:"), "does not reload sshd on failure");

      const passSshd = runSshdBlock(0);
      assert.strictEqual(passSshd.status, 0, "sshd -t pass exits 0");
      assert.ok(fs.readFileSync(sshdConfFile, "utf8").includes("Match User jobrunner"), "writes Match User config");
      assert.ok(passSshd.stdout.includes("SYSTEMCTL:reload ssh"), "reloads sshd on pass");
    }

    const metaData = buildAutoinstallMetaData(profile);
    assert.ok(metaData.includes('local-hostname: "ubuntu-server"'));
    assert.ok(metaData.includes('instance-id: "job-queue-worker-ubuntu-server"'));

    const yamlEdgeProfile = validateProfile({
      ...sampleRaw,
      hostname: "no",
      adminUser: "null",
      timezone: "123",
    });
    const yamlEdgeUserData = buildAutoinstallUserData(yamlEdgeProfile, fakeKey);
    assert.ok(yamlEdgeUserData.includes('  timezone: "123"'));
    assert.ok(yamlEdgeUserData.includes('    hostname: "no"'));
    assert.ok(yamlEdgeUserData.includes('    username: "null"'));
    const yamlEdgeMetaData = buildAutoinstallMetaData(yamlEdgeProfile);
    assert.ok(yamlEdgeMetaData.includes('instance-id: "job-queue-worker-no"'));
    assert.ok(yamlEdgeMetaData.includes('local-hostname: "no"'));

    const noDnsEmptyUserData = buildAutoinstallUserData(
      validateProfile({ ...sampleRaw, network: { ...sampleNet, nameservers: [] } }),
      fakeKey,
    );
    assert.ok(!noDnsEmptyUserData.includes("nameservers:"));
    assert.ok(!noDnsEmptyUserData.includes("1.1.1.1"));

    const netNoNameservers = { ...sampleNet };
    delete netNoNameservers.nameservers;
    const noDnsUndefinedUserData = buildAutoinstallUserData(
      validateProfile({ ...sampleRaw, network: netNoNameservers }),
      fakeKey,
    );
    assert.ok(!noDnsUndefinedUserData.includes("nameservers:"));
    assert.ok(!noDnsUndefinedUserData.includes("1.1.1.1"));

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
