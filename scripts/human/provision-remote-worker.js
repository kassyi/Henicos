#!/usr/bin/env node
'use strict';

// Provisions a locked-down Ubuntu 24.04 remote CPU worker (`jobrunner` user with no sudo, root-owned SSH
// authorized_keys, sshd Match User restrictions, UID-scoped RFC1918 LAN egress block via UFW/iptables) and emits
// Ubuntu 24.04 Autoinstall (`cloud-init` NoCloud) seed files / `cidata.iso` for zero-touch bare-metal or VM
// redeployment. Also writes `<mainCheckout>/tmp/job-queue/remotes.local.json`.
//
// Usage:
//   node scripts/human/provision-remote-worker.js --init-profile [--out <file>]
//   node scripts/human/provision-remote-worker.js --apply [--profile <file>] [--write-remotes] [--no-migrate-cache]
//   node scripts/human/provision-remote-worker.js --emit-autoinstall [--profile <file>] --out <dir> [--build-iso] [--iso-via-ssh]
//   node scripts/human/provision-remote-worker.js --write-remotes [--profile <file>] [--out <file>]
//   node scripts/human/provision-remote-worker.js --self-test
//   node scripts/human/provision-remote-worker.js --help

const assert = require('node:assert');
const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { mainCheckout } = require('./job-queue/lib/main-checkout.js');

const RepoRoot = path.resolve(__dirname, '..', '..');
const ExampleProfilePath = path.join(__dirname, 'job-queue', 'worker-profile.example.json');
const DefaultStateDir = path.join(mainCheckout(__dirname, RepoRoot), 'tmp', 'job-queue');
const DefaultProfilePath = path.join(DefaultStateDir, 'worker-profile.json');
const DefaultRemotesPath = path.join(DefaultStateDir, 'remotes.local.json');

const Usage = [
  'Usage:',
  '  node scripts/human/provision-remote-worker.js --init-profile [--out <file>]',
  '  node scripts/human/provision-remote-worker.js --apply [--profile <file>] [--write-remotes] [--no-migrate-cache]',
  '  node scripts/human/provision-remote-worker.js --emit-autoinstall [--profile <file>] --out <dir> [--build-iso] [--iso-via-ssh]',
  '  node scripts/human/provision-remote-worker.js --write-remotes [--profile <file>] [--out <file>]',
  '  node scripts/human/provision-remote-worker.js --self-test',
  '',
  'Options:',
  '  --init-profile      Copy worker-profile.example.json to tmp/job-queue/worker-profile.json (or --out).',
  '  --apply             Apply idempotent jobrunner isolation + UFW LAN egress block to an existing Ubuntu 24.04 host',
  '                      via interactive `ssh -t <admin>@<host>` (sudo password prompted by remote sshd).',
  '  --emit-autoinstall  Generate Ubuntu 24.04 Subiquity autoinstall user-data & meta-data in --out <dir> for',
  '                      bare-metal or VM (Hyper-V / Proxmox / QEMU / VMware) unattended installation.',
  '  --build-iso         Also pack user-data & meta-data into <out>/cidata.iso (uses xorriso/genisoimage/cloud-localds).',
  '  --iso-via-ssh       When building cidata.iso on Windows without local xorriso, pack it via the configured Ubuntu host.',
  '  --write-remotes     Write/update <mainCheckout>/tmp/job-queue/remotes.local.json from the profile.',
  '  --no-migrate-cache  With --apply, do not copy existing /home/<admin>/henicos cache to /home/<runner>/henicos.',
  '  --profile <file>    Path to worker-profile.json (default: <mainCheckout>/tmp/job-queue/worker-profile.json).',
  '  --out <path>        Output file or directory.',
  '  --self-test         Run built-in unit checks without touching any remote host.',
].join('\n');

const NamePattern = /^[a-z][a-z0-9-]{0,31}$/;
const UserPattern = /^[a-z_][a-z0-9_-]{0,31}$/;
const HostPattern = /^[A-Za-z0-9][A-Za-z0-9._-]*$/;
const CidrPattern = /^(?:\d{1,3}\.){3}\d{1,3}\/\d{1,2}$/;
const PubKeyPattern = /^(?:ssh-ed25519|ssh-rsa|ecdsa-sha2-nistp(?:256|384|521)|sk-ssh-ed25519@openssh\.com)\s+[A-Za-z0-9+/=]+(?:\s+[^\r\n]+)?$/;

