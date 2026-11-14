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
import { RunHistory } from "./history.ts";

type AssertModule = typeof Assert;

const Cpu = "tests/Compiler/App.Cpu";
const Cuda = "tests/Compiler/App.Cuda";
/** A job in a worktree of a made-up Windows checkout (the scheduler keeps the worktree in lower case there). */
const Windows: KeyContext = { cwd: "C:\\work\\repo\\wt\\feat-a", worktree: "c:\\work\\repo\\wt\\feat-a" };
/** The same on a Unix machine. */
const Unix: KeyContext = { cwd: "/home/dev/wt/feat-a", worktree: "/home/dev/wt/feat-a" };

const keysOf = (command: string, context: KeyContext = Windows): ReturnType<typeof commandKeys> =>
  commandKeys(command, context);

/** The command reader: steps, filters, comments, assignments, redirections, and what it cannot read. */
export function checkCommandReader(assert: AssertModule): void {
  const words = (command: string): string[][] | undefined => readCommand(command)?.map((step) => step.words);
  assert.deepStrictEqual(words("dotnet build 'a b' \"c\" > x.log 2>&1"), [["dotnet", "build", "a b", "c"]]);
  assert.deepStrictEqual(words("A=1 B='x y' dotnet test &> l *> m >> n"), [["dotnet", "test"]]);
  assert.deepStrictEqual(words("a && b || c; d\ne"), [["a"], ["b"], ["c"], ["d"], ["e"]], "all the separators");
  assert.deepStrictEqual(
    words("dotnet build X 2>&1 | grep -E 'a|b' | tail -3; echo done"),
    [
      ["dotnet", "build", "X"],
      ["echo", "done"],
    ],
    "the stages after a pipe are filters, and a pipe in quotes is a word",
  );
  assert.deepStrictEqual(words("dotnet build X # intent: a reason; with a separator"), [["dotnet", "build", "X"]]);
  assert.deepStrictEqual(words("echo a#b"), [["echo", "a#b"]], "# inside a word is no comment");
  assert.deepStrictEqual(words("$env:CI='true'; dotnet build X"), [["$env:CI=true"], ["dotnet", "build", "X"]]);
  for (const unreadable of [
    "dotnet build 'x",
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
  const plain = keysOf(`dotnet build ${Cpu}`).normalized;
  assert.strictEqual(plain, `dotnet build ${Cpu}`);
  const same = (text: string, why: string, context: KeyContext = Windows): void =>
    assert.strictEqual(keysOf(text, context).normalized, plain, why);
  same(`dotnet build ${Cpu} -nodeReuse:false > tmp/a.log 2>&1`, "redirections and their files");
  same(
    `dotnet build ${Cpu} -m:6 -nodeReuse:false -v:q -nologo --logger trx -l console &> tmp/a.log`,
    "dropped options",
  );
  same(`CI=true DiffEngine_Disabled=true dotnet build ${Cpu}`, "assignments of bash");
  same(`export CI=true; dotnet build ${Cpu}`, "export of bash");
  same(`$env:CI='true'; dotnet build ${Cpu} *> tmp/a.log`, "assignment and *> of PowerShell");
  same(`dotnet build "${Cpu}" 2>&1 | grep -E 'error' | tail -5`, "quotes and a pipe into a filter");
  same(`dotnet build ${Cpu} # intent: a reason`, "the intent comment");
  same(`dotnet build ./${Cpu}`, "a leading ./");
  same(`dotnet build C:/work/repo/wt/feat-a/${Cpu}`, "an absolute path inside the worktree");
  same("dotnet build C:\\work\\repo\\wt\\feat-a\\tests\\Compiler\\App.Cpu", "backslashes");
  same(`dotnet build /c/work/repo/wt/feat-a/${Cpu}`, "Git Bash spelling");
  same(`dotnet build /mnt/c/work/repo/wt/feat-a/${Cpu}`, "WSL spelling");
  same("cd tests && dotnet build Compiler/App.Cpu", "a cd before the build");
  same("dotnet build Compiler/App.Cpu", "the job runs in a subdirectory", {
    cwd: "C:\\work\\repo\\wt\\feat-a\\tests",
    worktree: Windows.worktree,
  });
  same(`dotnet build /home/dev/wt/feat-a/${Cpu}`, "Unix: an absolute path inside the worktree", Unix);
  assert.strictEqual(
    keysOf(`dotnet build C:/work/repo/wt/feat-b/${Cpu}`).normalized,
    `dotnet build C:/work/repo/wt/feat-b/${Cpu}`,
    "a path of another worktree stays as written",
  );
  assert.strictEqual(
    keysOf(`dotnet test ${Cpu} --no-build -c Release`).normalized,
    keysOf(`dotnet test -c Release --no-build ${Cpu}`).normalized,
    "the order of the options",
  );
  assert.notStrictEqual(keysOf(`dotnet build ${Cuda}`).normalized, plain, "another project");
  assert.notStrictEqual(keysOf(`dotnet build ${Cpu} -c Release`).normalized, plain, "the configuration is work");
  assert.notStrictEqual(keysOf(`dotnet test ${Cpu} --no-build`).normalized, keysOf(`dotnet test ${Cpu}`).normalized);
  assert.strictEqual(
    keysOf(`dotnet build ${Cpu}; dotnet test ${Cpu} --no-build`).normalized,
    keysOf(`dotnet build ${Cpu} && dotnet test ${Cpu} --no-build`).normalized,
    "PowerShell and bash separators",
  );
  assert.notStrictEqual(
    keysOf(`dotnet test ${Cpu} --filter "A|B"`).normalized,
    keysOf(`dotnet test ${Cpu} --filter "A|C"`).normalized,
    "the filter value is part of the work",
  );
  assert.strictEqual(
    keysOf(`dotnet test ${Cpu} --filter "A|B"`).shape,
    keysOf(`dotnet test ${Cpu} --filter "A|C"`).shape,
    "but not of the shape",
  );
}

/** The shape names the steps and what changes their time; the kind only the steps. */
export function checkEstimateShape(assert: AssertModule): void {
  assert.strictEqual(keysOf(`dotnet build ${Cpu} -c Release`).shape, "build:App.Cpu", "-c takes a value");
  assert.strictEqual(keysOf(`CI=true dotnet test ${Cpu} --no-build --filter 'A|B' > x.log`).shape, "test:App.Cpu:nb:f");
  assert.strictEqual(keysOf(`dotnet test ${Cpu}`).shape, "test:App.Cpu", "unfiltered, with build");
  assert.strictEqual(
    keysOf(`cd tests && dotnet build ${Cpu}; grep error a.log; dotnet test ${Cpu} --no-build`).shape,
    "build:App.Cpu&test:App.Cpu:nb",
    "steps of no weight (cd, grep) do not count",
  );
  assert.strictEqual(keysOf("dotnet build a.csproj || dotnet build b.csproj").shape, "build:a&build:b", "||");
  assert.strictEqual(keysOf("just test-category Runtime").shape, "just:test-category");
  assert.strictEqual(keysOf("node tools/x/fuzz.js --minutes 5").shape, "node:fuzz");
  assert.strictEqual(keysOf("dotnet run --project tools/x").shape, "dotnet:run");
  assert.strictEqual(keysOf(`dotnet build ${Cuda} && dotnet test ${Cuda} --no-build`).kind, "build&test");
  assert.deepStrictEqual(keysOf("echo hi"), { normalized: "", shape: "", kind: "" }, "trivial steps only");
  // A command that cannot be read has the kind of its first program and nothing else
  assert.deepStrictEqual(keysOf("dotnet build $(pwd)/X"), { normalized: "", shape: "", kind: "dotnet" });
  assert.deepStrictEqual(keysOf("dotnet build 'x"), { normalized: "", shape: "", kind: "dotnet" });
  // An entry of an old history has no directory: paths stay as written
  assert.strictEqual(commandKeys(`dotnet build ${Cpu}`, { cwd: "", worktree: "" }).normalized, `dotnet build ${Cpu}`);
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
    rec(`dotnet build ${Cpu} > a.log 2>&1`, sec(200));
    rec(`dotnet build ${Cpu} -m:4 > b.log`, sec(300));
    rec(`dotnet build ${Cuda} > c.log`, sec(90));
    rec(`dotnet test ${Cpu} --no-build --filter A`, sec(10));
    const from = (command: string): string | undefined => history.estimateWithFallback(command, Windows)?.from;
    assert.strictEqual(from(`dotnet build ${Cpu} > a.log 2>&1`), "exact");
    assert.deepStrictEqual(
      history.estimateWithFallback(`dotnet build ${Cpu} > a.log 2>&1`, Windows)?.typicalMs,
      sec(200),
    );

    // normalized: the same text but for a log file name and an option; the median of the two commands' times
    const normalized = history.estimateWithFallback(`dotnet build ${Cpu} -nodeReuse:false > other.log`, Windows);
    assert.strictEqual(normalized?.from, "normalized");
    assert.strictEqual(normalized?.typicalMs, sec(250), "median of the matched commands' typical times");
    assert.strictEqual(normalized?.longestMs, sec(250), "median of their longest times");
    // shape: another project of the same kind is not the same shape; a filter value is not part of it
    assert.strictEqual(from(`dotnet test ${Cpu} --no-build --filter B`), "shape");
    assert.strictEqual(
      history.estimateWithFallback(`dotnet test ${Cpu} --no-build --filter B`, Windows)?.typicalMs,
      sec(10),
    );
    // kind: a project nobody built, the same steps
    const kind = history.estimateWithFallback(`dotnet build tests/Compiler/App.Integration`, Windows);
    assert.strictEqual(kind?.from, "kind");
    assert.strictEqual(kind?.typicalMs, sec(200), "median of the three builds: 200 s, 300 s, 90 s");
    // nothing alike
    assert.strictEqual(history.estimateWithFallback("python make-art.py", Windows), null);
    assert.strictEqual(
      history.estimateWithFallback("echo hi", Windows),
      null,
      "a command with no step of weight has no kind",
    );

    // The keys are made in the job's directory and kept with the entry: a path written absolute matches a relative one
    rec("dotnet build C:/work/repo/wt/feat-a/tests/Compiler/App.Other", sec(70));
    const relative = history.estimateWithFallback("dotnet build tests/Compiler/App.Other -m:2", Windows);
    assert.strictEqual(relative?.from, "normalized");
    assert.strictEqual(relative?.typicalMs, sec(70));
    assert.strictEqual(
      new RunHistory(file, () => {}).estimateWithFallback("dotnet build ./tests/Compiler/App.Other", Windows)?.from,
      "normalized",
      "also after a reload",
    );

    // The groups follow the history: a record made after a lookup counts
    rec(`dotnet test ${Cuda} --no-build --filter A`, sec(40));
    assert.strictEqual(from(`dotnet test ${Cuda} --no-build --filter Z`), "shape");
    assert.strictEqual(
      history.estimateWithFallback(`dotnet test ${Cuda} --no-build --filter Z`, Windows)?.typicalMs,
      sec(40),
    );

    // A history written by the version before this one (no accuracy file, plain commands) loads and falls back
    fs.writeFileSync(
      file,
      JSON.stringify({ nextId: 5, commands: { "dotnet build x/A": { runs: [100, 300], lastUsed: 1 } } }),
    );
    fs.rmSync(path.join(path.dirname(file), "accuracy.json"), { force: true });
    const old = new RunHistory(file, () => {});
    assert.strictEqual(old.nextId, 5);
    assert.strictEqual(old.estimateWithFallback("dotnet build x/A > l.log", Windows)?.from, "normalized");
    assert.strictEqual(old.estimateWithFallback("dotnet build x/A -c Release", Windows)?.from, "shape");
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
};
