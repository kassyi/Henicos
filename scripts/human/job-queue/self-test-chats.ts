// The self-test of the chat panel of the live view (view-chats.ts): the rows of the chats that wait for the person,
// their order and styles, the one-line summary of a short screen, the job ids of a chat's worktree, the hide key, and
// no panel when no chat waits. Drawn from the fixed chats of view-sample.ts.

import * as assert from "node:assert";
import type { StatusReply } from "./protocol.ts";
import {
  chatCounts,
  chatJobs,
  chatLabel,
  chatLegend,
  judgedByHaiku,
  pathKey,
  waitingChats,
  type ChatView,
} from "./view-chats.ts";
import { displayWidth, helpLines, initialViewState, renderScreen, screenJobs, type ViewState } from "./view.ts";
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
  assert.match(
    rule,
    /^── chats: 4 wait, 1 idle, 2 working, 1 in background ─+$/,
    "the rule counts the chats by state, a reply that only reports as idle",
  );
  const panel = panelOf(lines);
  // Tool questions first, then replies with an open question, the most recent first within each; the report is not
  // listed. A row names its chat: the title (in the main checkout without the worktree), else the start of the first
  // prompt after the worktree, else the worktree
  const label = (line: string): string[] => [
    line.slice(0, 3).trim(),
    line.slice(3, 10).trim(),
    line.slice(10, 47).trim(),
  ];
  assert.deepStrictEqual(
    panel.map(label),
    [
      ["?", "claude", "issue-1835"],
      ["?", "claude", "issue-1851-panel"],
      ["…?", "claude", "issue-1890: Fix the width of the cha"],
      ["…?", "agy", "Queue PR order"],
    ],
    panel.join("\n"),
  );
  assert.deepStrictEqual(
    panel.map((line) => line.slice(47).trim().split(/ +/).slice(0, 2)),
    [
      ["40s", "permission"],
      ["6m", "question"],
      ["9m", "reply"],
      ["41m", "reply"],
    ],
  );
  // A tool's question keeps its start; a reply judged by the question mark keeps its end, where agents ask, with its
  // line breaks as spaces; Claude Haiku's newest open question is marked with ≈, the others counted; a job without a
  // session counts for its worktree, but not in the main checkout
  assert.match(panel[1] ?? "", /#900 Where should the chat panel go: below the jobs, or…$/);
  assert.match(panel[2] ?? "", / 9m reply +The rows fit the width now\. Shall I push the branch\? +$/);
  assert.match(panel[3] ?? "", / 41m reply +≈ Which of the two queue PRs merges first\? \(\+1\) +$/);
  // A verdict of an earlier wait is not Haiku's for this one: the list is shown as the heuristic's
  const earlier: ChatView = {
    ...chats,
    chats: chats.chats.map((c) => (c.askVerdict ? { ...c, summaryOf: c.since - 1 } : c)),
  };
  const stale = panelOf(renderScreen(linkedReply(), initialViewState(), screen, false, earlier));
  assert.match(stale[3] ?? "", / 41m reply +Which of the two queue PRs merges first\? \(\+1\) +$/);
  // A tool's question stands out in the warning color, an asking reply by its mark
  const colored = panelOf(renderScreen(linkedReply(), initialViewState(), screen, true, chats));
  assert.ok((colored[1] ?? "").includes("\x1b[33mWhere should"), `a question in the warning color: ${colored[1]}`);
  assert.ok((colored[0] ?? "").includes("\x1b[33m?"), "the ? mark of a permission too");
  assert.ok((colored[3] ?? "").includes("\x1b[33m…?"), `the …? mark of an asking reply: ${colored[3]}`);
  assert.ok(!(colored[3] ?? "").includes("\x1b[33mWhich"), "but not its text");
  // A narrow screen leaves out the kind
  const narrow = panelOf(renderScreen(linkedReply(), initialViewState(), { ...screen, columns: 60 }, false, chats));
  assert.match(narrow[1] ?? "", /^\? +claude issue-1851-panel +6m #900 Where/);
  assert.match(narrow[3] ?? "", /^…\? agy +Queue PR order +41m ≈ Which of the two … \(\+1\)$/);
  narrow.forEach((line) => assert.ok(displayWidth(line) <= 60, line));
  // Which replies are listed: Claude Haiku's verdict over the question mark; an older file by its question mark
  const view = (fields: Parameters<typeof chat>[0][]): ChatView => ({ ...chats, chats: fields.map(chat) });
  const listed = (fields: Parameters<typeof chat>[0][]): number[] => waitingChats(view(fields)).map((c) => c.since);
  const open = [{ text: "Decide the merge order", askedAt: Now }];
  assert.deepStrictEqual(
    listed([
      { since: 1, question: "Merge it?", asks: true, askVerdict: "haiku", summaryOf: 1, openQuestions: [] },
      {
        since: 2,
        question: "Please decide the order.",
        asks: false,
        askVerdict: "haiku",
        summaryOf: 2,
        openQuestions: open,
      },
      { since: 3, question: "Merge it?" },
      { since: 4, asks: true, question: "Merge it?" },
      { since: 5, kind: "input", question: "Claude is waiting for your input" },
      { since: 6, state: "working", kind: null, openQuestions: open },
    ]),
    [4, 2],
    "Haiku's asks=false hides a reply with a ?, asks=true shows one without; a report or another wait is not listed",
  );
  assert.strictEqual(
    chatCounts(
      view([
        { since: 1, question: "Done." },
        { since: 2, kind: "question" },
      ]),
    ),
    "1 wait, 1 idle",
  );
}

function checkShortAndOverflow(): void {
  const chats = sampleChats();
  // 70x13: one summary line under the rule, in the same order, the job list keeps the rest
  const small = renderScreen(linkedReply(), initialViewState(), { columns: 70, rows: 13, now: Now }, false, chats);
  assert.strictEqual(small.length, 13);
  assert.match(small[11] ?? "", /^ 4 chats wait: \?issue-1835 40s, \?issue-1851-panel 6m, …\?issue-1890: Fi$/);
  assert.match(small[10] ?? "", /^── chats: 4 wait/);
  assert.match(small[4] ?? "", /^ 902 /, "the three jobs still show");
  // More waiting chats than a tall screen has room for: the job list keeps its minimum, the rest are counted
  const many: ChatView = {
    ...chats,
    chats: Array.from({ length: 30 }, (_, i) =>
      chat({ worktree: `P:/repo/tmp/worktree/w${i}`, since: Now - i * Minute, question: "Go on?", asks: true }),
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
  assert.match(tall[7] ?? "", /^…\? claude w0 /, "the most recent wait first");
  assert.match(tall[18] ?? "", /^ \+19 more chats wait/);
}

function checkJobLinks(): void {
  const chats = sampleChats();
  const jobs = screenJobs(linkedReply());
  const ids = (worktree: string, view: ChatView = chats): number[] => chatJobs(chat({ worktree }), view, jobs);
  assert.deepStrictEqual(ids(PanelWorktree), [900], "git's P:/ spelling matches the scheduler's lower-case p:\\ key");
  assert.deepStrictEqual(ids(LaneWorktree), [902], "a cwd in another case with a trailing separator");
  assert.deepStrictEqual(ids(MainCheckout), [], "no job by its worktree in the main checkout: every chat is there");
  assert.deepStrictEqual(ids("P:/repo/tmp/worktree/other"), []);
  assert.deepStrictEqual(ids(PanelWorktree, { ...chats, ignoreCase: false }), [], "case counts off Windows");
  // A job that names the chat that queued it goes to that chat only, in the main checkout too, and never to another
  // chat of its worktree
  const sessionJobs = screenJobs({
    ...linkedReply(),
    waiting: [
      job(910, { worktree: "p:\\repo", session: "chat-a", startAt: Now + Minute, endAt: Now + 2 * Minute }),
      job(911, { worktree: "p:\\repo\\tmp\\worktree\\issue-1835", session: "chat-b", startAt: Now, endAt: Now + 1 }),
    ],
  });
  const of = (worktree: string, session: string): number[] => chatJobs(chat({ worktree, session }), chats, sessionJobs);
  assert.deepStrictEqual(of(MainCheckout, "chat-a"), [910], "by its session in the main checkout");
  assert.deepStrictEqual(of(MainCheckout, "chat-c"), [], "another chat of the main checkout gets none");
  assert.deepStrictEqual(of(LaneWorktree, "chat-b"), [911]);
  assert.deepStrictEqual(of(LaneWorktree, "chat-c"), [], "another chat's job of its worktree is not its own");
  assert.deepStrictEqual(of(PanelWorktree, "chat-c"), [900], "a job without a session: by its worktree");
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

/** The label of a row (title, first prompt, worktree), the summary of the current wait, and the help's summary line. */
function checkLabelsAndLegend(): void {
  const view = sampleChats();
  const label = (fields: Parameters<typeof chat>[0]): string => chatLabel(chat(fields), view);
  assert.strictEqual(label({}), "main");
  assert.strictEqual(label({ worktree: LaneWorktree }), "issue-1835");
  assert.strictEqual(label({ firstPrompt: "  Fix\nthe   queue  " }), "Fix the queue");
  assert.strictEqual(label({ firstPrompt: "x".repeat(40) }), "x".repeat(24), "the start of a long first prompt");
  assert.strictEqual(label({ firstPrompt: "Fix", title: "Queue fix" }), "Queue fix", "the title wins");
  assert.strictEqual(label({ title: "Queue fix", agentTitle: "Hook survey" }), "Hook survey", "Antigravity's wins");
  assert.strictEqual(label({ worktree: LaneWorktree, title: "Queue fix" }), "issue-1835: Queue fix");
  assert.strictEqual(judgedByHaiku(chat({ since: 5, askVerdict: "haiku", summaryOf: 5 })), true);
  assert.strictEqual(judgedByHaiku(chat({ since: 6, askVerdict: "haiku", summaryOf: 5 })), false, "an earlier wait's");
  assert.strictEqual(judgedByHaiku(chat({ since: 5, askVerdict: "heuristic", summaryOf: 5 })), false);
  // A wide title keeps the row within the screen
  const titled = chat({
    title: "チャット状態の要約とタイトルの表示",
    since: Now,
    question: "要約を見ますか？",
    asks: true,
  });
  const wide: ChatView = { ...view, chats: [titled] };
  const lines = renderScreen(linkedReply(), initialViewState(), { columns: 80, rows: 17, now: Now }, false, wide);
  assert.ok(
    lines.some((line) => line.includes("チャット状態")),
    "the row is drawn",
  );
  lines.forEach((line) => assert.ok(displayWidth(line) <= 80, `fits 80 columns: ${line}`));
  // The help says who decides that a reply asks (Claude Haiku, or the question mark and how to turn Haiku on), and that
  // a report is idle
  const legend = (summaries: ChatView["summaries"]): string =>
    helpLines(120, { ...view, summaries })
      .map((line) => line.text)
      .join("\n");
  assert.match(
    legend({ on: true }),
    /≈ Claude Haiku decides what asks, writes the\n.* question and title \(Claude Code\); reports: idle/,
  );
  assert.match(
    legend({ on: false, reason: "disabled" }),
    /\? a "\?" decides what asks \(GSHARP_CHAT_STATUS_SUMMARY=0\); reports: idle/,
  );
  assert.doesNotMatch(legend({ on: false, reason: "disabled" }), /install/);
  const missing = legend({ on: false, reason: "missing" });
  assert.match(
    missing,
    /a "\?" decides what asks \(no Claude Code CLI\); reports: idle\n.* install Claude Code or set GSHARP_CHAT_STATUS/,
  );
  assert.doesNotMatch(legend(null), /decides/, "no line when chat-status.js could not tell");
  assert.strictEqual(chatLegend(null).length, 6, "the chat legend without the line of the verdicts");
}

export function checkChats(): void {
  checkRows();
  checkLabelsAndLegend();
  checkShortAndOverflow();
  checkJobLinks();
  checkHideAndAbsence();
}
