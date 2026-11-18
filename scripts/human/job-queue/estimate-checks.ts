// Unit checks of the command reader, the estimate keys (estimate-key.ts), the fallback (estimate-fallback.ts,
// RunHistory.estimateWithFallback) and the accuracy log, run by the self-test through SchedulerChecks. Every path in
// them is made up.

import type * as Assert from "node:assert";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { readCommand } from "./command-reader.ts";
import { AccuracyLog, formatAccuracy, summarizeAccuracy } from "./estimate-fallback.ts";
import { commandKeys, type KeyContext } from "./estimate-key.ts";
import { EstimatePlaceChecks } from "./estimate-place-checks.ts";
import { RunHistory } from "./history.ts";

type AssertModule = typeof Assert;

const ScriptA = "scripts/human/app.js";
const ScriptB = "scripts/human/other.ts";
/** A job in a worktree of a made-up Windows checkout (the scheduler keeps the worktree in lower case there). */
const Windows: KeyContext = { cwd: "C:\\work\\repo\\wt\\feat-a", worktree: "c:\\work\\repo\\wt\\feat-a" };
/** The same on a Unix machine. */
const Unix: KeyContext = { cwd: "/home/dev/wt/feat-a", worktree: "/home/dev/wt/feat-a" };

const keysOf = (command: string, context: KeyContext = Windows): ReturnType<typeof commandKeys> =>
  commandKeys(command, context);

/** The command reader: steps, filters, comments, assignments, redirections, and what it cannot read. */
export function checkCommandReader(assert: AssertModule): void {
  const words = (command: string): string[][] | undefined => readCommand(command)?.map((step) => step.words);
  assert.deepStrictEqual(words("tool build 'a b' \"c\" > x.log 2>&1"), [["tool", "build", "a b", "c"]]);
  assert.deepStrictEqual(words("A=1 B='x y' tool test &> l *> m >> n"), [["tool", "test"]]);
  assert.deepStrictEqual(words("a && b || c; d\ne"), [["a"], ["b"], ["c"], ["d"], ["e"]], "all the separators");
  assert.deepStrictEqual(
    words("tool build X 2>&1 | grep -E 'a|b' | tail -3; echo done"),
    [
      ["tool", "build", "X"],
      ["echo", "done"],
    ],
    "the stages after a pipe are filters, and a pipe in quotes is a word",
  );
  assert.deepStrictEqual(words("tool build X # intent: a reason; with a separator"), [["tool", "build", "X"]]);
  assert.deepStrictEqual(words("echo a#b"), [["echo", "a#b"]], "# inside a word is no comment");
  assert.deepStrictEqual(words("$env:CI='true'; tool build X"), [["$env:CI=true"], ["tool", "build", "X"]]);
  for (const unreadable of [
    "tool build 'x",
    "echo $(pwd)",
    "echo `pwd`",
    "cat <<END",
    "a < b",
    "(a && b)",
    "(a",
    "a b)",
    "a &",
  ]) {
    assert.strictEqual(readCommand(unreadable), null, unreadable);
  }
  assert.deepStrictEqual(words("echo 'a $(b)' \"c|d\""), [["echo", "a $(b)", "c|d"]], "quotes hide the syntax");
}

/** The normalized key is the work: the same for the forms that differ only in what does not change it. */
export function checkEstimateNormalize(assert: AssertModule): void {
  const plain = keysOf(`node ${ScriptA}`).normalized;
  assert.strictEqual(plain, `node ${ScriptA}`);
  const same = (text: string, why: string, context: KeyContext = Windows): void =>
    assert.strictEqual(keysOf(text, context).normalized, plain, why);
  same(`node ${ScriptA} > tmp/a.log 2>&1`, "redirections and their files");
  same(`CI=true node ${ScriptA}`, "assignments of bash");
  same(`export CI=true; node ${ScriptA}`, "export of bash");
  same(`$env:CI='true'; node ${ScriptA} *> tmp/a.log`, "assignment and *> of PowerShell");
  same(`node "${ScriptA}" 2>&1 | grep -E 'error' | tail -5`, "quotes and a pipe into a filter");
  same(`node ${ScriptA} # intent: a reason`, "the intent comment");
  same(`node ./${ScriptA}`, "a leading ./");
  same(`node C:/work/repo/wt/feat-a/${ScriptA}`, "an absolute path inside the worktree");
  same("node C:\\work\\repo\\wt\\feat-a\\scripts\\human\\app.js", "backslashes");
  same(`node /c/work/repo/wt/feat-a/${ScriptA}`, "Git Bash spelling");
  same(`node /mnt/c/work/repo/wt/feat-a/${ScriptA}`, "WSL spelling");
  same("cd scripts && node human/app.js", "a cd before the script");
  same("node human/app.js", "the job runs in a subdirectory", {
    cwd: "C:\\work\\repo\\wt\\feat-a\\scripts",
    worktree: Windows.worktree,
  });
  same(`node /home/dev/wt/feat-a/${ScriptA}`, "Unix: an absolute path inside the worktree", Unix);
  assert.strictEqual(
    keysOf(`node C:/work/repo/wt/feat-b/${ScriptA}`).normalized,
    `node C:/work/repo/wt/feat-b/${ScriptA}`,
    "a path of another worktree stays as written",
  );
  assert.notStrictEqual(keysOf(`node ${ScriptB}`).normalized, plain, "another script");
  assert.notStrictEqual(keysOf(`node ${ScriptA} --flag`).normalized, plain, "script arguments are work");
  assert.strictEqual(
    keysOf(`node ${ScriptA}; node ${ScriptB}`).normalized,
    keysOf(`node ${ScriptA} && node ${ScriptB}`).normalized,
    "PowerShell and bash separators",
  );
}

