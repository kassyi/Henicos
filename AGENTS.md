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

- **DO NOT execute `git commit` or `git push` automatically.** You must always wait for the explicit `/commit` command or approval from the user before finalizing a state.

### 1.2. Source Generator Constraints

- **NEVER use Reflection under any circumstances for transpilation.** All C# code transpilation to CUDA must be done via Roslyn Syntax Nodes (`CSharpSyntaxWalker`) and Semantic Models.

### 1.3. No Empty Catch Blocks (Exception Swallowing)

- **NEVER suppress or swallow exceptions** with an empty `catch { }` block unless accompanied by a clear English comment explaining _why_ it is safe to ignore (e.g., `// Ignore cleanup errors on best-effort basis`).

---

## 2. C# DEVELOPMENT & CODE STYLE CHARTER

For all C# coding style conventions (Naming, Syntax, Zero-Allocation preferences, etc.), you **MUST** strictly follow the dedicated rules file. Do not duplicate those rules here.

- **See:** [`.agents/rules/coding-style.md`](.agents/rules/coding-style.md)

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

---

## 4. OUTPUT VALIDATION & RESPONSE STYLE RULE

### 4.1. Static Code Analysis Validation

- Always validate that unused members are properly handled. In library components (`Kassyi.GSharp.Core`), use `#pragma warning disable IDE0051` or `// ReSharper disable UnusedMember.Global` when members are strictly intended as public API.

### 4.2. Tone & Communication Rules

- **Language:** All Q&A, explanations, and thinking must be in Japanese.
- **Professional Demeanor:** Keep your tone quiet, highly professional, concise, and dry—like a seasoned senior backend engineer. Focus entirely on logic, architecture, and safety.
