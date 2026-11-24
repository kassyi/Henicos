// Target Generator: Ubuntu 24.04 Subiquity Autoinstall `user-data` & `meta-data` from `WorkerProfile`.

import { buildHardeningShellScript } from "./hardening.ts";
import type { WorkerProfile } from "./profile.ts";

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
  const net = profile.network;
  const nameservers =
    net.dhcp4 === false && net.nameservers && net.nameservers.length > 0 ? net.nameservers : ["1.1.1.1"];
  const nameserverItems = nameservers.map((ns) => JSON.stringify(ns)).join(", ");
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
          "        nameservers:",
          `          addresses: [${nameserverItems}]`,
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
    `  timezone: ${profile.timezone}`,
    "  keyboard:",
    "    layout: us",
    "  storage:",
    "    layout:",
    "      name: direct",
    networkBlock,
    "  identity:",
    `    hostname: ${profile.hostname}`,
    `    username: ${profile.adminUser}`,
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
  return `instance-id: job-queue-worker-${profile.hostname}\nlocal-hostname: ${profile.hostname}\n`;
}
