// Target Generator: idempotent `jobrunner` user isolation, sshd Match User hardening,
// nproc limits, and UID-scoped UFW egress block.

import * as path from "node:path";
import {
  isValidRelativeCacheDir,
  isValidRemoteWorkspace,
  RemoteWorkspaceMarkerFile,
} from "../job-queue/remote/spec.ts";
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
    "Match all",
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
  if (typeof pubKey !== "string" || !/^[\x20-\x7E\t]+$/.test(pubKey)) {
    throw new Error("SSH public key contains unsupported or control characters");
  }
  if (!isValidRemoteWorkspace(profile.workspace, { provisionerRunnerUser: profile.runnerUser })) {
    throw new Error(`unsafe workspace for hardening script: ${profile.workspace}`);
  }
  for (const cacheDir of profile.cacheDirs) {
    if (!isValidRelativeCacheDir(cacheDir)) {
      throw new Error(`unsafe cacheDirs entry for hardening script: ${cacheDir}`);
    }
  }
  if (!profile.blockAllEgress && (!Array.isArray(profile.blockedLanCidrs) || profile.blockedLanCidrs.length === 0)) {
    throw new Error("blockedLanCidrs must not be empty when blockAllEgress is false");
  }
  const sshdConf = buildSshdMatchConfig(profile);
  const limitsConf = buildLimitsConfig(profile);
  const ipv4RejectLines = profile.blockAllEgress
    ? '  echo "-A ufw-before-output -m owner --uid-owner ${RUNNER_UID} -j REJECT --reject-with icmp-port-unreachable"'
    : profile.blockedLanCidrs
        .map(
          (cidr) =>
            `  echo "-A ufw-before-output -m owner --uid-owner \${RUNNER_UID} -d ${cidr} ` +
            '-j REJECT --reject-with icmp-port-unreachable"',
        )
        .join("\n");
  const ipv6RejectLines = profile.blockAllEgress
    ? '  echo "-A ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -j REJECT --reject-with icmp6-port-unreachable"'
    : [
        '  echo "-A ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -d fc00::/7 ' +
          '-j REJECT --reject-with icmp6-port-unreachable"',
        '  echo "-A ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -d fe80::/10 ' +
          '-j REJECT --reject-with icmp6-port-unreachable"',
      ].join("\n");

  const workspaceRel = profile.workspace.startsWith("~/") ? profile.workspace.slice(2).replace(/\/+$/, "") : "";
  const absWorkspace = profile.workspace.replace(/\/+$/, "");
  const runnerWorkspaceExpr = workspaceRel !== "" ? `/home/$RUNNER_USER/${workspaceRel}` : absWorkspace;
  const adminWorkspaceExpr = workspaceRel !== "" ? `/home/$ADMIN_USER/${workspaceRel}` : "";
  const runnerStopParent = workspaceRel !== "" ? '"/home/$RUNNER_USER"' : '""';
  const migrateCacheLines: string[] = [];
  if (migrateCache) {
    if (adminWorkspaceExpr !== "") {
      migrateCacheLines.push(
        "# 6. Migrate existing warm build workspace from admin user if runner workspace does not exist yet",
        `if [ ! -d "${runnerWorkspaceExpr}" ] && [ -d "${adminWorkspaceExpr}" ]; then`,
        `  reject_symlink_parents "${runnerWorkspaceExpr}" ${runnerStopParent}`,
        `  reject_if_symlink "${adminWorkspaceExpr}"`,
        `  mkdir -p "$(dirname "${runnerWorkspaceExpr}")"`,
        `  chown_runner_parents "$(dirname "${runnerWorkspaceExpr}")"`,
        `  cp -a "${adminWorkspaceExpr}" "${runnerWorkspaceExpr}"`,
        `  rm -rf "${runnerWorkspaceExpr}/.claude/worktrees"`,
        `  rm -rf "${runnerWorkspaceExpr}/.job-queue-manifest.digest"`,
        `  rm -rf "${runnerWorkspaceExpr}/.job-queue-manifest.sha256"`,
        `  reject_if_symlink "${runnerWorkspaceExpr}"`,
        `  chown -hR "$RUNNER_USER:$RUNNER_USER" "${runnerWorkspaceExpr}"`,
        "fi",
      );
    }
    for (const cacheDir of profile.cacheDirs) {
      const parentRel = path.posix.dirname(cacheDir);
      const runnerParent = parentRel !== "." ? `/home/$RUNNER_USER/${parentRel}` : "/home/$RUNNER_USER";
      migrateCacheLines.push(
        `if [ ! -d "/home/$RUNNER_USER/${cacheDir}" ] && [ -d "/home/$ADMIN_USER/${cacheDir}" ]; then`,
        `  reject_symlink_parents "/home/$RUNNER_USER/${cacheDir}" "/home/$RUNNER_USER"`,
        `  reject_if_symlink "/home/$ADMIN_USER/${cacheDir}"`,
        `  mkdir -p "${runnerParent}"`,
        `  chown_runner_parents "${runnerParent}"`,
        `  cp -a "/home/$ADMIN_USER/${cacheDir}" "/home/$RUNNER_USER/${cacheDir}"`,
        `  reject_if_symlink "/home/$RUNNER_USER/${cacheDir}"`,
        `  chown -hR "$RUNNER_USER:$RUNNER_USER" "/home/$RUNNER_USER/${cacheDir}"`,
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
    "reject_if_symlink() {",
    '  local target="$1"',
    '  if [ -L "$target" ]; then',
    '    echo "ERROR: refusing symlink path: $target" >&2',
    "    exit 1",
    "  fi",
    "}",
    "",
    "reject_symlink_parents() {",
    '  local cur="$1"',
    '  local stop="${2:-}"',
    '  while [ "$cur" != "$stop" ] && [ "$cur" != "/" ] && [ "$cur" != "." ]; do',
    '    reject_if_symlink "$cur"',
    '    cur=$(dirname "$cur")',
    "  done",
    '  if [ -n "$stop" ]; then',
    '    reject_if_symlink "$stop"',
    "  fi",
    "}",
    "",
    "chown_runner_parents() {",
    '  local dir="$1"',
    '  local home_dir="/home/$RUNNER_USER"',
    '  while [ "$dir" != "$home_dir" ] && [ "$dir" != "/" ] && [ "$dir" != "." ]; do',
    '    case "$dir" in',
    '      "$home_dir"/*)',
    '        reject_if_symlink "$dir"',
    '        chown -h "$RUNNER_USER:$RUNNER_USER" "$dir"',
    "        ;;",
    "      *) break ;;",
    "    esac",
    '    dir=$(dirname "$dir")',
    "  done",
    "}",
    "",
    "# 1. Create dedicated unprivileged runner user",
    '# (no sudo, password disabled with "*" so pubkey SSH works, 0750 home)',
    'if ! id -u "$RUNNER_USER" >/dev/null 2>&1; then',
    '  useradd -m -s /bin/bash "$RUNNER_USER"',
    "else",
    '  EXISTING_UID=$(id -u "$RUNNER_USER")',
    '  EXISTING_HOME=$(getent passwd "$RUNNER_USER" | cut -d: -f6)',
    '  if [ "$EXISTING_UID" -lt 1000 ] || [ "$EXISTING_UID" -ge 65534 ] || [ "$EXISTING_HOME" != "/home/$RUNNER_USER" ]; then',
    '    echo "ERROR: refusing pre-existing system or non-standard user $RUNNER_USER (uid=$EXISTING_UID home=$EXISTING_HOME)" >&2',
    "    exit 1",
    "  fi",
    "fi",
    'usermod -U "$RUNNER_USER" >/dev/null 2>&1 || true',
    'usermod -p "*" "$RUNNER_USER"',
    "for grp in sudo adm systemd-journal docker lxd disk shadow; do",
    '  if id -nG "$RUNNER_USER" | tr " " "\\n" | grep -qx "$grp"; then',
    '    gpasswd -d "$RUNNER_USER" "$grp" >/dev/null 2>&1 || true',
    "  fi",
    "done",
    'reject_if_symlink "/home/$RUNNER_USER"',
    'chmod 0750 "/home/$RUNNER_USER"',
    'if [ -d "/home/$ADMIN_USER" ]; then',
    '  reject_if_symlink "/home/$ADMIN_USER"',
    '  chmod 0750 "/home/$ADMIN_USER"',
    "fi",
    "",
    "# 2. Install root-owned authorized_keys (runner cannot modify its own SSH keys)",
    "install -d -m 0755 -o root -g root /etc/ssh/authorized_keys",
    'reject_if_symlink "/etc/ssh/authorized_keys/$RUNNER_USER"',
    "cat <<'__GSHARP_PUBKEY__' > \"/etc/ssh/authorized_keys/$RUNNER_USER\"",
    pubKey,
    "__GSHARP_PUBKEY__",
    'chown root:root "/etc/ssh/authorized_keys/$RUNNER_USER"',
    'chmod 0644 "/etc/ssh/authorized_keys/$RUNNER_USER"',
    'rm -rf "/home/$RUNNER_USER/.ssh"',
    "",
    "# 3. Lock down sshd for runner user",
    "install -d -m 0755 /etc/ssh/sshd_config.d",
    'SSHD_CONF="/etc/ssh/sshd_config.d/90-jobrunner.conf"',
    'SSHD_BACKUP=""',
    'if [ -e "$SSHD_CONF" ] || [ -L "$SSHD_CONF" ]; then',
    "  SSHD_BACKUP=$(mktemp)",
    '  cp -p "$SSHD_CONF" "$SSHD_BACKUP"',
    "fi",
    "cat <<'__GSHARP_SSHD__' > \"$SSHD_CONF\"",
    sshdConf.trimEnd(),
    "__GSHARP_SSHD__",
    'chmod 0644 "$SSHD_CONF"',
    "if ! sshd -t; then",
    '  echo "ERROR: sshd -t failed; rolling back $SSHD_CONF" >&2',
    '  if [ -n "$SSHD_BACKUP" ]; then',
    '    mv -f "$SSHD_BACKUP" "$SSHD_CONF"',
    "  else",
    '    rm -f "$SSHD_CONF"',
    "  fi",
    "  exit 1",
    "fi",
    'if [ -n "$SSHD_BACKUP" ]; then',
    '  rm -f "$SSHD_BACKUP"',
    "fi",
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
    "if ! command -v ufw >/dev/null 2>&1 || [ ! -f /etc/ufw/before.rules ]; then",
    '  echo "ERROR: ufw and /etc/ufw/before.rules are required for egress isolation" >&2',
    "  exit 1",
    "fi",
    "patch_ufw_rules() {",
    '  local rules_path="$1"',
    '  local block_path="$2"',
    '  python3 - "$rules_path" "$block_path" <<\'__GSHARP_PY__\'',
    "import re",
    "import sys",
    "rules_path, block_path = sys.argv[1], sys.argv[2]",
    'text = open(rules_path, "r", encoding="utf-8").read()',
    'block = open(block_path, "r", encoding="utf-8").read().strip() + "\\n"',
    'start_marker = "# BEGIN GSHARP-JOBRUNNER-ISOLATION"',
    'end_marker = "# END GSHARP-JOBRUNNER-ISOLATION"',
    "a = text.find(start_marker)",
    "if a >= 0:",
    "    end_pos = text.find(end_marker, a)",
    "    if end_pos < 0:",
    '        raise SystemExit("unclosed isolation marker in " + rules_path)',
    "    b = end_pos + len(end_marker)",
    '    if b < len(text) and text[b] == "\\n":',
    "        b += 1",
    '    if a >= 2 and text[a - 2:a] == "\\n\\n":',
    "        a -= 1",
    '    elif a == 1 and text[0] == "\\n":',
    "        a = 0",
    "    text = text[:a] + text[b:]",
    'm_filter = re.search(r"(?m)^[ \\t]*\\*filter[ \\t]*$", text)',
    "if not m_filter:",
    '    raise SystemExit("*filter table not found in " + rules_path)',
    'm_commit = re.search(r"(?m)^[ \\t]*COMMIT[ \\t]*$", text[m_filter.end():])',
    "if not m_commit:",
    '    raise SystemExit("COMMIT marker for *filter not found in " + rules_path)',
    "idx = m_filter.end() + m_commit.start()",
    'if re.search(r"(?m)^[ \\t]*\\*", text[m_filter.end():idx]):',
    '    raise SystemExit("COMMIT missing before next table in " + rules_path)',
    'new_text = text[:idx] + "\\n" + block + text[idx:]',
    'open(rules_path, "w", encoding="utf-8").write(new_text)',
    "__GSHARP_PY__",
    "}",
    "TMP_V4=$(mktemp)",
    "{",
    '  echo "# BEGIN GSHARP-JOBRUNNER-ISOLATION"',
    '  echo "-A ufw-before-output -m owner --uid-owner ${RUNNER_UID} -o lo -j ACCEPT"',
    '  echo "-A ufw-before-output -m owner --uid-owner ${RUNNER_UID} ' +
      '-m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT"',
    ipv4RejectLines,
    '  echo "# END GSHARP-JOBRUNNER-ISOLATION"',
    '} > "$TMP_V4"',
    'patch_ufw_rules /etc/ufw/before.rules "$TMP_V4"',
    'rm -f "$TMP_V4"',
    "if [ -e /proc/net/if_inet6 ] && [ ! -f /etc/ufw/before6.rules ]; then",
    '  echo "ERROR: /etc/ufw/before6.rules is required for IPv6 egress isolation" >&2',
    "  exit 1",
    "fi",
    "if [ -f /etc/ufw/before6.rules ]; then",
    "  TMP_V6=$(mktemp)",
    "  {",
    '    echo "# BEGIN GSHARP-JOBRUNNER-ISOLATION"',
    '    echo "-A ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -o lo -j ACCEPT"',
    '    echo "-A ufw6-before-output -m owner --uid-owner ${RUNNER_UID} ' +
      '-m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT"',
    ipv6RejectLines,
    '    echo "# END GSHARP-JOBRUNNER-ISOLATION"',
    '  } > "$TMP_V6"',
    '  patch_ufw_rules /etc/ufw/before6.rules "$TMP_V6"',
    '  rm -f "$TMP_V6"',
    "fi",
    "if command -v ischroot >/dev/null 2>&1 && ischroot; then",
    "  ufw limit 22/tcp >/dev/null 2>&1 || true",
    "  ufw --force enable >/dev/null 2>&1 || true",
    "  ufw reload >/dev/null 2>&1 || true",
    "else",
    "  ufw limit 22/tcp >/dev/null",
    "  ufw --force enable >/dev/null",
    "  ufw reload >/dev/null",
    "fi",
    "",
    ...migrateCacheLines,
    'rm -rf "/home/$RUNNER_USER/.ssh"',
    `reject_symlink_parents "${runnerWorkspaceExpr}" ${runnerStopParent}`,
    `mkdir -p "${runnerWorkspaceExpr}"`,
    `chown_runner_parents "$(dirname "${runnerWorkspaceExpr}")"`,
    `reject_if_symlink "${runnerWorkspaceExpr}"`,
    `chown -h "$RUNNER_USER:$RUNNER_USER" "${runnerWorkspaceExpr}"`,
    `reject_if_symlink "${runnerWorkspaceExpr}/${RemoteWorkspaceMarkerFile}"`,
    `: > "${runnerWorkspaceExpr}/${RemoteWorkspaceMarkerFile}"`,
    `chown -h "$RUNNER_USER:$RUNNER_USER" "${runnerWorkspaceExpr}/${RemoteWorkspaceMarkerFile}"`,
    'echo "PROVISION_OK runner=$RUNNER_USER uid=$RUNNER_UID"',
    "",
  ].join("\n");
}
