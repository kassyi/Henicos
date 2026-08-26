# Henicos (GSharp) Agent Context

This file gives agents enough repository context to work safely and follow project-specific conventions.

<!-- BEGIN GENERATED PORTFOLIO CONTEXT -->

## Generated Repository Context

### Repository Metadata

- Primary language: C#
- Topics: csharp, source-generator, cuda, ptx, transpiler, gpu

### Working Summary

Henicos (GSharp) is a framework that allows executing C# code directly on NVIDIA GPUs. It transpiles C# code into CUDA C++ / PTX using Roslyn Source Generators and provides a Runtime (`JitEngine`, `BackendCompiler`) to compile and launch kernels natively.

<!-- END GENERATED PORTFOLIO CONTEXT -->

## What is G# (GSharp)?

"G#" (GSharp) is the internal name for this custom framework . **It is not an officially existing programming language.** Rather, it is a C#-to-CUDA transpilation engine and runtime built entirely within this repository. It enables C# developers to write GPU kernels in C# syntax, which are then intercepted by Roslyn Source Generators and translated into `.cu` (CUDA C++) code. When working on this project, treat "G#" as the domain-specific transpiler defined by this repository, not as a general-purpose language.

## 1. CRITICAL GUARDRAILS & PROHIBITED ACTIONS

These rules are absolute. Any violation will directly induce runtime failures, Git history pollution, or development workflow disruption.

### 1.1. Absolute Ban on Unauthorized Git Commits / Pushes

- **DO NOT execute `git commit` or `git push` automatically under any circumstances.** AI agents are prone to executing commits autonomously to "complete" a task, but this is strictly forbidden.
- When you finish modifying code, **STOP**. Do not commit. Instead, notify the user that modifications are complete and prompt them to invoke one of the commit workflows:
    - **For prototyping/WIP:** Suggest `/commit.ja.prompt` (to propose commits with Japanese messages).
    - **For publishing/PR creation:** Suggest `/commit.en.prompt` (to propose commits with English conventional messages).
- Only execute commits when running the explicitly invoked `/commit.*.prompt` workflow AND after the user replies with "yes" to your proposed commit plan.

### 1.2. Source Generator Constraints

- **NEVER use Reflection under any circumstances for transpilation.** All C# code transpilation to CUDA must be done via Roslyn Syntax Nodes (`CSharpSyntaxWalker`) and Semantic Models.

### 1.3. No Empty Catch Blocks (Exception Swallowing)

- **NEVER suppress or swallow exceptions** with an empty `catch { }` block unless accompanied by a clear English comment explaining _why_ it is safe to ignore (e.g., `// Ignore cleanup errors on best-effort basis`).

---

## 2. C# DEVELOPMENT & CODE STYLE CHARTER

For all C# coding style conventions (Naming, Syntax, Zero-Allocation preferences, etc.), you **MUST** strictly follow the dedicated rules file. Do not duplicate those rules here.

- **See:** [`.agents/rules/coding-style.md`](.agents/rules/coding-style.md)

### 2.1. GPU Kernel Naming Convention (.cu.cs)

Any C# file that contains G# GPU kernel implementations (e.g., methods annotated with `[GpuKernel]` or `[GpuDeviceFunction]`) **MUST** use the `.cu.cs` extension (e.g., `GpuBigIntMulKernels.cu.cs`). This convention signals to the system and developers that the file contains transpilation targets for CUDA C++.

- **Separation in Test Projects (`tests/`):**
  - Test files containing test runner/assertion logic (e.g., `FooTests.cs`) **MUST NOT** directly contain `[GpuKernel]` implementations.
  - Test helper kernels **MUST** be separated into dedicated `.cu.cs` files matching the corresponding test file name (e.g., `FooTests.cs` -> `FooTestKernels.cu.cs` or `FooKernels.cu.cs`).
  - This ensures clean separation between test execution code and transpiler target kernels, avoiding rule violations and keeping wrapper generation predictable.

---

## 3. Source Generation & Transpilation Best Practices (GSharp)

This project (GSharp) operates by reading C# code via Roslyn Source Generators to generate C# boilerplate/stubs, and ultimately transpiling the logic into CUDA C++ (`.cu`) source code. Both generation steps heavily rely on building code strings using the custom `SourceWriter`.

### Core Principle: Use Zero-Allocation Scopes for Brackets and Parentheses