function expandHome(filePath) {
  if (filePath === '~') {
    return os.homedir();
  }
  if (filePath.startsWith('~/') || filePath.startsWith('~\\')) {
    return path.join(os.homedir(), filePath.slice(2));
  }
  return filePath;
}

function validateProfile(raw) {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) {
    throw new Error('profile must be a JSON object');
  }
  if (typeof raw.workerName !== 'string' || !NamePattern.test(raw.workerName)) {
    throw new Error('workerName must match /^[a-z][a-z0-9-]{0,31}$/');
  }
  if (typeof raw.hostname !== 'string' || !/^[A-Za-z0-9][A-Za-z0-9-]{0,62}$/.test(raw.hostname)) {
    throw new Error('hostname must be a valid RFC 1123 hostname label');
  }
  if (typeof raw.connectHost !== 'string' || !HostPattern.test(raw.connectHost)) {
    throw new Error('connectHost must be a valid hostname or IPv4 address');
  }
  if (typeof raw.adminUser !== 'string' || !UserPattern.test(raw.adminUser)) {
    throw new Error('adminUser must be a valid Linux username');
  }
  if (typeof raw.runnerUser !== 'string' || !UserPattern.test(raw.runnerUser)) {
    throw new Error('runnerUser must be a valid Linux username');
  }
  if (raw.runnerUser === 'root' || raw.runnerUser === raw.adminUser) {
    throw new Error('runnerUser must be distinct from root and adminUser');
  }
  if (!Number.isInteger(raw.cpus) || raw.cpus < 1) {
    throw new Error('cpus must be a positive integer');
  }
  if (typeof raw.memoryMb !== 'number' || !Number.isFinite(raw.memoryMb) || raw.memoryMb < 512) {
    throw new Error('memoryMb must be >= 512');
  }
  const maxJobMinutes = raw.maxJobMinutes === undefined ? 30 : raw.maxJobMinutes;
  if (!Number.isInteger(maxJobMinutes) || maxJobMinutes < 1 || maxJobMinutes > 1440) {
    throw new Error('maxJobMinutes must be an integer between 1 and 1440');
  }
  const maxProcesses = raw.maxProcesses === undefined ? 2048 : raw.maxProcesses;
  if (!Number.isInteger(maxProcesses) || maxProcesses < 128) {
    throw new Error('maxProcesses must be an integer >= 128');
  }
  const blockedLanCidrs = Array.isArray(raw.blockedLanCidrs)
    ? raw.blockedLanCidrs
    : ['192.168.0.0/16', '10.0.0.0/8', '172.16.0.0/12', '169.254.0.0/16'];
  for (const cidr of blockedLanCidrs) {
    if (typeof cidr !== 'string' || !CidrPattern.test(cidr)) {
      throw new Error(`invalid blockedLanCidrs entry: ${String(cidr)}`);
    }
  }
  const network = raw.network && typeof raw.network === 'object' ? raw.network : { dhcp4: true };
  if (network.dhcp4 === false) {
    if (typeof network.address !== 'string' || !CidrPattern.test(network.address)) {
      throw new Error('network.address (CIDR) is required when network.dhcp4 is false');
    }
    if (typeof network.gateway4 !== 'string' || !/^(?:\d{1,3}\.){3}\d{1,3}$/.test(network.gateway4)) {
      throw new Error('network.gateway4 is required when network.dhcp4 is false');
    }
  }
  return {
    workerName: raw.workerName,
    hostname: raw.hostname,
    connectHost: raw.connectHost,
    network,
    adminUser: raw.adminUser,
    runnerUser: raw.runnerUser,
    workspace: typeof raw.workspace === 'string' && raw.workspace.trim() !== '' ? raw.workspace : '~/henicos',
    cpus: raw.cpus,
    memoryMb: raw.memoryMb,
    maxJobMinutes,
    maxProcesses,
    sshPublicKey: typeof raw.sshPublicKey === 'string' ? raw.sshPublicKey.trim() : '',
    sshPublicKeyPath: typeof raw.sshPublicKeyPath === 'string' ? raw.sshPublicKeyPath : '~/.ssh/id_ed25519.pub',
    blockAllEgress: raw.blockAllEgress !== false,
    blockedLanCidrs,
    dotnetChannel: typeof raw.dotnetChannel === 'string' && /^\d+\.\d+$/.test(raw.dotnetChannel) ? raw.dotnetChannel : '10.0',
    timezone: typeof raw.timezone === 'string' && /^[A-Za-z0-9_/-]+$/.test(raw.timezone) ? raw.timezone : 'UTC',
  };
}