/** The shape names the steps and what changes their time; the kind only the steps. */
export function checkEstimateShape(assert: AssertModule): void {
  assert.strictEqual(keysOf("node tools/x/fuzz.js --minutes 5").shape, "node:fuzz");
  assert.strictEqual(
    keysOf(`cd scripts && node ${ScriptA}; grep error a.log; node -p 1`).shape,
    "node:app&node:eval",
    "steps of no weight (cd, grep) do not count",
  );
  assert.strictEqual(keysOf("node a.js || node b.ts").shape, "node:a&node:b", "||");
  assert.deepStrictEqual(keysOf("echo hi"), { normalized: "", shape: "", kind: "" }, "trivial steps only");
  // A script outside the worktree (a scratch script) is not the repository's work: a kind of its own
  assert.strictEqual(keysOf("node C:\\scratch\\probe.mjs 1").kind, "node:outside");
  assert.strictEqual(keysOf("node ../other/probe.js").kind, "node:outside", "relative, still outside");
  assert.strictEqual(keysOf("node scripts/x.js").kind, "node");
  assert.strictEqual(keysOf("node C:\\scratch\\a.js", { cwd: "", worktree: "" }).kind, "node", "no context");
  // Inline code (a probe, a sleep) is its own kind too
  assert.strictEqual(keysOf('node -e "setTimeout(()=>0,5000)"').kind, "node:eval");
  assert.strictEqual(keysOf("node -p 1").kind, "node:eval");
  // Without a plugin, commands for unknown programs fall back to the core generic key
  assert.deepStrictEqual(keysOf("make build X"), {
    normalized: "make build X",
    shape: "make",
    kind: "make",
  });
  // A command that cannot be read has the kind of its first program and nothing else
  assert.deepStrictEqual(keysOf("node $(pwd)/x.js"), { normalized: "", shape: "", kind: "node" });
  assert.deepStrictEqual(keysOf("node 'x"), { normalized: "", shape: "", kind: "node" });
  // An entry of an old history has no directory: paths stay as written
  assert.strictEqual(commandKeys(`node ${ScriptA}`, { cwd: "", worktree: "" }).normalized, `node ${ScriptA}`);
}

function withHistory(run: (file: string, history: RunHistory) => void): void {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "job-queue-estimate-"));
  try {
    const file = path.join(directory, "history.json");
    run(file, new RunHistory(file, () => {}));
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
}