When generating C# code or ultimately generating `.cu` C++ code (e.g., in `RoslynToCudaVisitor.cs`), **NEVER manually manage braces (`{}`), parentheses (`()`), or brackets (`[]`) with raw string appends.**

Manual management is a breeding ground for bugs (e.g., missing closing parentheses).

- ❌ **DO NOT** write:

```csharp
_writer.Append("if (");
Visit(node.Condition);
_writer.AppendLine(")");
```

- ✨ **DO** use the `using` pattern with `SourceWriter` inline scopes and helpers:

```csharp
_writer.AppendKeyword("if");
using (_writer.Parentheses())
{
    Visit(node.Condition);
}
_writer.AppendLine();
```

### Supported Harnesses (`SourceWriter` extensions):

- `using (_writer.Scope())` -> outputs `{` and `}` with line breaks and block structure.
- `using (_writer.Parentheses())` -> outputs `(` and `)` inline.
- `using (_writer.Brackets())` -> outputs `[` and `]` inline.

This prevents mismatched scopes and eliminates the risk of missing closing characters.

### No Spacing Rule (Dense Formatting)

When generating C# code or ultimately `.cu` C++ code, **do not insert spaces around operators or symbols** (e.g., `=`, `,`, `+`). To prevent spacing errors, let the AI output dense code without manual visual formatting. Rely exclusively on `SourceWriter` helpers (`AppendComma()`, `AppendOperator()`, `AppendKeyword()`, etc.) instead of hardcoding spaces in string literals.

### 3.3. Mandatory Snapshot Testing for New Features

Whenever you implement support for a new C# syntax, operator, or Transpiler feature in G#, you **MUST** create a corresponding snapshot test to verify the generated CUDA C++ output.

1. Create a new `.cs` test case file inside `tests/Kassyi.GSharp.Tests/TestCases/`.
2. Add the corresponding `[InlineData("YourTestCase.cs")]` to the `GeneratorSnapshots` method in `GeneratorSnapshotTests.cs`.
3. Run `dotnet test` to generate and verify the `.cu` source code snapshot.

### 3.4. QA & Test Coverage Guidelines

When writing or modifying unit tests (xUnit), you **MUST** adhere to the multi-perspective QA criteria (Edge Case, Stress/Concurrency, Alignment/Padding, Sad Path/Exceptions).

- **See:** [`.agents/skills/qa-testing-guidelines/SKILL.md`](.agents/skills/qa-testing-guidelines/SKILL.md)

---

## 4. TASK TRACKING & ROADMAP OBLIGATIONS

When working on G# (GSharp) or GpuBigInt (GBI) implementation tasks, you **MUST** strictly adhere to the project's roadmap and progress tracking system.

### 4.1. Mandatory Progress Tracking

All development tasks are tracked in the Markdown files located at `docs/roadmaps/` (e.g., `gbi_roadmap.md`, `gsharp_transpiler_issues.md`, `csharp_syntax_support.md`).

- **Update Checkboxes:** Whenever you successfully implement, test, and verify a feature or fix a bug related to a task, you **MUST** update the corresponding checkbox from `- [ ]` to `- [x]` in the roadmap file.
- **Update Status Details:** You **MUST** also update the status, impact, and notes columns in the table with your current findings, next actions, or reasoning. **All table updates and notes MUST be written in Japanese**, as the roadmap is maintained in Japanese.

### 4.2. Reporting New Tasks

If you discover a new technical debt, missing syntax support, or an optimization opportunity, **DO NOT ignore it**. You must append it as a new task with a new ID to the appropriate roadmap file, and ideally recalculate priorities using `node scripts/calculate_priorities.js --update-docs`.

---

## 5. OUTPUT VALIDATION & RESPONSE STYLE RULE

### 5.1. Static Code Analysis Validation

- Always validate that unused members are properly handled. In library components (`Kassyi.GSharp.Core`), use `#pragma warning disable IDE0051` or `// ReSharper disable UnusedMember.Global` when members are strictly intended as public API.

### 5.2. Tone & Communication Rules

- **Language:** All Q&A, explanations, and thinking must be in Japanese.
- **Professional Demeanor:** Keep your tone quiet, highly professional, concise, and dry—like a seasoned senior backend engineer. Focus entirely on logic, architecture, and safety.