function resolvePublicKey(profile) {
  const key =
    profile.sshPublicKey !== ''
      ? profile.sshPublicKey
      : fs.readFileSync(expandHome(profile.sshPublicKeyPath), 'utf8').trim();
  if (!PubKeyPattern.test(key)) {
    throw new Error('SSH public key is missing or not a valid OpenSSH public key line');
  }
  return key;
}

function buildSshdMatchConfig(profile) {
  return [
    `# Managed by Henicos provision-remote-worker.js`,
    `AllowUsers ${profile.adminUser} ${profile.runnerUser}`,
    `Match User ${profile.runnerUser}`,
    `    AuthorizedKeysFile /etc/ssh/authorized_keys/%u`,
    `    PasswordAuthentication no`,
    `    KbdInteractiveAuthentication no`,
    `    AllowTcpForwarding no`,
    `    AllowAgentForwarding no`,
    `    AllowStreamLocalForwarding no`,
    `    X11Forwarding no`,
    `    PermitTunnel no`,
    '',
  ].join('\n');
}

function buildLimitsConfig(profile) {
  return [
    '# Managed by Henicos provision-remote-worker.js',
    `${profile.runnerUser} soft nproc ${profile.maxProcesses}`,
    `${profile.runnerUser} hard nproc ${profile.maxProcesses}`,
    '',
  ].join('\n');
}

/**
 * Generates an idempotent bash script (run as root via sudo or Subiquity late-commands) that creates the unprivileged
 * `runnerUser`, pins its root-owned authorized_keys & sshd Match User block, and installs UID-scoped RFC1918 LAN
 * egress blocking rules in /etc/ufw/before.rules.
 */
