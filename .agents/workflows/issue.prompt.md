---
name: "/issue"
description: "Workflow that drafts and proposes an Issue conforming to the GitHub Issue Template (.github/ISSUE_TEMPLATE/) and creates it with the gh CLI"
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
    Start([Invoke /issue]) --> FetchLabels[1. Fetch Existing Labels & Classify Issue]
    FetchLabels --> Inspect[2. Inspect Repo for Reproduction / Context if needed]
    Inspect --> ReadTemplate[3. Read corresponding .github/ISSUE_TEMPLATE/*.yml]
    ReadTemplate --> Draft[4. Synthesize Structured Issue Draft & Labels (Max 2)]
    Draft --> Propose[5. Present Proposal (Title, Labels, Body) to User]
    Propose --> UserDecision{User Approval?}
    UserDecision -- Modifications Requested --> Revise[Revise draft and re-propose]
    Revise --> Propose
    UserDecision -- Approved (yes/y) --> WriteTemp[Write body to tmp/issue_body.md]
    WriteTemp --> Execute[6. Run gh issue create --label & --body-file]
    Execute --> Cleanup[Clean up tmp/issue_body.md & Report Issue URL]
```

---

## Step-by-Step Instructions

### Step 1: Existing Label Retrieval & Issue Category Identification

1. **Fetch Existing Repository Labels**:
   Retrieve the current labels registered in the repository:
   ```bash
   gh label list --limit 100 --json name,description
   ```

2. **Classify Issue Template & Select Labels (Max 2)**:
   Analyze the user's request and classify into one of the 3 templates:
   - **Bug Report** (`.github/ISSUE_TEMPLATE/bug_report.yml`)
     - Title format: `[Bug]: <Concise description>`
     - Primary Label: `bug`
   - **Feature / Syntax Request** (`.github/ISSUE_TEMPLATE/feature_request.yml`)
     - Title format: `[Feature]: <Concise description>`
     - Primary Label: `feature`
   - **Performance / Optimization** (`.github/ISSUE_TEMPLATE/performance.yml`)
     - Title format: `[Perf]: <Concise description>`
     - Primary Label: `performance`

   - **Multi-Topic Classification (1–2 Labels Maximum)**:
     - Select **1 to 2 labels** from the existing repository labels fetched above (e.g., `feature`, `refactor`, `bug`, `performance`, `test`, `documentation`, `chore`).
     - If the issue spans two distinct topics (e.g., a new feature combined with refactoring -> `feature,refactor`), attach both labels.
     - **Strict Cap**: Assign **at most 2 labels** (`1 <= count <= 2`) so that label classification remains focused and meaningful.

3. **Select the Milestone (exactly 1)**:
   Pick one milestone by the decision order in [`.agents/rules/issue-milestones.md`](../rules/issue-milestones.md) (`M1 Wrong Results`, `M2 Build Failures & Missing Diagnostics`, `M3 Unconfirmed Suspicions`, `M4 Bug-Finding Infrastructure`, `M5 Cleanup`, `Future`). List the current ones with `gh api repos/{owner}/{repo}/milestones --jq '.[].title'`.

If ambiguous, briefly ask the user to clarify the issue type.

### Step 2: Context Gathering & Repo Inspection

Collect technical details from the repository or system:

- For bugs: Search for relevant source files, minimal reproduction code in C#, expected vs actual behavior, stack traces or diagnostics. Run `just env-info` (`dotnet run tools/collect-env/collect-env.cs`) and use its output for the Environment Info field.
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

Present the proposed Issue to the user in the following structure. The proposal text itself (the headings, the reasons and the confirmation question) is written in Japanese, as in Safety & Execution Guardrail 4; the English below only describes what each part contains. **Note**: Do NOT wrap the Issue body in a code block (` ```markdown ... ``` `). Render it directly as formatted Markdown between horizontal rules (`---`) so that headings, checklists, and code samples render naturally:

```markdown
### Proposed Issue content

- **Title**: `[Bug]: ...`
- **Labels**: `<label1>` (or `<label1>, <label2>` - at most 2)
- **Milestone**: `<milestone>` (exactly 1; give the reason for the assignment in one sentence)

### Issue body:

---
<Markdown Content formatted per .github/ISSUE_TEMPLATE/*.yml>
---

May I create (or update) the GitHub Issue with this content? (yes / no / revision instructions)
```

### Step 5: Execution via `gh issue create`

Upon explicit approval from the user:

1. Ensure `tmp/` directory exists:
    ```powershell
    if (!(Test-Path -Path "tmp")) { New-Item -ItemType Directory -Path "tmp" }
    ```
2. Write the finalized issue body into `tmp/issue/issue_body.md` using `write_to_file`.
3. Run the `gh` command (passing the 1–2 selected labels separated by a comma and the approved milestone):
    ```powershell
    gh issue create --title "<Title>" --label "<label1,label2>" --milestone "<milestone>" --body-file tmp/issue/issue_body.md
    ```
4. Remove the temporary file:
    ```powershell
    Remove-Item -Path "tmp/issue/issue_body.md" -Force -ErrorAction SilentlyContinue
    ```
5. Report the created Issue URL to the user.
