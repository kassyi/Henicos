# Release Checklist

Checks that run once before a release instead of on every build. Each item names what to check, how, and the issue that
holds the full procedure. Add an item when a review finds a kind of problem that a permanent check (an analyzer, a test)
cannot catch or would not pay for.

| Check | How | Issue |
| :- | :- | :- |
| Names of members that return `bool` say what `true` means, in the affirmative | List the methods with ast-grep and jq, then read the candidates | #1129 |
| The specification (`docs/spec`) matches the implementation: APIs, diagnostics, samples | Run the `.agents/workflows/sync-spec.ja.prompt.md` workflow (the C# implementation is the source of truth); pull requests update the pages they touch, this pass catches the rest | - |
