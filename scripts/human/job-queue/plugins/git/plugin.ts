// Built-in git plugin: a digest of a worktree's content (`contentDigest`), so that a job can say when its worktree
// changed between the queue and the start. The digest is the tree `git write-tree` gives for everything `git add -A`
// would stage, written through a copy of the index and a temporary object directory, so that the worktree's own index
// and object store stay as they were. Outside a git worktree, or when git fails, there is no digest.

import { execFile } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { PluginApiVersion, type ClientContext, type ContentDigest, type QueuePlugin } from "../../plugin-api.ts";

/** A git call gives up after this long: the digest is a notice, never worth a wait. */
const GitTimeoutMs = 20000;

/** The scratch folders of the digests are named so; one older than StaleScratchMs was left by a client that exited. */
const ScratchPrefix = "job-queue-digest-";
const StaleScratchMs = 60 * 60 * 1000;

/** Removes the scratch folders a client left when it exited during a digest (the job ended first). Best effort. */
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
    execFile(
      "git",
      args,
      { cwd, env, encoding: "utf8", windowsHide: true, timeout: GitTimeoutMs, maxBuffer: 1024 * 1024 },
      (error, stdout) => resolve(error ? null : stdout.trim()),
    );
  });
}

/** The tree of what `git add -A` would stage in `worktree`, with the worktree's index and objects untouched. */
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
  removeStaleScratch(Date.now());
  const scratch = fs.mkdtempSync(path.join(os.tmpdir(), ScratchPrefix));
  try {
    const copy = path.join(scratch, "index");
    // git takes a missing object directory for no repository at all
    fs.mkdirSync(path.join(scratch, "objects"));
    if (fs.existsSync(index)) {
      fs.copyFileSync(index, copy);
    }
    const isolated = {
      ...clean,
      GIT_INDEX_FILE: copy,
      GIT_OBJECT_DIRECTORY: path.join(scratch, "objects"),
      GIT_ALTERNATE_OBJECT_DIRECTORIES: objects,
    };
    if ((await git(worktree, ["add", "-A"], isolated)) === null) {
      return undefined;
    }
    const tree = await git(worktree, ["write-tree"], isolated);
    return tree !== null && /^[0-9a-f]{40,64}$/.test(tree) ? tree : undefined;
  } finally {
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
