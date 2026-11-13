// The self-test of the chat panel of the live view (view-chats.ts): the rows of the chats that wait for the person,
// their order and styles, the one-line summary of a short screen, the job ids of a chat's worktree, the hide key, and
// no panel when no chat waits. Drawn from the fixed chats of view-sample.ts.

import * as assert from "node:assert";
import type { StatusReply } from "./protocol.ts";
import { chatJobs, pathKey, waitingChats, type ChatView } from "./view-chats.ts";
import { displayWidth, initialViewState, renderScreen, screenJobs, type ViewState } from "./view.ts";
import { reduceKey } from "./watch.ts";
import { LaneWorktree, MainCheckout, Minute, Now, PanelWorktree, chat, job, sampleChats } from "./view-sample.ts";

/** Jobs of three worktrees, spelled as the scheduler keys them on Windows or as a shell gives the cwd. */
function linkedReply(): StatusReply {
  return {
    type: "status",
    pid: 1,
    now: Now,
    policy: "hrrn",
    capacity: 8,
    used: 2,
    running: [
      // the scheduler's key: lower case with backslashes
      job(900, {
        state: "running",
        worktree: "p:\\repo\\tmp\\worktree\\issue-1851-panel",
        grantedAt: Now - Minute,
        endAt: Now + Minute,
      }),
      job(901, { state: "running", worktree: "p:\\repo", grantedAt: Now - Minute, endAt: Now + Minute }),
    ],
    waiting: [
      // an older scheduler sends no worktree: the cwd, another case and a trailing separator
      job(902, { cwd: "P:\\REPO\\tmp\\worktree\\ISSUE-1835\\", startAt: Now + Minute, endAt: Now + 2 * Minute }),
    ],
  };
}