function buildHardeningShellScript(profile, pubKey, migrateCache) {
  const sshdConf = buildSshdMatchConfig(profile);
  const limitsConf = buildLimitsConfig(profile);
  const ipv4RejectLines = profile.blockAllEgress
    ? '  echo "-A ufw-before-output -m owner --uid-owner ${RUNNER_UID} -j REJECT --reject-with icmp-port-unreachable"'
    : profile.blockedLanCidrs
        .map(
          (cidr) =>
            `  echo "-A ufw-before-output -m owner --uid-owner \${RUNNER_UID} -d ${cidr} -j REJECT --reject-with icmp-port-unreachable"`,
        )
        .join('\n');
  const ipv6RejectLines = profile.blockAllEgress
    ? '  echo "-A ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -j REJECT --reject-with icmp6-port-unreachable"'
    : [
        '  echo "-A ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -d fc00::/7 -j REJECT --reject-with icmp6-port-unreachable"',
        '  echo "-A ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -d fe80::/10 -j REJECT --reject-with icmp6-port-unreachable"',
      ].join('\n');

  return [
    '#!/usr/bin/env bash',
    'set -euo pipefail',
    '',
    `ADMIN_USER=${JSON.stringify(profile.adminUser)}`,
    `RUNNER_USER=${JSON.stringify(profile.runnerUser)}`,
    '',
    '# 1. Create dedicated unprivileged runner user (no sudo, password disabled with "*" so pubkey SSH works, 0750 home)',
    'if ! id -u "$RUNNER_USER" >/dev/null 2>&1; then',
    '  useradd -m -s /bin/bash "$RUNNER_USER"',
    'fi',
    'usermod -U "$RUNNER_USER" >/dev/null 2>&1 || true',
    'usermod -p "*" "$RUNNER_USER"',
    'for grp in sudo adm systemd-journal docker lxd; do',
    '  if id -nG "$RUNNER_USER" | tr " " "\\n" | grep -qx "$grp"; then',
    '    gpasswd -d "$RUNNER_USER" "$grp" >/dev/null 2>&1 || true',
    '  fi',
    'done',
    'chmod 0750 "/home/$RUNNER_USER"',
    'if [ -d "/home/$ADMIN_USER" ]; then chmod 0750 "/home/$ADMIN_USER"; fi',
    '',
    '# 2. Install root-owned authorized_keys (runner cannot modify its own SSH keys)',
    'install -d -m 0755 -o root -g root /etc/ssh/authorized_keys',
    'cat <<\'__GSHARP_PUBKEY__\' > "/etc/ssh/authorized_keys/$RUNNER_USER"',
    pubKey,
    '__GSHARP_PUBKEY__',
    'chown root:root "/etc/ssh/authorized_keys/$RUNNER_USER"',
    'chmod 0644 "/etc/ssh/authorized_keys/$RUNNER_USER"',
    'rm -rf "/home/$RUNNER_USER/.ssh"',
    '',
    '# 3. Lock down sshd for runner user',
    'install -d -m 0755 /etc/ssh/sshd_config.d',
    'cat <<\'__GSHARP_SSHD__\' > /etc/ssh/sshd_config.d/90-jobrunner.conf',
    sshdConf.trimEnd(),
    '__GSHARP_SSHD__',
    'chmod 0644 /etc/ssh/sshd_config.d/90-jobrunner.conf',
    'sshd -t',
    'systemctl reload ssh 2>/dev/null || systemctl reload sshd 2>/dev/null || true',
    '',
    '# 4. Configure nproc fork-bomb guard',
    'cat <<\'__GSHARP_LIMITS__\' > /etc/security/limits.d/90-jobrunner.conf',
    limitsConf.trimEnd(),
    '__GSHARP_LIMITS__',
    'chmod 0644 /etc/security/limits.d/90-jobrunner.conf',
    '',
    '# 5. Block runner UID outbound traffic (IPv4 & IPv6) via UFW before.rules / before6.rules',
    'RUNNER_UID=$(id -u "$RUNNER_USER")',
    'patch_ufw_rules() {',
    '  local rules_path="$1"',
    '  local block_path="$2"',
    '  [ -f "$rules_path" ] || return 0',
    '  python3 - "$rules_path" "$block_path" <<\'__GSHARP_PY__\'',
    'import sys',
    'rules_path, block_path = sys.argv[1], sys.argv[2]',
    'text = open(rules_path, "r", encoding="utf-8").read()',
    'block = open(block_path, "r", encoding="utf-8").read().strip() + "\\n"',
    'start_marker = "# BEGIN GSHARP-JOBRUNNER-ISOLATION"',
    'end_marker = "# END GSHARP-JOBRUNNER-ISOLATION"',
    'if start_marker in text and end_marker in text:',
    '    a = text.index(start_marker)',
    '    b = text.index(end_marker) + len(end_marker)',
    '    if b < len(text) and text[b] == "\\n":',
    '        b += 1',
    '    text = text[:a] + text[b:]',
    'idx = text.rfind("\\nCOMMIT")',
    'if idx < 0:',
    '    raise SystemExit("COMMIT marker not found in " + rules_path)',
    'new_text = text[:idx + 1] + "\\n" + block + text[idx + 1:]',
    'open(rules_path, "w", encoding="utf-8").write(new_text)',
    '__GSHARP_PY__',
    '}',
    'if [ -f /etc/ufw/before.rules ]; then',
    '  TMP_V4=$(mktemp)',
    '  {',
    '    echo "# BEGIN GSHARP-JOBRUNNER-ISOLATION"',
    '    echo "-A ufw-before-output -m owner --uid-owner ${RUNNER_UID} -o lo -j ACCEPT"',
    '    echo "-A ufw-before-output -m owner --uid-owner ${RUNNER_UID} -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT"',
    ipv4RejectLines,
    '    echo "# END GSHARP-JOBRUNNER-ISOLATION"',
    '  } > "$TMP_V4"',
    '  patch_ufw_rules /etc/ufw/before.rules "$TMP_V4"',
    '  rm -f "$TMP_V4"',
    'fi',
    'if [ -f /etc/ufw/before6.rules ]; then',
    '  TMP_V6=$(mktemp)',
    '  {',
    '    echo "# BEGIN GSHARP-JOBRUNNER-ISOLATION"',
    '    echo "-A ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -o lo -j ACCEPT"',
    '    echo "-A ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT"',
    ipv6RejectLines,
    '    echo "# END GSHARP-JOBRUNNER-ISOLATION"',
    '  } > "$TMP_V6"',
    '  patch_ufw_rules /etc/ufw/before6.rules "$TMP_V6"',
    '  rm -f "$TMP_V6"',
    'fi',
    'ufw limit 22/tcp >/dev/null 2>&1 || true',
    'ufw --force enable >/dev/null 2>&1 || true',
    'ufw reload >/dev/null 2>&1 || true',
    '',
    ...(migrateCache
      ? [
          '# 6. Migrate existing warm build workspace from admin user if runner workspace does not exist yet',
          'if [ ! -d "/home/$RUNNER_USER/henicos" ] && [ -d "/home/$ADMIN_USER/henicos" ]; then',
          '  cp -a "/home/$ADMIN_USER/henicos" "/home/$RUNNER_USER/henicos"',
          '  rm -rf "/home/$RUNNER_USER/henicos/.claude/worktrees" "/home/$RUNNER_USER/henicos/.job-queue-manifest.digest" "/home/$RUNNER_USER/henicos/.job-queue-manifest.sha256"',
          '  chown -R "$RUNNER_USER:$RUNNER_USER" "/home/$RUNNER_USER/henicos"',
          'fi',
          'if [ ! -d "/home/$RUNNER_USER/.nuget/packages" ] && [ -d "/home/$ADMIN_USER/.nuget/packages" ]; then',
          '  mkdir -p "/home/$RUNNER_USER/.nuget"',
          '  cp -a "/home/$ADMIN_USER/.nuget/packages" "/home/$RUNNER_USER/.nuget/packages"',
          '  chown -R "$RUNNER_USER:$RUNNER_USER" "/home/$RUNNER_USER/.nuget"',
          'fi',
        ]
      : []),
    'mkdir -p "/home/$RUNNER_USER/henicos"',
    'chown "$RUNNER_USER:$RUNNER_USER" "/home/$RUNNER_USER/henicos"',
    'echo "PROVISION_OK runner=$RUNNER_USER uid=$RUNNER_UID"',
    '',
  ].join('\n');
}

