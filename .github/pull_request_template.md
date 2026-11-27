<!--
Write for a reviewer who has not read the issue, the design notes or the code.
Each section answers one question in sentences. A list of the types and files you touched answers none of
them: the Files changed tab already has that list.
Name a type only when the reader needs it to find the code, and say what it does when you name it.
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
(was 0)", "the IR route is off unless -p:GSharpIrCpp=true".
For groundwork that changes no output, say what the next change can rely on that it could not before.
-->

## How it works

<!--
The shape of the solution in a few sentences: the path the data takes from input to output, and where each
new piece sits on that path. Then the decisions a reviewer would question: why this way, and which other
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