/** The lines below the rule up to the bottom line: the panel (also of colored lines). */
function panelOf(lines: string[]): string[] {
  const rule = lines.findIndex((line, i) => i > 1 && line.replace(/\[[0-9;]*m/g, "").startsWith("──"));
  return lines.slice(rule + 1, -1);
}

function checkRows(): void {
  const chats = sampleChats();
  const screen = { columns: 120, rows: 17, now: Now };
  const lines = renderScreen(linkedReply(), initialViewState(), screen, false, chats);
  assert.strictEqual(lines.length, 17);
  lines.forEach((line, i) => assert.ok(displayWidth(line) <= 120, `line ${i} fits 120 columns: ${line}`));
  const rule = lines.find((line, i) => i > 1 && line.startsWith("──")) ?? "";
  assert.match(rule, /^── chats: 4 wait, 2 working, 1 in background ─+$/, "the rule counts the chats by state");
  const panel = panelOf(lines);
  // Tool questions first, then replies that ask, then the rest; the most recent first within each, so a report newer
  // than an asking reply still comes after it
  assert.deepStrictEqual(
    panel.map((line) => [line.slice(0, 3).trim(), ...line.slice(3).trim().split(/ +/).slice(0, 4)]),
    [
      ["?", "claude", "issue-1835", "40s", "permission"],
      ["?", "claude", "issue-1851-panel", "6m", "question"],
      ["…?", "agy", "main", "41m", "reply"],
      ["", "claude", "issue-1835", "2m", "reply"],
    ],
    panel.join("\n"),
  );
  // A tool's question keeps its start; a reply keeps its end, where agents ask, with its line breaks as spaces
  assert.match(panel[1] ?? "", /#900 Where should the chat panel go: below the jobs, or in a column beside …$/);
  assert.match(panel[2] ?? "", /#901 …[^…]*round-robin; which one goes first\?$/);
  assert.match(panel[3] ?? "", /#902 …[^…]*builds and tests through the queue\.$/);
  // A tool's question stands out in the warning color, an asking reply by its mark; a report not at all
  const colored = panelOf(renderScreen(linkedReply(), initialViewState(), screen, true, chats));
  assert.ok((colored[1] ?? "").includes("\x1b[33mWhere should"), `a question in the warning color: ${colored[1]}`);
  assert.ok((colored[0] ?? "").includes("\x1b[33m?"), "the ? mark of a permission too");
  assert.ok((colored[2] ?? "").includes("\x1b[33m…?"), `the …? mark of an asking reply: ${colored[2]}`);
  assert.ok(!(colored[2] ?? "").includes("\x1b[33m…uld"), "but not its text");
  assert.ok(!(colored[3] ?? "").includes("\x1b[33m"), `no warning color on a report: ${colored[3]}`);
  // A narrow screen leaves out the kind
  const narrow = panelOf(renderScreen(linkedReply(), initialViewState(), { ...screen, columns: 60 }, false, chats));
  assert.match(narrow[1] ?? "", /^\? +claude issue-1851-panel +6m #900 Where/);
  assert.match(narrow[2] ?? "", /^…\? agy +main +41m #901 …[^…]*which one goes first\?$/);
  narrow.forEach((line) => assert.ok(displayWidth(line) <= 60, line));
  // An older chat file without asks counts as a report
  const old: ChatView = {
    ...chats,
    chats: [chat({ question: "Merge it?", since: Now }), chat({ asks: true, since: Now - Minute })],
  };
  assert.deepStrictEqual(
    waitingChats(old).map((c) => c.since),
    [Now - Minute, Now],
  );
}

function checkShortAndOverflow(): void {
  const chats = sampleChats();
  // 70x13: one summary line under the rule, in the same order, the job list keeps the rest
  const small = renderScreen(linkedReply(), initialViewState(), { columns: 70, rows: 13, now: Now }, false, chats);
  assert.strictEqual(small.length, 13);
  assert.match(small[11] ?? "", /^ 4 chats wait: \?issue-1835 40s, \?issue-1851-panel 6m, …\?main 41m, issu$/);
  assert.match(small[10] ?? "", /^── chats: 4 wait/);
  assert.match(small[4] ?? "", /^ 902 /, "the three jobs still show");
  // More waiting chats than a tall screen has room for: the job list keeps its minimum, the rest are counted
  const many: ChatView = {
    ...chats,
    chats: Array.from({ length: 30 }, (_, i) =>
      chat({ worktree: `P:/repo/tmp/worktree/w${i}`, since: Now - i * Minute }),
    ),
  };
  const sixJobs: StatusReply = {
    ...linkedReply(),
    waiting: [903, 904, 905, 906].map((id) => job(id, { startAt: Now + Minute, endAt: Now + 2 * Minute })),
  };
  const tall = renderScreen(sixJobs, initialViewState(), { columns: 100, rows: 20, now: Now }, false, many);
  assert.strictEqual(tall.length, 20);
  assert.match(tall[5] ?? "", /^ 904 /, "four job rows are kept");
  assert.match(tall[6] ?? "", /^── \+2 more ── chats: 30 wait/);
  assert.match(tall[7] ?? "", /^ {3}claude w0 /, "the most recent wait first");
  assert.match(tall[18] ?? "", /^ \+19 more chats wait/);
}

function checkJobLinks(): void {
  const chats = sampleChats();
  const jobs = screenJobs(linkedReply());
  const ids = (worktree: string, view: ChatView = chats): number[] => chatJobs(chat({ worktree }), view, jobs);
  assert.deepStrictEqual(ids(PanelWorktree), [900], "git's P:/ spelling matches the scheduler's lower-case p:\\ key");
  assert.deepStrictEqual(ids(LaneWorktree), [902], "a cwd in another case with a trailing separator");
  assert.deepStrictEqual(ids(MainCheckout), [901], "the main checkout is not every worktree under it");
  assert.deepStrictEqual(ids("P:/repo/tmp/worktree/other"), []);
  assert.deepStrictEqual(ids(PanelWorktree, { ...chats, ignoreCase: false }), [], "case counts off Windows");
  assert.deepStrictEqual(chatJobs(chat({ worktree: null, cwd: null }), chats, jobs), [], "no directory, no jobs");
  assert.strictEqual(pathKey("P:\\Repo\\x\\", true), "p:/repo/x");
}

function checkHideAndAbsence(): void {
  const chats = sampleChats();
  const screen = { columns: 120, rows: 17, now: Now };
  const without = renderScreen(linkedReply(), initialViewState(), screen, false);
  // a hides the panel and shows it again; the rule still says that chats wait
  let state: ViewState = initialViewState();
  state = reduceKey(state, "a", linkedReply(), Now).state;
  assert.strictEqual(state.chatsHidden, true);
  const hidden = renderScreen(linkedReply(), state, screen, false, chats);
  assert.ok(!hidden.some((line) => /(claude|agy) +(issue-|main)/.test(line)), "no chat rows while hidden");
  assert.match(hidden[15] ?? "", /^── 4 chats wait, a shows them ─+$/);
  assert.deepStrictEqual(
    hidden.filter((_, i) => i !== 15),
    without.filter((_, i) => i !== 15),
  );
  state = reduceKey(state, "a", linkedReply(), Now).state;
  assert.strictEqual(state.chatsHidden, false);
  // a inside a prompt is not the hide key
  const prompting: ViewState = { ...initialViewState(), prompt: { kind: "cancel", id: 900 } };
  assert.strictEqual(reduceKey(prompting, "a", linkedReply(), Now).state.chatsHidden, false);
  // No waiting chat (only working and background ones), or no chat file at all: no panel and the screen as before
  const busy: ChatView = { ...chats, chats: chats.chats.filter((c) => c.state !== "waiting") };
  assert.deepStrictEqual(waitingChats(busy), []);
  assert.deepStrictEqual(renderScreen(linkedReply(), initialViewState(), screen, false, busy), without);
  assert.deepStrictEqual(renderScreen(linkedReply(), initialViewState(), screen, false, null), without);
}

export function checkChats(): void {
  checkRows();
  checkShortAndOverflow();
  checkJobLinks();
  checkHideAndAbsence();
}