function buildAutoinstallUserData(profile, pubKey) {
  const net = profile.network;
  const networkBlock =
    net.dhcp4 === false
      ? [
          '  network:',
          '    version: 2',
          '    ethernets:',
          '      id0:',
          '        match:',
          '          name: "e*"',
          '        dhcp4: false',
          `        addresses: ["${net.address}"]`,
          '        routes:',
          `          - to: default`,
          `            via: ${net.gateway4}`,
          '        nameservers:',
          `          addresses: [${(Array.isArray(net.nameservers) && net.nameservers.length > 0 ? net.nameservers : ['1.1.1.1']).join(', ')}]`,
        ].join('\n')
      : [
          '  network:',
          '    version: 2',
          '    ethernets:',
          '      id0:',
          '        match:',
          '          name: "e*"',
          '        dhcp4: true',
        ].join('\n');

  const hardeningB64 = Buffer.from(buildHardeningShellScript(profile, pubKey, false), 'utf8').toString('base64');

  return [
    '#cloud-config',
    'autoinstall:',
    '  version: 1',
    '  locale: en_US.UTF-8',
    `  timezone: ${profile.timezone}`,
    '  keyboard:',
    '    layout: us',
    '  storage:',
    '    layout:',
    '      name: direct',
    networkBlock,
    '  identity:',
    `    hostname: ${profile.hostname}`,
    `    username: ${profile.adminUser}`,
    '    # Password disabled ("*"): SSH key login works, password login impossible',
    '    password: "*"',
    '  ssh:',
    '    install-server: true',
    '    allow-pw: false',
    '    authorized-keys:',
    `      - "${pubKey.replace(/"/g, '\\"')}"`,
    '  packages:',
    '    - openssh-server',
    '    - ufw',
    '    - avahi-daemon',
    '    - git',
    '    - curl',
    '    - rsync',
    '    - clang',
    '    - g++',
    '    - ninja-build',
    '    - ca-certificates',
    '    - libicu-dev',
    '    - psmisc',
    '    - python3',
    '  late-commands:',
    `    - curtin in-target -- bash -c "curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh && bash /tmp/dotnet-install.sh --channel ${profile.dotnetChannel} --install-dir /usr/local/share/dotnet && ln -sf /usr/local/share/dotnet/dotnet /usr/local/bin/dotnet && rm -f /tmp/dotnet-install.sh"`,
    '    - curtin in-target -- bash -c "curl --proto =https --tlsv1.2 -sSf https://just.systems/install.sh | bash -s -- --to /usr/local/bin"',
    `    - curtin in-target -- bash -c "echo '${hardeningB64}' | base64 -d > /usr/local/sbin/henicos-harden-worker.sh && chmod 0700 /usr/local/sbin/henicos-harden-worker.sh && /usr/local/sbin/henicos-harden-worker.sh"`,
    '',
  ].join('\n');
}

