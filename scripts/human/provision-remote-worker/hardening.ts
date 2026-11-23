// Target Generator: idempotent `jobrunner` user isolation, sshd Match User hardening, nproc limits, and UFW egress block.

import * as path from "node:path";
import type { WorkerProfile } from "./profile.ts";

export function buildSshdMatchConfig(profile: WorkerProfile): string {
  return [
    "# Managed by provision-remote-worker.js",
    `AllowUsers ${profile.adminUser} ${profile.runnerUser}`,
    `Match User ${profile.runnerUser}`,
    "    AuthorizedKeysFile /etc/ssh/authorized_keys/%u",
    "    PasswordAuthentication no",
    "    KbdInteractiveAuthentication no",
    "    AllowTcpForwarding no",
    "    AllowAgentForwarding no",
    "    AllowStreamLocalForwarding no",
    "    X11Forwarding no",
    "    PermitTunnel no",
    "",
  ].join("\n");
}

export function buildLimitsConfig(profile: WorkerProfile): string {
  return [
    "# Managed by provision-remote-worker.js",
    `${profile.runnerUser} soft nproc ${profile.maxProcesses}`,
    `${profile.runnerUser} hard nproc ${profile.maxProcesses}`,
    "",
  ].join("\n");
}

/**
 * Generates an idempotent bash script (run as root via sudo or Subiquity late-commands) that creates the unprivileged
 * `runnerUser`, pins its root-owned authorized_keys & sshd Match User block, and installs UID-scoped egress blocking.
 */
