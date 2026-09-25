---
name: "/issue"
description: "GitHub Issue Template (.github/ISSUE_TEMPLATE/) に準拠した Issue を起票・提案し、gh CLI で作成するワークフロー"
---

# Autonomous Issue Creation Workflow (`/issue`)

You are an expert engineer and open-source project contributor.
Your mission is to understand the user's issue topic (bug report, feature request, or performance optimization), inspect the repository if needed for technical context, draft a comprehensive Issue body conforming strictly to the repository's [`.github/ISSUE_TEMPLATE/`](.github/ISSUE_TEMPLATE/), present it to the user for approval, and create the issue via the `gh` CLI.

---

## Safety & Execution Guardrails

1. **No Blind Creation**: Always propose the Issue title, labels, and complete body for user review before running `gh issue create` or `gh issue edit`. **Never wrap the issue body in a Markdown code block (` ```markdown ... ``` `)**; output it directly as formatted markdown so that headings, checklists, and code snippets render naturally in the chat UI. Only execute when the user replies affirmatively (e.g. `yes`, `y`, `proceed`).
2. **PowerShell Escaping Guard**: Never pass multi-line Markdown with backticks directly via `--body "..."` on Windows PowerShell (backticks will be swallowed or converted to backslashes). Always write the body to a temporary file `tmp/issue_body.md` and pass `--body-file tmp/issue_body.md`, then clean up the temporary file after creation.
3. **Template Conformity**: The drafted issue body must strictly follow the fields of the selected template in `.github/ISSUE_TEMPLATE/` (`bug_report.yml`, `feature_request.yml`, or `performance.yml`).
4. **Language**: Draft the Issue title and body in English by default to align with the OSS repository conventions, unless the user explicitly requests Japanese. Communication and proposals with the user must be in Japanese.

---

## Execution Lifecycle

```mermaid
graph TD
    Start([Invoke /issue]) --> Identify[1. Identify Issue Category & Context]
    Identify --> Inspect[2. Inspect Repo for Reproduction / Context if needed]
    Inspect --> ReadTemplate[3. Read corresponding .github/ISSUE_TEMPLATE/*.yml]
    ReadTemplate --> Draft[4. Synthesize Structured Issue Draft]
    Draft --> Propose[5. Present Proposal (Title, Label, Body) to User]
    Propose --> UserDecision{User Approval?}
    UserDecision -- Modifications Requested --> Revise[Revise draft and re-propose]
    Revise --> Propose
    UserDecision -- Approved (yes/y) --> WriteTemp[Write body to tmp/issue_body.md]
    WriteTemp --> Execute[6. Run gh issue create --body-file]
    Execute --> Cleanup[Clean up tmp/issue_body.md & Report Issue URL]
```

---

## Step-by-Step Instructions

### Step 1: Issue Category Identification

Analyze the user's request and classify into one of the 3 templates:

1. **Bug Report** (`.github/ISSUE_TEMPLATE/bug_report.yml`)
    - Title format: `[Bug]: <Concise description>`
    - Label: `bug`
2. **Feature / Syntax Request** (`.github/ISSUE_TEMPLATE/feature_request.yml`)
    - Title format: `[Feature]: <Concise description>`
    - Label: `enhancement`
3. **Performance / Optimization** (`.github/ISSUE_TEMPLATE/performance.yml`)
    - Title format: `[Perf]: <Concise description>`
    - Label: `performance`

If ambiguous, briefly ask the user to clarify the issue type.

### Step 2: Context Gathering & Repo Inspection

Collect technical details from the repository or system:

- For bugs: Search for relevant source files, minimal reproduction code in C#, expected vs actual behavior, stack traces or diagnostics.
- For features: Check relevant AST nodes, transpiler visitors (`Kassyi.GSharp.Compiler`), or runtime classes. Verify conformity with **Zero-Allocation** and **Static Determinism**.
- For performance: Check benchmark commands (`just bench-pi`, `just test`), profiling numbers, or kernel bottlenecks.

### Step 3: Draft Issue Body

Construct the markdown body adhering to the template fields:

#### For Bug Report:

- **Affected Component**: Select from `Compiler / Transpiler`, `Runtime`, `Stdlib / Math`, `CLI / Tooling / Tests`, `Other`.
- **Bug Description**: Concise description.
- **Minimal Reproduction Code (C#)**: Self-contained `[GpuKernel]` or host C# snippet.
- **Expected vs Actual Behavior**: Clear contrast of results or exceptions.
- **Error Output / Logs**: Stack trace, diagnostic code (e.g. `GSPxxxx`), nvcc/CUDA errors.
- **Environment Info**: OS, .NET SDK, CUDA Toolkit, GPU model, Driver version.

#### For Feature Request:

- **Feature Category**: `C# Syntax / AST Transpilation Support`, `CUDA Native / Intrinsic Support`, `Runtime / Host-Device Memory Optimization`, `Stdlib / Math Expansion`, or `Developer Experience (DX) / Tooling`.
- **Motivation / Use Case**: Why is this needed?
- **Proposed Syntax / API Design**: C# example code.
- **Expected CUDA / PTX Mapping**: If known.
- **Design Constraints**: Confirmation of Zero-Allocation and Static Determinism.

#### For Performance:

- **Target Kernel / Component**: Specific kernel or method.
- **Current Profiling / Benchmark Data**: Execution times, occupancy, register spills.
- **Optimization Proposal / Direction**: Algorithmic or CUDA optimization ideas.

### Step 4: Proposal & User Confirmation

Present the proposed Issue to the user in the following structure. **Note**: Do NOT wrap the Issue body in a code block (` ```markdown ... ``` `). Render it directly as formatted Markdown between horizontal rules (`---`) so that headings, checklists, and code samples render naturally:

```markdown
### 提案する Issue 内容

- **Title**: `[Bug]: ...`
- **Labels**: `bug`

### Issue 本文:

---
<Markdown Content formatted per .github/ISSUE_TEMPLATE/*.yml>
---

この内容で GitHub Issue を作成（または更新）してよろしいですか？ (yes / no / 修正指示)
```

### Step 5: Execution via `gh issue create`

Upon explicit approval from the user:

1. Ensure `tmp/` directory exists:
    ```powershell
    if (!(Test-Path -Path "tmp")) { New-Item -ItemType Directory -Path "tmp" }
    ```
2. Write the finalized issue body into `tmp/issue/issue_body.md` using `write_to_file`.
3. Run the `gh` command:
    ```powershell
    gh issue create --title "<Title>" --label "<Label>" --body-file tmp/issue/issue_body.md
    ```
4. Remove the temporary file:
    ```powershell
    Remove-Item -Path "tmp/issue/issue_body.md" -Force -ErrorAction SilentlyContinue
    ```
5. Report the created Issue URL to the user.