function buildAutoinstallMetaData(profile) {
  return `instance-id: henicos-worker-${profile.hostname}\nlocal-hostname: ${profile.hostname}\n`;
}

function buildRemotesLocalJson(profile) {
  const sshTarget = `${profile.runnerUser}@${profile.connectHost}`;
  const hardenedSshOptions = [
    '-o',
    'BatchMode=yes',
    '-o',
    'StrictHostKeyChecking=yes',
    '-o',
    'ForwardAgent=no',
    '-o',
    'ForwardX11=no',
    '-o',
    'ClearAllForwardings=yes',
    '-o',
    'PermitLocalCommand=no',
    '-o',
    'ConnectTimeout=5',
    '-o',
    'ServerAliveInterval=5',
    '-o',
    'ServerAliveCountMax=3',
  ];
  return {
    remotes: {
      [profile.workerName]: {
        host: sshTarget,
        workspace: profile.workspace,
        slots: 1,
        cpus: profile.cpus,
        memoryMb: profile.memoryMb,
        mode: 'prefer-remote',
        maxJobMinutes: profile.maxJobMinutes,
        sshOptions: hardenedSshOptions,
        keepBuildServersWarm: true,
        probe: {
          argv: [
            'ssh',
            '-o',
            'BatchMode=yes',
            '-o',
            'StrictHostKeyChecking=yes',
            '-o',
            'ForwardAgent=no',
            '-o',
            'ForwardX11=no',
            '-o',
            'ClearAllForwardings=yes',
            '-o',
            'PermitLocalCommand=no',
            '-o',
            'ConnectTimeout=3',
            '--',
            sshTarget,
            'awk \'/MemAvailable:/{printf "%d\\n", $2/1024}\' /proc/meminfo',
          ],
          min: 1536,
          timeoutMs: 5000,
        },
      },
    },
  };
}

function buildSeedIso(outDir, profile, isoViaSsh) {
  const isoPath = path.join(outDir, 'cidata.iso');
  const userDataPath = path.join(outDir, 'user-data');
  const metaDataPath = path.join(outDir, 'meta-data');

  const tryLocal = (cmd, args) => {
    const res = spawnSync(cmd, args, { stdio: 'pipe', windowsHide: true });
    return !res.error && res.status === 0;
  };

  if (
    tryLocal('xorriso', ['-as', 'mkisofs', '-output', isoPath, '-volid', 'cidata', '-joliet', '-rock', userDataPath, metaDataPath]) ||
    tryLocal('genisoimage', ['-output', isoPath, '-volid', 'cidata', '-joliet', '-rock', userDataPath, metaDataPath]) ||
    tryLocal('mkisofs', ['-output', isoPath, '-volid', 'cidata', '-joliet', '-rock', userDataPath, metaDataPath]) ||
    tryLocal('cloud-localds', [isoPath, userDataPath, metaDataPath])
  ) {
    return isoPath;
  }

  if (isoViaSsh) {
    const adminTarget = `${profile.adminUser}@${profile.connectHost}`;
    const uB64 = fs.readFileSync(userDataPath).toString('base64');
    const mB64 = fs.readFileSync(metaDataPath).toString('base64');
    const remoteScript = [
      'set -euo pipefail',
      'D=$(mktemp -d)',
      'trap \'rm -rf "$D"\' EXIT',
      `echo '${uB64}' | base64 -d > "$D/user-data"`,
      `echo '${mB64}' | base64 -d > "$D/meta-data"`,
      'if command -v xorriso >/dev/null 2>&1; then',
      '  xorriso -as mkisofs -output "$D/cidata.iso" -volid cidata -joliet -rock "$D/user-data" "$D/meta-data" >/dev/null 2>&1',
      'elif command -v genisoimage >/dev/null 2>&1; then',
      '  genisoimage -output "$D/cidata.iso" -volid cidata -joliet -rock "$D/user-data" "$D/meta-data" >/dev/null 2>&1',
      'elif command -v cloud-localds >/dev/null 2>&1; then',
      '  cloud-localds "$D/cidata.iso" "$D/user-data" "$D/meta-data" >/dev/null 2>&1',
      'else',
      '  echo "MISSING_ISO_TOOL: install xorriso or cloud-image-utils on the Ubuntu host" >&2',
      '  exit 2',
      'fi',
      'cat "$D/cidata.iso"',
    ].join('\n');
    const res = spawnSync(
      'ssh',
      ['-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=yes', '--', adminTarget, 'bash -s'],
      { input: remoteScript, maxBuffer: 16 * 1024 * 1024, windowsHide: true },
    );
    if (res.status === 0 && res.stdout && res.stdout.length > 0) {
      fs.writeFileSync(isoPath, res.stdout);
      return isoPath;
    }
    throw new Error(`failed to build cidata.iso via SSH (${adminTarget}): ${res.stderr.toString('utf8').trim()}`);
  }

  throw new Error(
    'no local ISO tool found (xorriso / genisoimage / mkisofs / cloud-localds). ' +
      'Re-run with --iso-via-ssh (after installing xorriso on Ubuntu) or copy user-data & meta-data onto a FAT32 volume labeled CIDATA.',
  );
}