export function buildHardeningShellScript(profile: WorkerProfile, pubKey: string, migrateCache: boolean): string {
  const sshdConf = buildSshdMatchConfig(profile);
  const limitsConf = buildLimitsConfig(profile);
  const ipv4RejectLines = profile.blockAllEgress
    ? '  echo "-A ufw-before-output -m owner --uid-owner ${RUNNER_UID} -j REJECT --reject-with icmp-port-unreachable"'
    : profile.blockedLanCidrs
        .map(
          (cidr) =>
            `  echo "-A ufw-before-output -m owner --uid-owner \${RUNNER_UID} -d ${cidr} -j REJECT --reject-with icmp-port-unreachable"`,
        )
        .join("\n");
  const ipv6RejectLines = profile.blockAllEgress
    ? '  echo "-A ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -j REJECT --reject-with icmp6-port-unreachable"'
    : [
        '  echo "-A ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -d fc00::/7 -j REJECT --reject-with icmp6-port-unreachable"',
        '  echo "-A ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -d fe80::/10 -j REJECT --reject-with icmp6-port-unreachable"',
      ].join("\n");

  const workspaceRel = profile.workspace.startsWith("~/") ? profile.workspace.slice(2).replace(/\/+$/, "") : "";
  const runnerWorkspaceExpr = workspaceRel !== "" ? `/home/$RUNNER_USER/${workspaceRel}` : profile.workspace;
  const adminWorkspaceExpr = workspaceRel !== "" ? `/home/$ADMIN_USER/${workspaceRel}` : "";
  const migrateCacheLines: string[] = [];
  if (migrateCache) {
    if (adminWorkspaceExpr !== "") {
      migrateCacheLines.push(
        "# 6. Migrate existing warm build workspace from admin user if runner workspace does not exist yet",
        `if [ ! -d "${runnerWorkspaceExpr}" ] && [ -d "${adminWorkspaceExpr}" ]; then`,
        `  cp -a "${adminWorkspaceExpr}" "${runnerWorkspaceExpr}"`,
        `  rm -rf "${runnerWorkspaceExpr}/.claude/worktrees" "${runnerWorkspaceExpr}/.job-queue-manifest.digest" "${runnerWorkspaceExpr}/.job-queue-manifest.sha256"`,
        `  chown -R "$RUNNER_USER:$RUNNER_USER" "${runnerWorkspaceExpr}"`,
        "fi",
      );
    }
    for (const cacheDir of profile.cacheDirs) {
      const parentRel = path.posix.dirname(cacheDir);
      const runnerParent = parentRel !== "." ? `/home/$RUNNER_USER/${parentRel}` : "/home/$RUNNER_USER";
      migrateCacheLines.push(
        `if [ ! -d "/home/$RUNNER_USER/${cacheDir}" ] && [ -d "/home/$ADMIN_USER/${cacheDir}" ]; then`,
        `  mkdir -p "${runnerParent}"`,
        `  cp -a "/home/$ADMIN_USER/${cacheDir}" "/home/$RUNNER_USER/${cacheDir}"`,
        `  chown -R "$RUNNER_USER:$RUNNER_USER" "${runnerParent}"`,
        "fi",
      );
    }
  }

  return [
    "#!/usr/bin/env bash",
    "set -euo pipefail",
    "",
    `ADMIN_USER=${JSON.stringify(profile.adminUser)}`,
    `RUNNER_USER=${JSON.stringify(profile.runnerUser)}`,
    "",
    '# 1. Create dedicated unprivileged runner user (no sudo, password disabled with "*" so pubkey SSH works, 0750 home)',
    'if ! id -u "$RUNNER_USER" >/dev/null 2>&1; then',
    '  useradd -m -s /bin/bash "$RUNNER_USER"',
    "fi",
    'usermod -U "$RUNNER_USER" >/dev/null 2>&1 || true',
    'usermod -p "*" "$RUNNER_USER"',
    "for grp in sudo adm systemd-journal docker lxd; do",
    '  if id -nG "$RUNNER_USER" | tr " " "\\n" | grep -qx "$grp"; then',
    '    gpasswd -d "$RUNNER_USER" "$grp" >/dev/null 2>&1 || true',
    "  fi",
    "done",
    'chmod 0750 "/home/$RUNNER_USER"',
    'if [ -d "/home/$ADMIN_USER" ]; then chmod 0750 "/home/$ADMIN_USER"; fi',
    "",
    "# 2. Install root-owned authorized_keys (runner cannot modify its own SSH keys)",
    "install -d -m 0755 -o root -g root /etc/ssh/authorized_keys",
    "cat <<'__GSHARP_PUBKEY__' > \"/etc/ssh/authorized_keys/$RUNNER_USER\"",
    pubKey,
    "__GSHARP_PUBKEY__",
    'chown root:root "/etc/ssh/authorized_keys/$RUNNER_USER"',
    'chmod 0644 "/etc/ssh/authorized_keys/$RUNNER_USER"',
    'rm -rf "/home/$RUNNER_USER/.ssh"',
    "",
    "# 3. Lock down sshd for runner user",
    "install -d -m 0755 /etc/ssh/sshd_config.d",
    "cat <<'__GSHARP_SSHD__' > /etc/ssh/sshd_config.d/90-jobrunner.conf",
    sshdConf.trimEnd(),
    "__GSHARP_SSHD__",
    "chmod 0644 /etc/ssh/sshd_config.d/90-jobrunner.conf",
    "sshd -t",
    "systemctl reload ssh 2>/dev/null || systemctl reload sshd 2>/dev/null || true",
    "",
    "# 4. Configure nproc fork-bomb guard",
    "cat <<'__GSHARP_LIMITS__' > /etc/security/limits.d/90-jobrunner.conf",
    limitsConf.trimEnd(),
    "__GSHARP_LIMITS__",
    "chmod 0644 /etc/security/limits.d/90-jobrunner.conf",
    "",
    "# 5. Block runner UID outbound traffic (IPv4 & IPv6) via UFW before.rules / before6.rules",
    'RUNNER_UID=$(id -u "$RUNNER_USER")',
    "patch_ufw_rules() {",
    '  local rules_path="$1"',
    '  local block_path="$2"',
    '  [ -f "$rules_path" ] || return 0',
    '  python3 - "$rules_path" "$block_path" <<\'__GSHARP_PY__\'',
    "import sys",
    "rules_path, block_path = sys.argv[1], sys.argv[2]",
    'text = open(rules_path, "r", encoding="utf-8").read()',
    'block = open(block_path, "r", encoding="utf-8").read().strip() + "\\n"',
    'start_marker = "# BEGIN GSHARP-JOBRUNNER-ISOLATION"',
    'end_marker = "# END GSHARP-JOBRUNNER-ISOLATION"',
    "if start_marker in text and end_marker in text:",
    "    a = text.index(start_marker)",
    "    b = text.index(end_marker) + len(end_marker)",
    '    if b < len(text) and text[b] == "\\n":',
    "        b += 1",
    "    text = text[:a] + text[b:]",
    'idx = text.rfind("\\nCOMMIT")',
    "if idx < 0:",
    '    raise SystemExit("COMMIT marker not found in " + rules_path)',
    'new_text = text[:idx + 1] + "\\n" + block + text[idx + 1:]',
    'open(rules_path, "w", encoding="utf-8").write(new_text)',
    "__GSHARP_PY__",
    "}",
    "if [ -f /etc/ufw/before.rules ]; then",
    "  TMP_V4=$(mktemp)",
    "  {",
    '    echo "# BEGIN GSHARP-JOBRUNNER-ISOLATION"',
    '    echo "-A ufw-before-output -m owner --uid-owner ${RUNNER_UID} -o lo -j ACCEPT"',
    '    echo "-A ufw-before-output -m owner --uid-owner ${RUNNER_UID} -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT"',
    ipv4RejectLines,
    '    echo "# END GSHARP-JOBRUNNER-ISOLATION"',
    '  } > "$TMP_V4"',
    '  patch_ufw_rules /etc/ufw/before.rules "$TMP_V4"',
    '  rm -f "$TMP_V4"',
    "fi",
    "if [ -f /etc/ufw/before6.rules ]; then",
    "  TMP_V6=$(mktemp)",
    "  {",
    '    echo "# BEGIN GSHARP-JOBRUNNER-ISOLATION"',
    '    echo "-A ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -o lo -j ACCEPT"',
    '    echo "-A ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT"',
    ipv6RejectLines,
    '    echo "# END GSHARP-JOBRUNNER-ISOLATION"',
    '  } > "$TMP_V6"',
    '  patch_ufw_rules /etc/ufw/before6.rules "$TMP_V6"',
    '  rm -f "$TMP_V6"',
    "fi",
    "ufw limit 22/tcp >/dev/null 2>&1 || true",
    "ufw --force enable >/dev/null 2>&1 || true",
    "ufw reload >/dev/null 2>&1 || true",
    "",
    ...migrateCacheLines,
    `mkdir -p "${runnerWorkspaceExpr}"`,
    `chown "$RUNNER_USER:$RUNNER_USER" "${runnerWorkspaceExpr}"`,
    'echo "PROVISION_OK runner=$RUNNER_USER uid=$RUNNER_UID"',
    "",
  ].join("\n");
}
