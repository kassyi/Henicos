// The self-test of the live view (status --watch): the screen drawn from a fixed status at the size of a small terminal
// pane, the narrow and short layouts, and the keys. No scheduler and no terminal: view.ts and reduceKey are pure.

import * as assert from "node:assert";
import type { StatusJob, StatusReply } from "./protocol.ts";
import {
  displayWidth,
  fit,
  initialViewState,
  renderScreen,
  screenJobs,
  shortCommand,
  timelineCells,
  timeWindow,
  type ViewState,
} from "./view.ts";
import { describeResult, reduceKey, splitKeys } from "./watch.ts";

const Now = Date.UTC(2026, 10, 14, 5, 32, 0);
const Minute = 60 * 1000;

function job(id: number, fields: Partial<StatusJob>): StatusJob {
  return {
    id,
    weight: 1,
    gpu: false,
    pid: 100 + id,
    commandPid: null,
    orphan: false,
    cwd: "P:/repo",
    command: `dotnet build project-${id}`,
    queuedAt: Now - 20 * Minute,
    grantedAt: null,
    typicalMs: 10 * Minute,
    pin: null,
    before: null,
    after: null,
    keepIfFailed: false,
    gpuTurn: false,
    memoryMb: 0,
    state: "waiting",
    ahead: 0,
    position: 0,
    startAt: null,
    endAt: null,
    blockedBy: null,
    ...fields,
  };
}

/** Three running jobs (one late, one without history) and three waiting ones (one behind --after). */
function sampleReply(): StatusReply {
  return {
    type: "status",
    pid: 1,
    now: Now,
    policy: "hrrn",
    capacity: 8,
    used: 4,
    memoryCapacity: 32000,
    memoryUsed: 6000,
    running: [
      job(812, {
        state: "running",
        gpu: true,
        weight: 2,
        memoryMb: 6144,
        command: "just test-category Runtime",
        grantedAt: Now - 7 * Minute,
        endAt: Now + 3 * Minute,
      }),
      job(815, { state: "running", grantedAt: Now - 14 * Minute, endAt: Now + Minute }), // past its typical 10 m
      job(817, { state: "running", typicalMs: null, grantedAt: Now - 6 * Minute, endAt: Now + Minute / 2 }),
    ],
    waiting: [
      job(820, { ahead: 0, position: 1, startAt: Now + 3 * Minute, endAt: Now + 6 * Minute }),
      job(821, {
        gpu: true,
        gpuTurn: true,
        ahead: 1,
        position: 2,
        startAt: Now + 4 * Minute,
        endAt: Now + 19 * Minute,
      }),
      job(822, {
        after: 821,
        blockedBy: 821,
        ahead: 2,
        position: 3,
        startAt: Now + 19 * Minute,
        endAt: Now + 49 * Minute,
      }),
    ],
  };
}

const plain = (lines: string[]): string[] => lines;