function parseArgs(argv) {
  const o = {
    mode: '',
    profile: DefaultProfilePath,
    out: '',
    buildIso: false,
    isoViaSsh: false,
    writeRemotes: false,
    migrateCache: true,
  };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    switch (a) {
      case '--help':
      case '-h':
        process.stdout.write(`${Usage}\n`);
        process.exit(0);
        break;
      case '--init-profile':
        o.mode = 'init-profile';
        break;
      case '--apply':
        o.mode = 'apply';
        break;
      case '--emit-autoinstall':
        o.mode = 'emit-autoinstall';
        break;
      case '--write-remotes':
        if (o.mode === '') {
          o.mode = 'write-remotes';
        }
        o.writeRemotes = true;
        break;
      case '--self-test':
        o.mode = 'self-test';
        break;
      case '--profile':
        o.profile = path.resolve(argv[++i] || '');
        break;
      case '--out':
        o.out = path.resolve(argv[++i] || '');
        break;
      case '--build-iso':
        o.buildIso = true;
        break;
      case '--iso-via-ssh':
        o.isoViaSsh = true;
        break;
      case '--no-migrate-cache':
        o.migrateCache = false;
        break;
      default:
        process.stderr.write(`Unknown argument: ${a}\n${Usage}\n`);
        process.exit(2);
    }
  }
  if (o.mode === '') {
    process.stderr.write(`${Usage}\n`);
    process.exit(2);
  }
  return o;
}

function selfTest() {
  const sampleRaw = JSON.parse(fs.readFileSync(ExampleProfilePath, 'utf8'));
  const profile = validateProfile(sampleRaw);
  const fakeKey = 'ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITestKeyForSelfTestOnly test@local';

  // Reject unsafe runnerUser and invalid CIDRs
  assert.throws(() => validateProfile({ ...sampleRaw, runnerUser: 'root' }));
  assert.throws(() => validateProfile({ ...sampleRaw, runnerUser: sampleRaw.adminUser }));
  assert.throws(() => validateProfile({ ...sampleRaw, blockedLanCidrs: ['not-a-cidr'] }));
  assert.throws(() => validateProfile({ ...sampleRaw, connectHost: '-oProxyCommand=evil' }));

  const sshd = buildSshdMatchConfig(profile);
  assert.ok(sshd.includes('Match User jobrunner'));
  assert.ok(sshd.includes('AuthorizedKeysFile /etc/ssh/authorized_keys/%u'));
  assert.ok(sshd.includes('AllowTcpForwarding no'));
  assert.ok(sshd.includes('AllowAgentForwarding no'));

  const script = buildHardeningShellScript(profile, fakeKey, true);
  assert.ok(script.includes('chown root:root "/etc/ssh/authorized_keys/$RUNNER_USER"'));
  assert.ok(script.includes('--uid-owner ${RUNNER_UID} -j REJECT --reject-with icmp-port-unreachable'));
  assert.ok(script.includes('ufw6-before-output -m owner --uid-owner ${RUNNER_UID} -j REJECT'));
  assert.ok(script.includes('BEGIN GSHARP-JOBRUNNER-ISOLATION'));

  const lanOnlyScript = buildHardeningShellScript(validateProfile({ ...sampleRaw, blockAllEgress: false }), fakeKey, false);
  assert.ok(lanOnlyScript.includes('-d 192.168.0.0/16 -j REJECT'));

  const userData = buildAutoinstallUserData(profile, fakeKey);
  assert.ok(userData.startsWith('#cloud-config\nautoinstall:\n'));
  assert.ok(userData.includes('name: direct'), 'uses hardware-agnostic direct storage layout for bare-metal & VM');
  assert.ok(userData.includes('dotnet-install.sh --channel 10.0'));
  assert.ok(userData.includes('henicos-harden-worker.sh'));

  const metaData = buildAutoinstallMetaData(profile);
  assert.ok(metaData.includes('local-hostname: ubuntu-server'));

  const remotes = buildRemotesLocalJson(profile);
  assert.strictEqual(remotes.remotes.ubuntu.host, 'jobrunner@ubuntu-server.lan');
  assert.ok(remotes.remotes.ubuntu.sshOptions.includes('StrictHostKeyChecking=yes'));
  assert.ok(remotes.remotes.ubuntu.sshOptions.includes('ServerAliveInterval=5'));
  assert.strictEqual(remotes.remotes.ubuntu.probe.argv[0], 'ssh');

  process.stdout.write('provision-remote-worker self-test: passed\n');
}

