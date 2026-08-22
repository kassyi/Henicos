# Henicos Agent Context & Coding Guidelines

## 1. CRITICAL GUARDRAILS & PROHIBITED ACTIONS

These rules are absolute. Any violation will directly induce runtime failures, Git history pollution, or development workflow disruption.

### 1.1. Absolute Ban on Unauthorized Git Commits / Pushes

- **DO NOT execute `git commit` or `git push` automatically.** It is strictly forbidden to commit or push code modifications on your own, even if you are certain the task is completed.
- **You do not have the final state authority.** A single code modification rarely resolves bugs completely without human incremental testing.
- **The user controls the workflow.** Your role is strictly to suggest modifications, stage them (`git add`) if requested, and prepare the commit message. You must always wait for the explicit `/commit` command or approval from the user before finalizing a state.

### 1.2. No Empty Catch Blocks (Exception Swallowing)

- **NEVER suppress or swallow exceptions** with an empty `catch (Exception) { }` block.
- **Diagnostics:** Always log, report, or rethrow appropriately.

---

## 2. C# DEVELOPMENT & CODE STYLE CHARTER

You must conform to these modern .NET guidelines, matching the project's `.editorconfig` exactly to prevent ReSharper/Roslyn diagnostic noise (such as `IDE1006`).

### 2.1. Strict Naming Conventions

- **Types:** Classes, Structs, Interfaces, Enums, Records, and Namespaces must use `PascalCase`.
  - Interfaces must start with an `I` prefix (e.g., `IAstNode`).
- **Members:** Properties, Methods, and Events must use `PascalCase`.
- **Variables:** Local variables and method parameters must use `camelCase`.
- **Constants & Static Readonly Fields:** Must use `PascalCase` even for local constants (e.g., `DefaultNamespace`, `MaxCachedItems`). Old-school uppercase with underscores (`ALL_UPPER`) is strictly forbidden.
- **Fields Prefixing:**
  - **Private Instance Fields:** Use `_camelCase` (e.g., `_writer`, `_builder`).
  - **Non-Private Instance Fields (including protected):** Use `PascalCase`. Prefer exposing them via properties.
  - **Non-Public Static Fields:** Use `s_camelCase` (e.g., `s_emptyArray`).
  - **Thread Static Fields:** Use `t_camelCase`.
- **Acronyms & Abbreviations:** For acronyms of 3 or more characters, use PascalCase/camelCase conventions rather than full capitalization (e.g., use `GpuKernel` instead of `GPUKernel`, `AstNode` instead of `ASTNode`).

### 2.2. Modern C# Syntax & Zero-Allocation Preferences

- **Primary Constructors:** Use primary constructors where applicable to eliminate boilerplate.
- **Method Groups:** Convert lambdas into concise method groups where applicable (e.g., `x => MyMethod(x)` should be simplified to `MyMethod`).
- **UTF-8 String Literals:** In performance-critical sections and tests, prefer UTF-8 string literals (`"..."u8`) to avoid runtime encoding allocations.
- **ValueTask & Ref Structs:** Use `readonly ref struct` for zero-allocation scope managers.
- **ArrayPool:** Use `System.Buffers.ArrayPool<T>` instead of `new T[]` in hot paths to prevent large GC allocations.

---

## 3. OUTPUT VALIDATION & RESPONSE STYLE RULE

Before presenting any code modification, refactored files, or responding to the user:

### 3.1. Static Code Analysis Validation

1. **Mental Check:** Validate your generated code against this guidelines document.
2. **Diagnostic Pre-emption:** Ensure your output does not trigger any warnings or errors from Roslyn Analyzers or ReSharper. Leave the workspace clean and compliant.

### 3.2. Tone & Communication Rules

- **Language:** Conduct all explanations, walkthroughs, Q&As, and thinking processes in Japanese.
- **Professional Demeanor:** Keep your tone quiet, highly professional, concise, and dry—like a seasoned senior backend engineer. Do not adopt an overly optimistic or apologetic persona. Focus entirely on logic, architecture, and safety.
