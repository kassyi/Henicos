# Routing Hard Problems (Fable / Deep Think)

Ordinary delegation (the `pro` / `flash` mapping) follows `agent-tool-mapping.md`. This file covers problems harder than that.

## 1. Hardest design and verification (Claude Code only)

Use `model: "fable"` (Claude Fable 5.1) only for problems where Opus has already got stuck, or where a wrong judgment is costly.
It costs 2.5 times as much as Opus and one response can take several minutes, so do not use it routinely.

- Use for: design and review of lowering (suspend / resume, keeping the original-span annotation), cache boundaries of the incremental generator, correctness verification of GPU numeric code, the initial design of cross-cutting redesigns, root-cause investigation of unstable (hard to reproduce) failures
- Do not use for: mechanical work such as routine migrations, snapshot updates and catalog syncs, or implementation-lane work

## 2. Math optimization and complexity reduction (outsourced to the user)

Agents do not solve formula transformation / optimization or complexity (order) reduction themselves; they outsource it to the user (who solves it with Google Deep Think).
This applies in every environment.

- Outsource when: the problem and its answer can be written entirely in plain text (formulas in LaTeX or ASCII) and the problem is too complex to solve on your own. Do not outsource a problem that cannot be written without the codebase's context
- How to hand it over: show the user, in a code block in the chat, a self-contained problem statement that includes the premises, definitions of the symbols, the constraints and the form of the answer wanted. Do not attach files or refer to file paths
- After outsourcing, stop the work that depends on the result and wait for the user's reply
