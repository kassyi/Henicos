---
name: "Roadmap and Task Management"
description: "Rules and procedures for updating the project roadmaps, tracking progress, and reporting new tasks."
---

# TASK TRACKING & ROADMAP OBLIGATIONS

When working on G# (GSharp) or GpuBigInt (GBI) implementation tasks, you **MUST** strictly adhere to the project's roadmap and progress tracking system.

## Mandatory Progress Tracking

The roadmap is now managed through an interactive UI application located at `tools/roadmap-ui`, with the single source of truth stored in `docs/data/roadmap_tasks.json`.

**IMPORTANT**: DO NOT attempt to edit roadmap Markdown files directly.

- **How to Update:** When you complete a task or want to change its status, you must prompt the human user to invoke the `/roadmap` slash command (which launches the UI), or you can directly modify `docs/data/roadmap_tasks.json` if explicitly instructed by the user.
- **Reporting New Tasks:** If you discover technical debt or new tasks, do not create them in Markdown. Ask the user to add them via the UI or append them directly to the JSON file.