/** The estimate of a command with no record comes from the narrowest level that has one. */
export function checkEstimateFallback(assert: AssertModule): void {
  withHistory((file, history) => {
    const sec = (n: number): number => n * 1000;
    const rec = (command: string, ms: number): void => history.record(command, ms, undefined, 1, Windows);
    rec(`node ${ScriptA} > a.log 2>&1`, sec(200));
    rec(`node ${ScriptA} &> b.log`, sec(300));
    rec(`node ${ScriptB} > c.log`, sec(90));
    rec(`node ${ScriptA} --case A > d.log`, sec(10));
    const from = (command: string): string | undefined => history.estimateWithFallback(command, Windows)?.from;
    assert.strictEqual(from(`node ${ScriptA} > a.log 2>&1`), "exact");
    assert.deepStrictEqual(history.estimateWithFallback(`node ${ScriptA} > a.log 2>&1`, Windows)?.typicalMs, sec(200));

    // normalized: the same text but for a log file name; the median of the two commands' times
    const normalized = history.estimateWithFallback(`node ${ScriptA} > other.log`, Windows);
    assert.strictEqual(normalized?.from, "normalized");
    assert.strictEqual(normalized?.typicalMs, sec(250), "median of the matched commands' typical times");
    assert.strictEqual(normalized?.longestMs, sec(250), "median of their longest times");
    // shape: another argument of the same script is not the same normalized key, same shape
    assert.strictEqual(from(`node ${ScriptA} --case B`), "shape");
    assert.strictEqual(history.estimateWithFallback(`node ${ScriptA} --case B`, Windows)?.typicalMs, sec(200));
    // kind: a script nobody ran, the same kind of step
    const kind = history.estimateWithFallback("node scripts/human/other2.js", Windows);
    assert.strictEqual(kind?.from, "kind");
    assert.strictEqual(kind?.typicalMs, sec(145), "median of the four node runs: 10 s, 90 s, 200 s, 300 s");
    // A scratch script outside the worktree and inline node code never take the repository's script times
    assert.strictEqual(history.estimateWithFallback("node C:\\scratch\\probe.mjs", Windows), null);
    assert.strictEqual(history.estimateWithFallback('node -e "setTimeout(()=>0,5000)"', Windows), null);
    // nothing alike
    assert.strictEqual(history.estimateWithFallback("python make-art.py", Windows), null);
    assert.strictEqual(
      history.estimateWithFallback("echo hi", Windows),
      null,
      "a command with no step of weight has no kind",
    );

    // The keys are made in the job's directory and kept with the entry: a path written absolute matches a relative one
    rec("node C:/work/repo/wt/feat-a/scripts/human/new.js", sec(70));
    const relative = history.estimateWithFallback("node scripts/human/new.js > l.log", Windows);
    assert.strictEqual(relative?.from, "normalized");
    assert.strictEqual(relative?.typicalMs, sec(70));
    assert.strictEqual(
      new RunHistory(file, () => {}).estimateWithFallback("node ./scripts/human/new.js", Windows)?.from,
      "normalized",
      "also after a reload",
    );

    // A history written by the version before this one (no accuracy file, plain commands) loads and falls back
    fs.writeFileSync(
      file,
      JSON.stringify({ nextId: 5, commands: { "node x/a.js": { runs: [100, 300], lastUsed: 1 } } }),
    );
    fs.rmSync(path.join(path.dirname(file), "accuracy.json"), { force: true });
    const old = new RunHistory(file, () => {});
    assert.strictEqual(old.nextId, 5);
    assert.strictEqual(old.estimateWithFallback("node x/a.js > l.log", Windows)?.from, "normalized");
    assert.strictEqual(old.estimateWithFallback("node x/a.js --flag", Windows)?.from, "shape");
    assert.deepStrictEqual(old.accuracy.entries, [], "no accuracy file: an empty log");
  });
}

/** The accuracy log keeps the last entries, survives damage and sums up per level. */
export function checkEstimateAccuracy(assert: AssertModule): void {
  withHistory((file, history) => {
    history.accuracy.add("shape", 100, 100, 1);
    history.accuracy.add("shape", 100, 400, 2);
    history.accuracy.add("none", 60000, 1000, 3);
    history.accuracy.add("exact", 1000, 1200, 4);
    const rows = summarizeAccuracy(new RunHistory(file, () => {}).accuracy.entries);
    assert.deepStrictEqual(
      rows.map((r) => r.level),
      ["exact", "shape", "none"],
      "levels in order, each with its own entries",
    );
    const shape = rows.find((r) => r.level === "shape");
    assert.deepStrictEqual(
      [shape?.runs, shape?.medianErrorPercent, shape?.within50Percent],
      [2, 38, 50],
      "errors 0% and 75%: median 37.5, one of two within 50%",
    );
    assert.strictEqual(rows.find((r) => r.level === "none")?.medianErrorPercent, 5900);
    // Times off counts both ways alike: 100 for 400 is x4, 60000 for 1000 is x60
    assert.strictEqual(shape?.medianFactor, 2.5, "x1 and x4: median 2.5");
    assert.strictEqual(rows.find((r) => r.level === "none")?.medianFactor, 60);
    assert.ok(formatAccuracy(rows).includes("x60.0"), "the table shows times off");
    assert.ok(formatAccuracy(rows).includes("shape"), "the table names the levels");
    assert.ok(formatAccuracy([]).includes("no estimate"), "an empty log says so");

    const accuracyFile = path.join(path.dirname(file), "accuracy.json");
    for (let i = 0; i < 1100; i++) {
      history.accuracy.add("kind", 10, 10, i);
    }
    assert.strictEqual(AccuracyLog.read(accuracyFile).length, 1000, "the log is bounded");
    assert.strictEqual(history.accuracy.entries.length, 1000, "also in memory");
    assert.strictEqual((JSON.parse(fs.readFileSync(accuracyFile, "utf8")) as unknown[]).length, 1000, "and on disk");
    fs.writeFileSync(accuracyFile, JSON.stringify([[1, "kind", 5, 6], [1, "bogus", 5, 6], "x", [1, "kind", -1, 6]]));
    assert.strictEqual(AccuracyLog.read(accuracyFile).length, 1, "damaged entries are dropped");
    fs.writeFileSync(accuracyFile, "{not json");
    assert.deepStrictEqual(AccuracyLog.read(accuracyFile), [], "an unreadable file is an empty log");
  });
}

export const EstimateChecks: Record<string, (assert: AssertModule) => void> = {
  checkCommandReader,
  checkEstimateNormalize,
  checkEstimateShape,
  checkEstimateFallback,
  checkEstimateAccuracy,
  ...EstimatePlaceChecks,
};
