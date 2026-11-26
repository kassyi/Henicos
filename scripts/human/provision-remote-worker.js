#!/usr/bin/env node
"use strict";

// Provisions a locked-down Ubuntu 24.04 remote CPU worker (`jobrunner` user with no sudo, root-owned SSH
// authorized_keys, sshd Match User restrictions, UID-scoped egress block via UFW) and emits Ubuntu 24.04 Autoinstall
// (`cloud-init` NoCloud) seed files / `cidata.iso` plus `<mainCheckout>/tmp/job-queue/remotes.local.json`.
// This thin entry checks the Node version and delegates to the TypeScript modules in provision-remote-worker/.
//
// Usage:
//   node scripts/human/provision-remote-worker.js --init-profile [--out <file>]
//   node scripts/human/provision-remote-worker.js --apply [--profile <file>] [--write-remotes] [--no-migrate-cache]
//   node scripts/human/provision-remote-worker.js --emit-autoinstall [--profile <file>] --out <dir> [--build-iso] [--iso-via-ssh]
//   node scripts/human/provision-remote-worker.js --write-remotes [--profile <file>] [--out <file>]
//   node scripts/human/provision-remote-worker.js --self-test
//   node scripts/human/provision-remote-worker.js --help

const [major, minor] = process.versions.node.split(".").map(Number);
const stripsTypes = major > 23 || (major === 23 && minor >= 6) || (major === 22 && minor >= 18);
if (!stripsTypes || (process.features && process.features.typescript === false)) {
  process.stderr.write(
    `[provision-remote-worker] Node ${process.versions.node} cannot run TypeScript files as they are; ` +
      "install Node 22.18, 23.6 or later (scripts/human/Install-DevTools.ps1 on Windows), " +
      "without --no-experimental-strip-types.\n",
  );
  process.exit(1);
}

import("./provision-remote-worker/cli.ts").then((cli) => cli.main(process.argv.slice(2)));
