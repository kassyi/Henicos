---
name: jules-wave-spec
description: Execution rules for Google Jules when it implements an issue that carries a "Jules Execution Spec" (test first, allowed files only, no comment edits, PR naming, stop conditions). Load it when you write, run, or review a Jules Wave issue or its pull request.
---

# Jules Wave Execution Rules

These rules apply to every issue that contains a section titled **🤖 Jules Execution Spec**. That section names this file and adds the issue-specific steps (allowed files, tests to write, snapshots you may approve). Jules reads this file from the repository, so the issue text does not repeat it.

Implement the issue end to end and open **one** pull request. Nobody answers questions during the run, so everything you need is in the issue and in this file. If something does not match the code, follow the stop conditions below; do not improvise.

An issue-specific rule overrides a rule here only when it names that rule explicitly (for example, "this overrides the 'any existing snapshot changes' stop condition for this one file").

## Mandatory context (read first)

1. `AGENTS.md`
2. `.agents/rules/coding-style.md`
3. `.agents/rules/test-architecture.md` and `.agents/rules/test-execution.md`
4. `.agents/rules/generator-invariants.md`
5. `.agents/skills/qa-testing-guidelines/SKILL.md`

## Common constraints

- Write the tests first. Commit the failing tests before the fix, so the PR has at least two commits. Do not squash them.
  - If your tooling squashes the commits anyway, paste into the PR body the output of the test command run with only the tests applied, before the fix. It must show the new tests failing.
- Edit only the files listed under "Allowed files". Do not refactor, rename, or reformat anything else.
- **Do not delete or reword existing comments.** This includes comments in code that you move or rewrite.
- Do not use reflection, empty `catch` blocks, `System.Linq` in compiler code, or Regex in new code.
- Do not use `!`. A justification comment does not make an avoidable `!` acceptable. For example, use `GetValueOrDefault()` on a `Nullable<T>` that you have already checked, instead of `x!.Value`.
- Tests:
  - Mark tests with `[Category(TestCategory.X)]` and `[Scenario(TestScenario.X)]`.
  - In an existing test class, add `[Category]` once at the class level when it is missing, instead of on each method.
  - Do not use `[Trait("Category", ...)]` magic strings, and do not make test classes `partial`.
  - Do not add `using Kassyi.GSharp.Tests.Infrastructure;`. `tests/Directory.Build.props` already imports it globally.
  - Files under `tests/**/TestCases/` are not compiled by the test project, so a broken one does not fail `just test`. Each one is compiled on its own by the test helpers, so it must compile on its own: declare every type it uses inside the same file, and do not reference types from other test case files. Before you push, check each new or changed test case file by compiling it alone (for example with a temporary test that asserts `RoslynTestUtility.GetTestCaseContext(path)` has no compilation errors), and remove that temporary test afterwards.
- Run the tests with `DiffEngine_Disabled=true dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu`. Approve only the new snapshots that the issue lists; do not approve any other snapshot.
- CUDA tests cannot run in this environment. Do not edit `tests/Compiler/Kassyi.GSharp.Tests.Cuda/**`.
- PR title and every commit message: `<type>(<area>): <summary> (#N)`, where `#N` is **this** issue's number and `<type>` is the one given in the issue-specific steps. Start the PR body with `Closes #N`, using the same issue number (never a pull request number or a Jules task ID), and include the output of the test command.
- Numbers and facts that you write into documentation (sizes in bytes, limits, counts, which types are affected) must match the code. Check each one against the code before you push; do not copy a figure from the issue without checking it.
- Review feedback arrives as PR comments that start with `@jules`. Apply every item marked "Required" before you do anything else in the PR.

## Stop conditions

Stop and open the PR as a **draft** that describes what you found, instead of continuing, when:

- the new test cannot be made to fail on current `main`;
- any existing snapshot changes;
- the fix requires a file that is not under "Allowed files".
