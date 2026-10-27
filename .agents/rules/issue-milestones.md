# Issue Priority (GitHub Milestones)

Issue priority is managed with GitHub milestones. Labels (`bug`, `feature`, ...) describe the kind of issue; the milestone describes the order in which issues are handled.
An issue can have only one milestone, so priorities cannot contradict each other.

## 1. Current policy (2026-10-25)

Fixing bugs and cleaning up code take priority over new features. New development that is needed to fix a bug (for example, adding a diagnostic) goes into the same milestone as that bug.
Clean the code and flush out the bugs first, then grow the semantic libraries (such as `GpuGraph`).

## 2. Milestones

The numbers follow severity. `Future` is an open-ended place for new work, so it has no number and is never closed.

| Milestone | What goes in |
| :- | :- |
| `M1 Wrong Results` | Values become wrong without a diagnostic, a crash, an infinite loop, or a failure to launch |
| `M2 Build Failures & Missing Diagnostics` | The generated C++ does not compile, an internal error, or something that should be diagnosed is not |
| `M3 Unconfirmed Suspicions` | Suspicions not yet reproduced. Move to M1 or M2 once reproduced; close if it does not reproduce |
| `M4 Bug-Finding Infrastructure` | Holes that let bugs through, such as tests or analyzers that do not run and snapshots that are not compared; the tools for finding bugs |
| `M5 Cleanup` | Removal, consolidation, replacing deprecated APIs |
| `Future` | Semantic libraries, new features, performance |

## 3. How to assign

Apply the following from the top; the first match decides the milestone.

1. Not yet reproduced -> `M3`
2. Gives a wrong result or a run-time failure with no diagnostic -> `M1`
3. The build fails, or a diagnostic should appear but does not -> `M2`
4. A test or check that does not work, or a tool for finding bugs -> `M4`
5. Cleanup only, with no change in behavior -> `M5`
6. Anything else (new features, performance) -> `Future`

Every open issue must have exactly one milestone. Issues without one can be listed with the following command.

```sh
gh issue list --state open --limit 500 --json number,title,milestone --jq '.[] | select(.milestone == null) | "\(.number)\t\(.title)"'
```

## 4. Order of work

The priority order is `M1` -> `M2` -> `M5`. `M1` is the highest and `M5` the lowest. `Future` is an open-ended place for new development; do not start it while issues remain in `M1` to `M5`.
`M3` and `M4` are small per issue, and settling them fixes what `M1` and `M2` contain. They may run in parallel with `M1`, but are never put ahead of it.
The order within a milestone is decided by sorting on the GitHub milestone page.

## 5. What agents do

- When filing an issue (`/issue`), propose a milestone together with the labels and attach it with `gh issue create --milestone "<name>"`.
- Changing a milestone, and creating or closing a milestone, are public actions, so get the user's approval first.
- When an `M3` issue is investigated and reproduced, propose where to move it (`M1` or `M2`).
- Ask the user before closing a milestone whose issues are all done. When creating a new one, use the next free number (`M6 ...`).
