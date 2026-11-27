<!--
Write for a reviewer who has not read the issue, the design notes or the code. Delete every comment like this one.
Each section answers one question. A list of the types and files you touched answers none of them: the Files
changed tab already has that list. Name at most about three code symbols outside Verification, each with what it does.
Many readers are not native English speakers: open each section with one short sentence that carries its point,
keep paragraphs to three sentences or fewer, and lead list items with a few bold words.
Output longer than about ten lines (snapshots, tables, logs) goes inside <details> or into a file, never inline.
Keep every heading; write "Not needed: <one-sentence reason>" under one that does not apply.
-->

## Why

<!--
What was wrong or missing, and who hit it: a wrong result, a missing feature, a slow build, a step that a
later change needs. Say in a few words where this PR stands in a larger plan, and end with one sentence on
what this PR does about it. Link the issue: "Fixes #<n>" only when this PR really fixes it, "Relates to #<n>"
otherwise.
-->

## What changes

<!--
What a user, the compiler output or the next change sees differently after this PR, and what stays the same.
Be concrete: "generated code is identical for all 1086 files", "a ref local of a struct now gives 5 on CUDA
(was 0)", "the new option -p:X=true turns on the new path; without it nothing changes".
For groundwork that changes no output, say what the next change can rely on that it could not before.
-->

## Breaking changes

<!--
Write "None." or, for each break, what stops working (public API, diagnostic, generated code, build property,
file format), who is affected, and what they must change.
-->

## How it works

<!--
When there is a data path, open with its skeleton as inline code: stages joined by arrows in plain words, such
as `parse -> check each rule -> report`. Use a code block of two to four lines when it does not fit one line.
Then the decisions a reviewer would question, one per bold-led item: why this way, and which other way you
rejected. For a bug fix: the cause, in one or two sentences.
-->

## Human check

<!--
What a person should look at, because the agent cannot judge it alone: a contract or behavior decision, a
trade-off, a cost, an open question. For each item, say what was decided and what the reviewer should decide
or confirm; work the author still has to do is not an item. Quote 1-2 short test excerpts (or the expected
table) that check the main behavior.
-->

## Verification

<!--
What you ran and what it showed, as results, not as a checklist: the test command and its pass / fail
counts, generated code parity, benchmarks with numbers. Say what you did not run and why.
-->
