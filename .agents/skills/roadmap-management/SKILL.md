---
name: "Roadmap and Task Management"
description: "Rules and procedures for updating the project roadmaps, tracking progress, and reporting new tasks."
---

# TASK TRACKING & ROADMAP OBLIGATIONS

When working on G# (GSharp) or GpuBigInt (GBI) implementation tasks, you **MUST** strictly adhere to the project's roadmap and progress tracking system.

## Mandatory Progress Tracking

All development tasks are tracked in the Markdown files located at `docs/roadmaps/` (e.g., `gbi_roadmap.md`, `gsharp_transpiler_issues.md`, `csharp_syntax_support.md`).

- **Update Checkboxes:** Whenever you successfully implement, test, and verify a feature or fix a bug related to a task, you **MUST** update the corresponding checkbox from `- [ ]` to `- [x]` in the roadmap file.
- **Update Status Details:** You **MUST** also update the status, impact, and notes columns in the table with your current findings, next actions, or reasoning. **All table updates and notes MUST be written in Japanese**, as the roadmap is maintained in Japanese.

## Reporting New Tasks

If you discover a new technical debt, missing syntax support, or an optimization opportunity, **DO NOT ignore it**. You must append it as a new task with a new ID to the appropriate roadmap file, and ideally recalculate priorities using `node scripts/calculate_priorities.js --update-docs`.
