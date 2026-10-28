---
name: pr-review-pairing
description: Reviewing open pull requests together with a person. The agent explains each PR plainly and verifies its logic and API; the person skims the code and judges its structure. Covers the review worktree, the per-PR walk-through, bug verification before posting, filing what is out of scope, the PR comment with the `human-reviewed` label, and confirming the author's fixes before calling the PR ready to merge.
---

# PR Review Pairing

Use this skill when a person asks to review the open pull requests with you. GitHub shows only diffs, which give a person no picture of a change. Your job is to give them that picture, check what needs careful reading, and turn the findings into PR comments and issues.

## 1. Roles

| Who | Does |
| :- | :- |
| Agent | Explains each change (what was broken, what the PR does, how the parts fit). Verifies the logic, the math, the bounds and the API shape. Points at structural hot spots before the person looks. Verifies suspected bugs by running them. Drafts comments and issues. |
| Person | Reads the explanation, skims the linked code, and comments on structure: long functions, repeated patterns, hard-to-read code, mixed responsibilities. Approves every comment, issue and label before it is posted. |

Take the person's structural remarks seriously and turn each into a concrete proposal (what to split, what to name, a short code sketch). Do not argue them away; if a remark rests on a misunderstanding (for example a method that cannot be merged because its target differs), explain why in one table and look for the real problem behind it.

## 2. Order and setup

- Review in merge order, one stack at a time (a stack is a chain of PRs, each based on the branch of the one before). Read each PR's own diff against its base, not the cumulative diff; the PR below counts as reviewed.
- Check out the PR head as a detached, read-only worktree and read from there:
  ```sh
  git fetch origin <head-branch>
  git worktree add --detach tmp/worktree/review-<n> origin/<head-branch>
  git -C tmp/worktree/review-<n> diff origin/<base-branch>...HEAD --stat
  ```
  Existing worktrees with a similar name may hold an older branch; compare with `git rev-list --left-right --count <branch>...origin/<head-branch>` before trusting one.
- Do not push merges of `main` into PR branches: keeping a branch up to date is the author's step. The reviewer takes no CI action.

## 3. Walking through one PR

Start with a map, then go file by file.

**Map** (one message):
- What was broken before, in plain words, with the symptom.
- What the PR does, as a short before/after picture or a list of independent fixes.
- A table of the files to read, in reading order, with their role. Say which files can be skipped (docs, mechanical changes) and how much of the line count is tests.

**Each file or fix** (one message each), in this shape:
1. **Summary**: two or three lines.
2. **Links**: the ranges to skim, as workspace-relative links into the review worktree, in the form `[Method :<start>-<end>](tmp/worktree/review-<n>/<path>#L<start>-L<end>)`. Always link; never name a file without a link.
3. **Verdict**: logic and API, with what you checked yourself (bounds recomputed, edge cases walked through, aliasing, races, failure paths). Say "no finding" plainly when there is none.
4. **Structural hot spots**: the long methods (with line counts), big `switch` statements, duplicated blocks (both locations), nested types that belong in their own file, readability problems. These are for the person to judge, so point at them before they ask. Line counts per method:
   ```sh
   awk '/^    (public|internal|private|protected)[^=;]*\(/{n=$0;s=NR} /^    }$/{if(s){printf "%4d %d-%d %s\n",NR-s+1,s,NR,substr(n,1,100);s=0}}' <file> | sort -nr | head
   ```
   Switches with six or more cases (statements and expressions), with their line ranges:
   ```sh
   awk 'match($0,/^ *(switch|.*\) switch| switch)/){ if($0 ~ /switch *\(|\) switch|[a-z] switch$/){ ind=match($0,/[^ ]/); s=FNR; c=0; open=1; next} } open && match($0,/[^ ]/)==ind && /^ *}/ { if(c>=6) printf "%s:%d-%d cases=%d lines=%d\n",FILENAME,s,FNR,c,FNR-s+1; open=0 } open && /^ *(case |default|[A-Za-z_.<>]+ .*=>|_ =>)/{c++}' <files>
   ```
   In the compiler, a big switch whose cases do the work inline is a Facade / Strategy candidate: propose a dispatcher with one line per case (or a switch expression that maps a kind to a descriptor), each case body in its own method or strategy, and the file split by responsibility. Raise it also when the switch predates the PR but sits in a method the PR changes.
