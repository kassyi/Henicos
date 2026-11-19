// Self-tests of the git plugin: the digest of a worktree's content, on a throwaway repository in the temporary
// directory, and that taking it leaves the repository's index and object store as they were.

import { spawnSync } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import type { AssertModule, ClientContext } from "../../plugin-api.ts";
import plugin, { worktreeDigest } from "./plugin.ts";

/** Runs git in `cwd` without the caller's GIT_* variables (a hook may set them), failing on an error. */
function git(cwd: string, ...args: string[]): string {
  const env = Object.fromEntries(Object.entries(process.env).filter(([name]) => !/^GIT_/i.test(name)));
  const result = spawnSync("git", ["-c", "user.name=check", "-c", "user.email=check@invalid", ...args], {
    cwd,
    env,
    encoding: "utf8",
    windowsHide: true,
  });
  if (result.status !== 0) {
    throw new Error(`git ${args.join(" ")}: ${result.stderr}`);
  }
  return result.stdout.trim();
}

/**
 * Every file under a directory with its size, and the index with its modification time too: what must not change. git
 * refreshes the time of an object it would write again (an existing blob, "freshening", which only delays its
 * pruning), so the objects are compared by name and size.
 */
function snapshot(directory: string): string[] {
  return (fs.readdirSync(directory, { recursive: true, withFileTypes: true }) as fs.Dirent[])
    .filter((entry) => entry.isFile())
    .map((entry) => {
      const file = path.join(entry.parentPath, entry.name);
      const stat = fs.statSync(file);
      const name = path.relative(directory, file);
      return `${name} ${stat.size}${name === "index" ? ` ${stat.mtimeMs}` : ""}`;
    })
    .sort();
}

async function checkDigest(assert: AssertModule): Promise<void> {
  const repo = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-git-check-"));
  const outside = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-git-outside-"));
  const env = { ...process.env };
  try {
    git(repo, "init", "-q");
    fs.writeFileSync(path.join(repo, "a.txt"), "one\n");
    fs.writeFileSync(path.join(repo, ".gitignore"), "ignored/\n");
    git(repo, "add", "-A");
    git(repo, "-c", "commit.gpgsign=false", "commit", "-q", "-m", "first");
    const before = snapshot(path.join(repo, ".git"));
    const first = await worktreeDigest(repo, env);
    assert.match(first ?? "", /^[0-9a-f]{40,64}$/, "a tree id");
    assert.strictEqual(first, git(repo, "rev-parse", "HEAD^{tree}"), "a clean worktree: the tree of HEAD");
    fs.writeFileSync(path.join(repo, "a.txt"), "two\n");
    const changed = await worktreeDigest(repo, env);
    assert.notStrictEqual(changed, first, "a tracked file changed");
    fs.writeFileSync(path.join(repo, "a.txt"), "one\n");
    assert.strictEqual(await worktreeDigest(repo, env), first, "changed back: the same digest");
    fs.writeFileSync(path.join(repo, "new.txt"), "new\n");
    const untracked = await worktreeDigest(repo, env);
    assert.notStrictEqual(untracked, first, "an untracked file counts");
    fs.mkdirSync(path.join(repo, "ignored"));
    fs.writeFileSync(path.join(repo, "ignored", "x.txt"), "x\n");
    assert.strictEqual(await worktreeDigest(repo, env), untracked, "an ignored file does not");
    assert.deepStrictEqual(
      snapshot(path.join(repo, ".git")),
      before,
      "no object is added, the index is left as it was",
    );
    // The caller's GIT_* variables do not point it elsewhere
    const pointed = { ...env, GIT_DIR: path.join(outside, "nothing"), GIT_INDEX_FILE: path.join(outside, "index") };
    assert.strictEqual(await worktreeDigest(repo, pointed), untracked, "GIT_DIR and GIT_INDEX_FILE are ignored");
    assert.strictEqual(await worktreeDigest(outside, env), undefined, "outside a git worktree: no digest");
    const context: ClientContext = {
      cwd: repo,
      worktree: repo,
      configRoot: repo,
      shell: "bash",
      env,
      options: {},
      log: () => {},
    };
    const hooked = await plugin.contentDigest(context);
    assert.strictEqual(hooked?.digest, untracked, "the hook gives the same digest");
    assert.ok(typeof hooked?.ms === "number" && hooked.ms >= 0, "and how long it took");
  } finally {
    fs.rmSync(repo, { recursive: true, force: true });
    fs.rmSync(outside, { recursive: true, force: true });
  }
}

export async function checks(assert: AssertModule): Promise<void> {
  await checkDigest(assert);
}
