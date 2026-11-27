<!--
Write for a reviewer who has not read the issue, the design notes or the code.
Each section answers one question in sentences. A list of the types and files you touched answers none of
them: the Files changed tab already has that list.
Name a type only when the reader needs it to find the code, and say what it does when you name it.
Many readers are not native English speakers: open each section with one short sentence that carries its point,
keep paragraphs to three sentences or fewer, and lead list items with a few bold words.
-->

## Why

<!--
What was wrong or missing, and who hit it: a wrong result, a missing feature, a slow build, a step that a
later change needs. One short paragraph. Link the issue (Fixes #123 only when this PR really fixes it,
Relates to #456 otherwise) and say in a few words where this PR stands in a larger plan.
-->

## What changes

<!--
What a user, the compiler output or the next change sees differently after this PR, and what stays the same.
Be concrete: "generated code is identical for all 1086 files", "a ref local of a struct now gives 5 on CUDA
(was 0)", "the new option -p:X=true turns on the new path; without it nothing changes".
For groundwork that changes no output, say what the next change can rely on that it could not before.
-->

## How it works

<!--
Open with the skeleton on one line when there is a data path, as stages joined by arrows in plain words:
  front (build, classify, scan; no dialect) -> per route (policy, decide, sequence, verify, support) -> cached result
Then say where each new piece sits on that path. Then the decisions a reviewer would question: why this way, and which other
way you rejected and why. Skip this section for a small fix whose "What changes" already says it all.
For a bug fix: the cause, in one or two sentences.
-->

## Human check

<!--
What a person should look at, because the agent cannot judge it alone: a contract or behavior decision, a
trade-off, a cost, an open question. For each item, say what was decided and what the reviewer should
decide or confirm. Quote 1-2 short test excerpts (or the expected table) that check the main behavior.
Put long output (snapshots, tables, logs) inside <details> or in a file, never inline.
-->

## Verification

<!--
What you ran and what it showed, as results, not as a checklist: the test command and its pass / fail
counts, generated code parity, benchmarks with numbers. Say what you did not run and why.
-->