function main() {
  const opts = parseArgs(process.argv.slice(2));
  if (opts.mode === 'self-test') {
    selfTest();
    return;
  }
  if (opts.mode === 'init-profile') {
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

  const profile = validateProfile(JSON.parse(fs.readFileSync(opts.profile, 'utf8')));

  if (opts.mode === 'write-remotes') {
    const dest = opts.out || DefaultRemotesPath;
    fs.mkdirSync(path.dirname(dest), { recursive: true });
    fs.writeFileSync(dest, `${JSON.stringify(buildRemotesLocalJson(profile), null, 2)}\n`, 'utf8');
    process.stdout.write(`wrote ${dest}\n`);
    return;
  }

  const pubKey = resolvePublicKey(profile);

  if (opts.mode === 'emit-autoinstall') {
    const outDir = opts.out || path.join(DefaultStateDir, 'autoinstall-seed');
    fs.mkdirSync(outDir, { recursive: true });
    fs.writeFileSync(path.join(outDir, 'user-data'), buildAutoinstallUserData(profile, pubKey), 'utf8');
    fs.writeFileSync(path.join(outDir, 'meta-data'), buildAutoinstallMetaData(profile), 'utf8');
    fs.writeFileSync(path.join(outDir, 'harden-worker.sh'), buildHardeningShellScript(profile, pubKey, false), 'utf8');
    process.stdout.write(`wrote autoinstall seed files to ${outDir}\n`);
    if (opts.buildIso) {
      const isoPath = buildSeedIso(outDir, profile, opts.isoViaSsh);
      process.stdout.write(`built seed ISO: ${isoPath}\n`);
    }
    return;
  }

  if (opts.mode === 'apply') {
    const adminTarget = `${profile.adminUser}@${profile.connectHost}`;
    const scriptB64 = Buffer.from(buildHardeningShellScript(profile, pubKey, opts.migrateCache), 'utf8').toString('base64');
    const remoteCmd = `echo '${scriptB64}' | base64 -d | sudo bash -s`;
    process.stdout.write(`applying jobrunner hardening to ${adminTarget} (sudo password may be prompted)...\n`);
    const res = spawnSync(
      'ssh',
      ['-t', '-o', 'StrictHostKeyChecking=yes', '-o', 'ConnectTimeout=10', '--', adminTarget, remoteCmd],
      { stdio: 'inherit' },
    );
    if (res.status !== 0) {
      process.exit(res.status ?? 1);
    }
    if (opts.writeRemotes) {
      fs.mkdirSync(path.dirname(DefaultRemotesPath), { recursive: true });
      fs.writeFileSync(DefaultRemotesPath, `${JSON.stringify(buildRemotesLocalJson(profile), null, 2)}\n`, 'utf8');
      process.stdout.write(`updated ${DefaultRemotesPath}\n`);
    }
  }
}

main();
