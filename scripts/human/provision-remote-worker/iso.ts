// Target Generator: packs `user-data` and `meta-data` into a NoCloud `cidata.iso` locally or via SSH.

import { spawnSync } from "node:child_process";
import * as fs from "node:fs";
import * as path from "node:path";
import type { WorkerProfile } from "./profile.ts";

export function buildSeedIso(outDir: string, profile: WorkerProfile, isoViaSsh: boolean): string {
  const isoPath = path.join(outDir, "cidata.iso");
  const userDataPath = path.join(outDir, "user-data");
  const metaDataPath = path.join(outDir, "meta-data");

  const tryLocal = (cmd: string, args: string[]): boolean => {
    const res = spawnSync(cmd, args, { stdio: "pipe", windowsHide: true });
    return !res.error && res.status === 0;
  };

  if (
    tryLocal("xorriso", [
      "-as",
      "mkisofs",
      "-output",
      isoPath,
      "-volid",
      "cidata",
      "-joliet",
      "-rock",
      userDataPath,
      metaDataPath,
    ]) ||
    tryLocal("genisoimage", ["-output", isoPath, "-volid", "cidata", "-joliet", "-rock", userDataPath, metaDataPath]) ||
    tryLocal("mkisofs", ["-output", isoPath, "-volid", "cidata", "-joliet", "-rock", userDataPath, metaDataPath]) ||
    tryLocal("cloud-localds", [isoPath, userDataPath, metaDataPath])
  ) {
    return isoPath;
  }

  if (isoViaSsh) {
    const adminTarget = `${profile.adminUser}@${profile.connectHost}`;
    const uB64 = fs.readFileSync(userDataPath).toString("base64");
    const mB64 = fs.readFileSync(metaDataPath).toString("base64");
    const remoteScript = [
      "set -euo pipefail",
      "D=$(mktemp -d)",
      "trap 'rm -rf \"$D\"' EXIT",
      `echo '${uB64}' | base64 -d > "$D/user-data"`,
      `echo '${mB64}' | base64 -d > "$D/meta-data"`,
      "if command -v xorriso >/dev/null 2>&1; then",
      '  xorriso -as mkisofs -output "$D/cidata.iso" -volid cidata -joliet -rock "$D/user-data" "$D/meta-data" >/dev/null 2>&1',
      "elif command -v genisoimage >/dev/null 2>&1; then",
      '  genisoimage -output "$D/cidata.iso" -volid cidata -joliet -rock "$D/user-data" "$D/meta-data" >/dev/null 2>&1',
      "elif command -v cloud-localds >/dev/null 2>&1; then",
      '  cloud-localds "$D/cidata.iso" "$D/user-data" "$D/meta-data" >/dev/null 2>&1',
      "else",
      '  echo "MISSING_ISO_TOOL: install xorriso or cloud-image-utils on the Ubuntu host" >&2',
      "  exit 2",
      "fi",
      'cat "$D/cidata.iso"',
    ].join("\n");
    const res = spawnSync(
      "ssh",
      ["-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes", "--", adminTarget, "bash -s"],
      { input: remoteScript, maxBuffer: 16 * 1024 * 1024, windowsHide: true },
    );
    if (res.status === 0 && res.stdout && res.stdout.length > 0) {
      fs.writeFileSync(isoPath, res.stdout);
      return isoPath;
    }
    throw new Error(`failed to build cidata.iso via SSH (${adminTarget}): ${res.stderr.toString("utf8").trim()}`);
  }

  throw new Error(
    "no local ISO tool found (xorriso / genisoimage / mkisofs / cloud-localds). " +
      "Re-run with --iso-via-ssh (after installing xorriso on Ubuntu) or copy user-data & meta-data onto a FAT32 volume labeled CIDATA.",
  );
}