function checkLayout(): void {
  const reply = sampleReply();
  // The pane of a split VS Code panel: 70 columns, 13 rows; #812 selected
  const state: ViewState = { ...initialViewState(), selectedId: 812 };
  const lines = plain(renderScreen(reply, state, { columns: 70, rows: 13, now: Now }, false));
  assert.strictEqual(lines.length, 13, "the screen fills the rows exactly, so nothing scrolls");
  lines.forEach((line, i) => assert.ok(displayWidth(line) <= 70, `line ${i} fits 70 columns: ${line}`));
  // Now is one column, marked on the axis and in every job line, past on its left and the expected rest on its right
  const nowColumn = [...(lines[1] ?? "")].indexOf("▼");
  assert.ok(nowColumn > 0 && (lines[1] ?? "").includes("▼now"), `the axis marks now: ${lines[1]}`);
  lines
    .slice(2, 8) // the six jobs
    .forEach((line) => assert.match([...line][nowColumn] ?? "", /[│┃]/, `now in every job line: ${line}`));
  const first = [...(lines[2] ?? "")];
  assert.ok(
    first.slice(0, nowColumn).includes("█") && first.slice(nowColumn + 1).includes("▓"),
    "run left, rest right",
  );
  assert.match(lines[0] ?? "", /CPU ■■■■□□□□ 4\/8 {2}MEM ■□□□□□□ 19% {2}GPU #812 \+1/);
  assert.match(lines[2] ?? "", /^▸812G test-category Runtime/, "running jobs first, the GPU holder flagged");
  assert.ok((lines[2] ?? "").includes("█") && (lines[2] ?? "").includes("▓"), "run so far and the expected rest");
  assert.ok((lines[4] ?? "").includes("?"), "a job without history shows no expected rest");
  assert.ok((lines[6] ?? "").includes("⋯") && (lines[6] ?? "").includes("▒"), "waits for the GPU, then runs");
  assert.ok(!(lines[5] ?? "").includes("⋯"), "a job that only waits for slots shows no ⋯");
  assert.match(lines[7] ?? "", /⋯▒+ +$/, "the window reaches the last expected end");
  assert.match(lines[12] ?? "", /^ #812 70% ends ~\d\d:\d\d {2}w2 6\.0GB gpu {2}just test-category Runtime/);

  // Colors: the selected line in reverse video, the late job in red; no color codes without color
  const selected812: ViewState = { ...initialViewState(), selectedId: 812 };
  const colored = renderScreen(reply, selected812, { columns: 70, rows: 13, now: Now }, true);
  assert.ok((colored[2] ?? "").includes("\x1b[7"), "the selected job is drawn in reverse video");
  assert.ok((colored[3] ?? "").includes("\x1b[31m█"), "a job past its typical time is red");
  assert.ok(!lines.join("").includes("\x1b"), "no escape codes without color");

  // Nothing selected (the start): no marker, no reverse video, the queue summed up at the bottom
  const full = { columns: 70, rows: 13, now: Now };
  const none = renderScreen(reply, initialViewState(), full, false);
  assert.ok(!none.join("").includes("▸"), "no job is marked");
  assert.ok(!renderScreen(reply, initialViewState(), full, true).join("").includes("\x1b[7"), "no reverse video");
  assert.match(none[12] ?? "", /^ 3 running, 3 waiting; next end ~\d\d:\d\d #817 {2}red = longer than usual/);
  assert.ok(
    (renderScreen(reply, initialViewState(), full, true)[12] ?? "").includes("\x1b[31m  red = longer than usual"),
    "the legend of the red bar is red itself",
  );
  // Without a late job the bottom line says how to select one instead
  const onTime = { ...reply, running: reply.running.map((j) => ({ ...j, grantedAt: Now - Minute })) };
  assert.match(plain(renderScreen(onTime, initialViewState(), full, false))[12] ?? "", / {2}↑↓ select a job/);
  // A selected job that ended selects nothing, instead of moving the selection to another job
  assert.deepStrictEqual(renderScreen(reply, { ...initialViewState(), selectedId: 700 }, full, false), none);

  // Fewer rows than jobs: the list follows the selection and the rule says how many are hidden
  const scrolled: ViewState = { ...initialViewState(), selectedId: 822 };
  const short = renderScreen(reply, scrolled, { columns: 70, rows: 7, now: Now }, false);
  assert.strictEqual(short.length, 7);
  assert.match(short[4] ?? "", /^▸822/, "the selected job stays on screen");
  assert.match(short[5] ?? "", /^── \+3 more /);
  assert.strictEqual(scrolled.offset, 3);

  // Narrow: no timeline, the progress as text
  const narrow = renderScreen(reply, initialViewState(), { columns: 40, rows: 13, now: Now }, false);
  narrow.forEach((line) => assert.ok(displayWidth(line) <= 40, line));
  assert.ok(!narrow.join("").includes("▒"), "no timeline at 40 columns");
  assert.match(narrow[3] ?? "", /815.*14m \+4m/);
  assert.match(narrow[7] ?? "", /822.*after #821/);

  // Very short: the header and the bottom line only; no scheduler
  assert.strictEqual(renderScreen(reply, initialViewState(), { columns: 70, rows: 3, now: Now }, false).length, 2);
  const idle = renderScreen(null, initialViewState(), { columns: 70, rows: 13, now: Now }, false);
  assert.match(idle[0] ?? "", /no scheduler \(idle\)/);
  assert.match(idle[12] ?? "", /no jobs/);
}

function checkPieces(): void {
  assert.strictEqual(fit("abcdef", 4), "abc…");
  assert.strictEqual(fit("ab", 4), "ab  ");
  assert.strictEqual(fit("日本語の名前", 7), "日本語…", "a wide character takes two cells");
  assert.strictEqual(displayWidth(fit("日本語の名前", 7)), 7);
  assert.strictEqual(fit("日本語の名前", 6), "日本… ", "a wide character that would pass the width is left out");
  assert.strictEqual(
    shortCommand("dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu/Kassyi.GSharp.Tests.Cpu.csproj -c Release"),
    "test Tests.Cpu.csproj -c Release",
  );
  // Commands as agents queue them: assignments, export steps, redirections and the node-reuse flag are dropped
  assert.strictEqual(
    shortCommand(
      "export DiffEngine_Disabled=true; dotnet build tests/A -nodeReuse:false > tmp/b.log 2>&1 && " +
        "dotnet test tests/A --no-build > tmp/t.log 2>&1; dotnet build tests/B >x 2>&1",
    ),
    "build A && test A --no-build ; build B",
  );
  assert.strictEqual(shortCommand("CI=true DiffEngine_Disabled=true dotnet test tests/X"), "test X");
  assert.strictEqual(shortCommand("a=1 b=2"), "a=1 b=2", "nothing left: the command as it is");
  // The timeline of a running job: █ up to now, ▓ to its expected end, nothing after
  const running = job(1, { state: "running", grantedAt: Now - 5 * Minute, endAt: Now + 5 * Minute });
  const window = timeWindow([running], Now, 40);
  const cells = timelineCells(running, window, Now)
    .map((c) => c.char)
    .join("");
  assert.match(cells, /^ *█+┃▓+ *$/, cells);
  // A waiting job with no forecast draws nothing but the now marker
  const unknown = timelineCells(job(2, {}), window, Now)
    .map((c) => c.char)
    .join("");
  assert.match(unknown, /^ *│ *$/, unknown);
}

function checkKeys(): void {
  const reply = sampleReply();
  let state = initialViewState();
  const press = (key: string): ReturnType<typeof reduceKey> => {
    const result = reduceKey(state, key, reply, Now);
    state = result.state;
    return result;
  };
  assert.deepStrictEqual(splitKeys("j\x1b[Bq\x1b"), ["j", "\x1b[B", "q", "\x1b"]);
  assert.strictEqual(state.selectedId, null, "nothing is selected at the start");
  assert.match(press("c").state.message?.text ?? "", /no job selected/, "c does nothing without a selection");
  assert.strictEqual(state.prompt, null);
  press("\x1b[A");
  assert.strictEqual(state.selectedId, 822, "up from no selection selects the last job");
  press("\x1b");
  assert.strictEqual(state.selectedId, null, "Esc clears the selection");
  press("\x1b[B");
  assert.strictEqual(state.selectedId, 812, "down from no selection selects the first job");
  press("\x1b[B");
  assert.strictEqual(state.selectedId, 815, "down selects the next job");
  press("k");
  press("k");
  assert.strictEqual(state.selectedId, 812, "up stops at the first job");
  press("G");
  assert.strictEqual(state.selectedId, 822);

  // cancel asks first; anything but y drops it
  press("c");
  assert.deepStrictEqual(state.prompt, { kind: "cancel", id: 822 });
  assert.strictEqual(press("n").action, undefined);
  press("c");
  assert.strictEqual(press("\r").action, undefined, "only y confirms");
  press("c");
  assert.deepStrictEqual(press("y").action, { type: "cancel", id: 822 });

  // move: f, r, or an id + Enter; Esc leaves it
  press("m");
  assert.deepStrictEqual(press("f").action, { type: "move", id: 822, first: true });
  press("m");
  assert.deepStrictEqual(press("r").action, { type: "move", id: 822, reset: true });
  press("m");
  press("8");
  press("2");
  press("1");
  press("\x7f");
  press("0");
  assert.deepStrictEqual(press("\r").action, { type: "move", id: 822, before: 820 });
  press("m");
  press("\x1b");
  assert.strictEqual(state.prompt, null);

  // weight; a running job can neither move nor change its slots
  press("w");
  press("3");
  assert.deepStrictEqual(press("\r").action, { type: "set-weight", id: 822, weight: 3 });
  press("g");
  press("m");
  assert.strictEqual(state.prompt, null);
  assert.match(state.message?.text ?? "", /#812 already runs/);

  assert.strictEqual(press("?").state.help, true);
  assert.strictEqual(press("\x1b").state.help, false);
  assert.strictEqual(state.selectedId, 812, "Esc closes the help first and keeps the selection");
  assert.strictEqual(press("q").action, "quit");
  assert.strictEqual(press("\x03").action, "quit");
  assert.strictEqual(
    reduceKey(initialViewState(), "c", null, Now).state.message?.text,
    "no job selected; pick one with ↑↓",
  );

  assert.strictEqual(
    describeResult({ type: "cancel", id: 1 }, { type: "cancel-result", cancelled: [1] }).text,
    "cancelled #1",
  );
  assert.strictEqual(
    describeResult({ type: "cancel", id: 9 }, { type: "cancel-result", cancelled: [] }).text,
    "no job #9",
  );
  assert.match(
    describeResult({ type: "move", id: 1, first: true }, { type: "error", message: "x" }).text,
    /scheduler: x/,
  );
  assert.strictEqual(describeResult({ type: "status" }, null).text, "no scheduler running");
}

/** A --gpu job and the client it runs under the GPU lock are one line; a waiting line says what it waits for. */
function checkGpuPairsAndReasons(): void {
  const turn = job(900, {
    state: "running",
    gpuTurn: true,
    weight: 0,
    command: "dotnet test X",
    grantedAt: Now - Minute,
  });
  const holder = job(901, {
    state: "running",
    gpu: true,
    weight: 2,
    command: "dotnet test X",
    grantedAt: Now - Minute / 2,
    endAt: Now + 5 * Minute,
  });
  const other = job(902, { state: "running", command: "dotnet build Y", grantedAt: Now - Minute, endAt: Now + Minute });
  const reply: StatusReply = {
    ...sampleReply(),
    capacity: 3,
    used: 3,
    running: [turn, holder, other],
    waiting: [
      job(910, { gpuTurn: true, startAt: Now + 5 * Minute, endAt: Now + 9 * Minute }),
      job(911, { startAt: Now + Minute, endAt: Now + 2 * Minute }),
    ],
  };
  const jobs = screenJobs(reply);
  assert.deepStrictEqual(
    jobs.map((j) => [j.id, j.holderId, j.gpu, j.weight, j.reason]),
    [
      [900, 901, true, 2, ""],
      [902, null, false, 1, ""],
      [910, null, true, 1, "GPU busy"],
      [911, null, false, 1, "CPU full"],
    ],
    "the holder joins the line of its --gpu job, which keeps the id its caller knows",
  );
  // With free slots: a test run whose budget does not fit waits for memory, any other for the jobs ahead of it
  const roomy: StatusReply = {
    ...reply,
    used: 1,
    memoryCapacity: 1000,
    memoryUsed: 900,
    waiting: [job(912, { memoryMb: 200 }), job(913, { memoryMb: 50 }), job(914, { blockedBy: 911 })],
  };
  assert.deepStrictEqual(
    screenJobs(roomy)
      .filter((j) => j.state === "waiting")
      .map((j) => j.reason),
    ["mem full", "in line", "after #911"],
  );
  const lines = renderScreen(
    reply,
    { ...initialViewState(), selectedId: 900 },
    { columns: 70, rows: 9, now: Now },
    false,
  );
  assert.match(lines[0] ?? "", /GPU #900 \+1/, "the GPU belongs to #900; one job waits for its turn");
  assert.match(lines[2] ?? "", /^▸900G/);
  assert.match(lines[4] ?? "", /^ 910G.*GPU busy [│┃]/, "a waiting line names what it waits for, left of now");
  assert.match(lines[5] ?? "", /^ 911 .*CPU full [│┃]/);
  assert.match(lines[8] ?? "", /^ #900 .* runs as #901 /);
  // A --gpu job whose holder has not registered yet (the lock is being taken) is still a GPU line
  const alone = screenJobs({ ...reply, running: [turn] });
  assert.deepStrictEqual(
    alone.map((j) => [j.id, j.holderId, j.gpu]),
    [
      [900, null, true],
      [910, null, true],
      [911, null, false],
    ],
  );
  // A holder whose command differs from every --gpu job is its own line (a run --gpu inside gpu-lock.js)
  const unpaired = screenJobs({ ...reply, running: [turn, { ...holder, command: "dotnet test Z" }] });
  assert.deepStrictEqual(
    unpaired.map((j) => j.id),
    [900, 901, 910, 911],
  );
}

/** s asks before it starts a waiting job, and says at once why a GPU turn, a blocked or a running job cannot start. */
function checkStartKey(): void {
  const reply = sampleReply();
  const at = (id: number): ViewState => ({ ...initialViewState(), selectedId: id });
  const asked = reduceKey(at(820), "s", reply, Now).state;
  assert.deepStrictEqual(asked.prompt, { kind: "start", id: 820 });
  assert.deepStrictEqual(reduceKey(asked, "y", reply, Now).action, { type: "start", id: 820 });
  assert.strictEqual(reduceKey(asked, "n", reply, Now).action, undefined);
  const lines = renderScreen(reply, asked, { columns: 90, rows: 13, now: Now }, false);
  assert.match(lines[12] ?? "", /start #820 now, over the pool \(it waits: in line\)\? y\/n/);
  assert.match(reduceKey(at(821), "s", reply, Now).state.message?.text ?? "", /#821 waits for the GPU/);
  assert.match(reduceKey(at(822), "s", reply, Now).state.message?.text ?? "", /#822 waits for #821 to end/);
  assert.match(reduceKey(at(812), "s", reply, Now).state.message?.text ?? "", /#812 already runs; .* can start/);
  assert.strictEqual(
    describeResult({ type: "start", id: 820 }, { type: "start-result", id: 820 }).text,
    "#820 started over the pool",
  );
}

export function checkView(): void {
  checkStartKey();
  checkPieces();
  checkLayout();
  checkKeys();
  checkGpuPairsAndReasons();
}