5. End with the next step ("next: X, shall I continue?").

**Explaining**: conclusion first, every domain term and algorithm explained the first time it appears. When the person says they do not follow, back up and explain the math or background from the start with a small worked example before going on. Keep chat in Japanese; everything posted to GitHub is English.

## 4. In scope or not

For every finding, decide where it belongs:

| Finding | Goes to |
| :- | :- |
| Introduced or touched by this PR | The PR comment |
| Pre-existing, found while reading | An issue (search first: `gh issue list --state open --search "<words>"`) |
| Stated out of scope in the PR body but tracked nowhere | An issue, and one line in the PR comment naming it |
| Cleanup across files that a pending PR is rewriting | An issue that says to start after that PR merges |

Assign the milestone with [issue-milestones.md](../../rules/issue-milestones.md): reproduced wrong result -> M1, build failure or missing diagnostic -> M2, not reproduced -> M3, cleanup -> M5. Labels: `bug`, `refactor`, `performance` and so on.

## 5. Verify a bug before posting it

Never post a suspected bug as fact. When reading suggests a wrong result:

1. Delegate the check to a subagent with a self-contained brief: the worktree, the suspected code (file and line), the contract it breaks, concrete inputs with the expected output worked out by hand, and the rules below.
2. The subagent adds one temporary test class in the review worktree, runs only that class (GPU runs through `node scripts/agent/gpu-lock.js -- "<command>"`, `DiffEngine_Disabled=true`, output to a file), records expected vs actual for each case, deletes the file and leaves `git status` clean. It stops only processes it started, and does not commit, push or post.
3. Put the observed output (a table of input, expected, actual) into the comment or issue. If something could only be read, not run, say "found by reading; not reproduced".

## 6. Comments, issues and labels

- Draft in the scratchpad (`pr<n>-review-comment.md`, `issue-<slug>.md`), in English, then show a short Japanese summary and the full path in chat and wait for approval. Show the draft before every post, also when the person already agreed to the content.
- PR comment layout:
  - First line: which items block the merge and which do not, and the question "fix it here or file it?".
  - "Checked and fine": what you verified, so the author knows what was covered.
  - Numbered items: a bold one-line title, the location (`File.cs` lines), the failure scenario or the structural problem, and a concrete proposal (a short code sketch when it helps).
- Issues follow `.github/ISSUE_TEMPLATE/` (bug: component, description, reproduction, expected vs actual, environment).
- No emoji, no "Generated with" or co-author footer.
- Commands that post or change GitHub state end with `# intent: <purpose in Japanese>` ([command-intent.md](../../rules/command-intent.md)).
- After posting the review comment, add the label: `gh pr edit <n> --add-label human-reviewed`. It tells the author that the review is in.
## 7. Confirming the fixes

After the author answers the review, check the fixes before calling the PR ready to merge:

1. Read the author's reply and the commits pushed after the review comment (`gh pr view <n> --comments`, `git log <reviewed head>..origin/<head-branch>`).
2. For each item, check the decision: the fix is in the diff and does what the item asked (a fixed bug has a test that failed before), or the item is filed as an issue with its milestone.
3. Report to the person per item (fixed / filed / missing). When every item is fixed or filed, the PR is labeled `human-reviewed` and its Ubuntu and Windows runs are green, tell the person it is ready to merge.
4. Otherwise reply on the PR with what is missing.

Do not push to start a run, re-run jobs or change labels unless asked ([test-execution.md](../../rules/test-execution.md), CI section).
- Decisions for the person (milestone, fix here or later, performance trade-offs) state the problem, each option's cost and your recommendation ([AGENTS.md](../../../AGENTS.md) tone rules).

## 8. What not to do

- No commits or pushes to PR branches; the review worktree stays read-only (the bug-check test file is temporary and removed).
- No issue while the approach is still being discussed; settle it in chat first.
- No "ready to merge" before the fixes are confirmed (section 7); no CI re-runs; no close-and-reopen to restart CI.
- Do not spawn subagents for reading; read inline. Use a subagent for running checks (bug verification) or when the person asks.

## 9. Wrap-up per stack

After the last PR of a stack, give a status table (PR, state, what it waits for) and list the comments and issues created, then name the next stack in the merge order.
