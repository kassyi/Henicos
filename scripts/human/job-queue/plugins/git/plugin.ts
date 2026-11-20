// Built-in git plugin: a digest of a worktree's content (`contentDigest`), so that a job can say when its worktree
// changed between the queue and the start. The digest is the tree `git write-tree` gives for everything `git add -A`
// would stage (Verify's `*.received.*` files left out), written through a copy of the index and a temporary object
// directory, with the settings that write into the repository turned off (a split index, clean filters such as Git
// LFS, the file system monitor, automatic gc), so that the worktree's own .git gains nothing. Outside a git worktree,
// or when git fails, there is no digest. A client that exits during a digest removes its scratch folder and stops its
// git at exit.

import { execFile, spawnSync, type ChildProcess } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { PluginApiVersion, type ClientContext, type ContentDigest, type QueuePlugin } from "../../plugin-api.ts";

/** A git call gives up after this long: the digest is a notice, never worth a wait. */
const GitTimeoutMs = 20000;

/** The scratch folders of the digests are named so; one older than StaleScratchMs was left by a client that died. */
const ScratchPrefix = "job-queue-digest-";
const StaleScratchMs = 60 * 60 * 1000;

/** What `git add -A` leaves out of the digest: Verify writes these files on a failed test, they say nothing built. */
const LeftOut = ":(exclude,glob)**/*.received.*";

/** The digests under way in this process: their scratch folders and git processes, cleaned up at exit. */
const underWay = { folders: new Set<string>(), children: new Set<ChildProcess>(), hooked: false };

/** At exit (process.exit included, which runs no finally), synchronously: stop the gits and remove the folders. */
function cleanUpAtExit(): void {
  for (const child of underWay.children) {
    // On Windows git.exe starts the real git and ends with it: only the whole tree stops it
    if (process.platform === "win32" && child.pid !== undefined) {
      spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], { stdio: "ignore", windowsHide: true });
    } else {
      child.kill();
    }
  }
  for (const folder of underWay.folders) {
    fs.rmSync(folder, { recursive: true, force: true });
  }
}

/** Removes the scratch folders a client left when it died during a digest. Best effort. */
function removeStaleScratch(now: number): void {
  let names: string[];
  try {
    names = fs.readdirSync(os.tmpdir()).filter((name) => name.startsWith(ScratchPrefix));
  } catch {
    return; // no listing: nothing to clean this time
  }
  for (const name of names) {
    const folder = path.join(os.tmpdir(), name);
    try {
      if (now - fs.statSync(folder).mtimeMs > StaleScratchMs) {
        fs.rmSync(folder, { recursive: true, force: true });
      }
    } catch {
      // another client removes it at the same time, or it is in use: left for the next time
    }
  }
}

/** Runs git in `cwd`; resolves to its trimmed output, or null when it fails. Never rejects. */
function git(cwd: string, args: string[], env: NodeJS.ProcessEnv): Promise<string | null> {
  return new Promise((resolve) => {
    const child = execFile(
      "git",
      args,
      { cwd, env, encoding: "utf8", windowsHide: true, timeout: GitTimeoutMs, maxBuffer: 1024 * 1024 },
      (error, stdout) => {
        underWay.children.delete(child);
        resolve(error ? null : stdout.trim());
      },
    );
    underWay.children.add(child);
  });
}

/**
 * The -c options that keep `git add` from writing into the repository: no split index (it writes a shared index next
 * to the real one), no clean filter of any configured driver (Git LFS stores objects under .git/lfs), no file system
 * monitor and no automatic gc or maintenance.
 */
async function isolatingOptions(worktree: string, env: NodeJS.ProcessEnv): Promise<string[]> {
  const options = [
    "core.splitIndex=false",
    "core.fsmonitor=false",
    "core.untrackedCache=false",
    "gc.auto=0",
    "maintenance.auto=false",
  ];
  const drivers = await git(
    worktree,
    ["config", "--name-only", "--get-regexp", "^filter\\..*\\.(clean|process)$"],
    env,
  );
  const names = new Set((drivers ?? "").split(/\r?\n/).map((key) => /^filter\.(.+)\.(clean|process)$/i.exec(key)?.[1]));
  for (const name of names) {
    if (name !== undefined) {
      options.push(`filter.${name}.clean=`, `filter.${name}.process=`, `filter.${name}.required=false`);
    }
  }
  return options.flatMap((option) => ["-c", option]);
}

/** The tree of what `git add -A` would stage in `worktree`, with the worktree's .git untouched. */
export async function worktreeDigest(worktree: string, env: NodeJS.ProcessEnv): Promise<string | undefined> {
  // GIT_* variables of a caller (a hook, another index) would point the calls elsewhere
  const clean = Object.fromEntries(Object.entries(env).filter(([name]) => !/^GIT_/i.test(name)));
  const paths = await git(
    worktree,
    ["rev-parse", "--path-format=absolute", "--git-path", "index", "--git-path", "objects"],
    clean,
  );
  const [index, objects] = paths?.split(/\r?\n/) ?? [];
  if (index === undefined || objects === undefined || index === "" || objects === "") {
    return undefined;
  }
  const options = await isolatingOptions(worktree, clean);
  removeStaleScratch(Date.now());
  if (!underWay.hooked) {
    underWay.hooked = true;
    process.once("exit", cleanUpAtExit);
  }
  const scratch = fs.mkdtempSync(path.join(os.tmpdir(), ScratchPrefix));
  underWay.folders.add(scratch);
  try {
    const copy = path.join(scratch, "index");
    // git takes a missing object directory for no repository at all
    fs.mkdirSync(path.join(scratch, "objects"));
    if (fs.existsSync(index)) {
      // A copy saves the stat of every file; a split index needs its shared parts beside it
      fs.copyFileSync(index, copy);
      for (const name of fs.readdirSync(path.dirname(index)).filter((file) => file.startsWith("sharedindex."))) {
        fs.copyFileSync(path.join(path.dirname(index), name), path.join(scratch, name));
      }
    }
    const isolated = {
      ...clean,
      GIT_INDEX_FILE: copy,
      GIT_OBJECT_DIRECTORY: path.join(scratch, "objects"),
      GIT_ALTERNATE_OBJECT_DIRECTORIES: objects,
    };
    if ((await git(worktree, [...options, "add", "-A", "--", ".", LeftOut], isolated)) === null) {
      return undefined;
    }
    const tree = await git(worktree, [...options, "write-tree"], isolated);
    return tree !== null && /^[0-9a-f]{40,64}$/.test(tree) ? tree : undefined;
  } finally {
    underWay.folders.delete(scratch);
    fs.rmSync(scratch, { recursive: true, force: true });
  }
}

const plugin = {
  name: "git",
  apiVersion: PluginApiVersion,

  async contentDigest(context: ClientContext): Promise<ContentDigest | undefined> {
    const started = Date.now();
    const digest = await worktreeDigest(context.worktree, context.env);
    return digest === undefined ? undefined : { digest, ms: Date.now() - started };
  },
} satisfies QueuePlugin;

export default plugin;
