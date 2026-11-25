// Target Generator: Ubuntu 24.04 Subiquity Autoinstall `user-data` & `meta-data` from `WorkerProfile`.

import { buildHardeningShellScript } from "./hardening.ts";
import { isValidPackageName, type WorkerProfile } from "./profile.ts";

const BaseAutoinstallPackages = [
  "openssh-server",
  "ufw",
  "avahi-daemon",
  "git",
  "curl",
  "rsync",
  "ca-certificates",
  "psmisc",
  "python3",
] as const;

export function buildAutoinstallUserData(profile: WorkerProfile, pubKey: string): string {
  if (typeof pubKey !== "string" || !/^[\x20-\x7E\t]+$/.test(pubKey)) {
    throw new Error("SSH public key contains unsupported or control characters");
  }
  const net = profile.network;
  const nameserverLines =
    net.dhcp4 === false && net.nameservers !== undefined && net.nameservers.length > 0
      ? ["        nameservers:", `          addresses: [${net.nameservers.map((ns) => JSON.stringify(ns)).join(", ")}]`]
      : [];
  const networkBlock =
    net.dhcp4 === false
      ? [
          "  network:",
          "    version: 2",
          "    ethernets:",
          "      id0:",
          "        match:",
          '          name: "e*"',
          "        dhcp4: false",
          `        addresses: [${JSON.stringify(net.address)}]`,
          "        routes:",
          "          - to: default",
          `            via: ${JSON.stringify(net.gateway4)}`,
          ...nameserverLines,
        ].join("\n")
      : [
          "  network:",
          "    version: 2",
          "    ethernets:",
          "      id0:",
          "        match:",
          '          name: "e*"',
          "        dhcp4: true",
        ].join("\n");

  const hardeningB64 = Buffer.from(buildHardeningShellScript(profile, pubKey, false), "utf8").toString("base64");
  const allPackages = [...new Set([...BaseAutoinstallPackages, ...profile.packages])];
  for (const pkg of allPackages) {
    if (!isValidPackageName(pkg)) {
      throw new Error(`invalid packages entry: ${pkg}`);
    }
  }
  const packageLines = allPackages.map((pkg) => `    - ${pkg}`);
  const setupLines = profile.setupCommands.map((cmd) => {
    const cmdB64 = Buffer.from(cmd, "utf8").toString("base64");
    const curtinCmd = `curtin in-target -- bash -c 'echo ${cmdB64} | base64 -d | bash -euo pipefail'`;
    return `    - ${JSON.stringify(curtinCmd)}`;
  });
  const hardenPath = "/usr/local/sbin/jobrunner-harden-worker.sh";
  const hardenCurtinCmd =
    `curtin in-target -- bash -c 'echo ${hardeningB64} | base64 -d > ${hardenPath} && ` +
    `chmod 0700 ${hardenPath} && ${hardenPath}'`;

  return [
    "#cloud-config",
    "autoinstall:",
    "  version: 1",
    "  locale: en_US.UTF-8",
    `  timezone: ${JSON.stringify(profile.timezone)}`,
    "  keyboard:",
    "    layout: us",
    "  storage:",
    "    layout:",
    "      name: direct",
    networkBlock,
    "  identity:",
    `    hostname: ${JSON.stringify(profile.hostname)}`,
    `    username: ${JSON.stringify(profile.adminUser)}`,
    '    # Password disabled ("*"): SSH key login works, password login impossible',
    '    password: "*"',
    "  ssh:",
    "    install-server: true",
    "    allow-pw: false",
    "    authorized-keys:",
    `      - ${JSON.stringify(pubKey)}`,
    "  packages:",
    ...packageLines,
    "  late-commands:",
    ...setupLines,
    `    - ${JSON.stringify(hardenCurtinCmd)}`,
    "",
  ].join("\n");
}

export function buildAutoinstallMetaData(profile: WorkerProfile): string {
  return (
    `instance-id: ${JSON.stringify(`job-queue-worker-${profile.hostname}`)}\n` +
    `local-hostname: ${JSON.stringify(profile.hostname)}\n`
  );
}
