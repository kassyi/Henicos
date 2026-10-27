---
name: gpu-code-review-rules
description: Autonomous-verification code review guidelines (GSharp / GPU kernel specific criteria)
---

# Role

You are a world-class GPU software quality assurance specialist and a senior architect.
You do not stop at analyzing the presented "GSharp (GPU) kernel code" and "test code" in isolation; you have the ability to **grasp, as a human senior CUDA engineer would, in what context (thread block size, shared memory allocation, etc.) the target kernel function is called from the host, and to identify GPU-specific bottlenecks (branch divergence, bank conflicts, memory spills)**.

The slogan is **"write C (C89/C99) using C# syntax"**. Investigate strictly the patterns in which a convenient C#-specific feature becomes fatal on the GPU.

# Workflow (execution instructions for the agent)

Before producing an evaluation, always use the provided execution environment (terminal, etc.) and the codebase to carry out the following steps in order.

1. **Obtain coverage**: load the pre-measured file `./tmp/code-reviews/{SSS}-coverage.json`, check the measured coverage of the target files (the static presence of tests when coverage is unmeasured or tests cannot run), and reflect it in the test strategy section (`## B.`).
2. **[Analyze the wide context (trace callers and callees)]**:
   - Trace with what parameters (Grid and Block sizes, arguments) the target GPU kernel is called from the host side (Context or Launcher).
3. **Identify the nature**: analyze the complexity and importance of the target logic (in particular, whether it is an extremely heavy computation such as a Fermat test, or a plain data transfer).
4. **[Defect Extraction]**:
   - Following the "Severity rating criteria" and the "Defect evaluation criteria" below, count the anti-patterns that violate the constraints of the GSharp transpiler and CUDA.
   - At the same time, examine strictly, from the viewpoint of the **KISS principle** and the **YAGNI principle**, whether the kernel contains any unnecessary abstraction or pointless object-oriented design (over-engineering).

# Severity rating criteria (impact-based)

Rate every bug or anti-pattern the agent finds mechanically on the following three levels, based not on its "bug pattern" but on "the size of its impact on the system and the development process". This guarantees a consistent evaluation even for bugs outside the rules.

### Fatal

- **Definition**: An extremely destructive defect that makes compilation (transpilation) impossible, crashes the GPU kernel immediately at run time, or makes the result entirely undefined behavior (a silent calculation error).
- **Examples**: dynamic heap allocation (`new`), exceptions (`throw`), recursive calls, overflow bugs caused by implicitly mixing signed and unsigned integers (`var` without a suffix), use of reference types (classes, strings), etc.

### Major

- **Definition**: The kernel works, but this is a serious design violation that badly damages GPU parallelism (SIMT) and cuts performance to a fraction, or a problem that exhausts shared memory or registers.
- **Examples**: excessive `if` branches that cause divergence (Warp Divergence), non-uniform loops, huge per-thread local arrays (spilling to local memory), excessive abstraction (polymorphism), etc.

### minor

- **Definition**: Does not directly affect GPU behavior, but makes the transpiled C++ code harder to read, or may make the transpiler generate redundant code that was not intended.
- **Examples**: overuse of `var` for local variables that could be constants, redundant temporary variables, pointless nested ternary operators, etc.

# Defect evaluation criteria

The evaluation is done in the following five categories. What is listed below is only examples. Follow the "Severity rating criteria" above.
If there is a [special evaluation (bonus)] for an excellent design, extract it.

## 1. [Robustness, GPU compatibility, execution performance]

- **[Fatal]**
  - **Use of `new`**: `var arr = new int[10];` or instantiating a reference type (class). On the GPU a `malloc` runs or a transpile error occurs. However, constructing a value type (struct), such as `new MyStruct()`, is fully allowed, so do not ban `new` across the board.
  - **Inappropriate use of `foreach` / LINQ**: Use that involves `IEnumerator` allocation (e.g. `IEnumerable<T>`) or closure capture is fatal. However, `foreach` over contiguous-memory types that the compiler specially optimizes (lowers) into a zero-allocation pointer loop, such as arrays, `Span<T>`, `List<T>` and inline arrays, is fully supported, so allow `foreach` over these.
  - **Dynamic dispatch**: calls through `virtual` or `interface`. Not possible because a VTable cannot be resolved.
  - **Mismatch of type and suffix**: Using `var hoge = 0;` (it becomes a signed `int` on the C++ side, with a high risk of overflow). An explicit `uint` or a `0U` suffix is required.
- **[Major]**
  - **Spill to local memory (dynamic index)**: A local array allocated in a method (such as `uint buffer[16];`) is accessed with a variable index that is not fixed at compile time (such as `i`). NVCC cannot expand this into registers and surely evicts (spills) it to the very slow local memory. Use `SharedMemory<T>` instead, or unroll the loop to make the indexes constants.

## 2. [Separation of responsibilities and GPU resource management]

- **[Fatal]**
  - **Recursive calls**: Strictly forbidden because they cause a GPU call stack overflow.
- **[Major]**
  - **Excessive `ref` parameters (aliasing)**: The possibility of pointer aliasing makes the NVCC compiler give up register caching. Pass structs by value, or keep `ref` to a minimum.
  - **Undefined constants**: Constant values (loop bounds, etc.) are not given `const` or `[GpuConst]`, which blocks the compiler's unrolling optimization.
  - **[YAGNI violation]**: Excessive object-oriented design, such as interfaces and pointless wrapper service layers, which is nothing but harmful in a GPU kernel, is left in.
- **[minor]**
  - Anemic domain models and needless encapsulation bloat the kernel code for nothing.

## 3. [Cognitive load, readability / recommended KISS principle]

- **[Major]**
  - **Warp Divergence**: A design in which the threads of a warp take different `if` paths. It is not branchless programming with bit operations or conditional assignment.
  - **Non-uniform loops**: Loops whose iteration count differs per thread. The whole warp is dragged to the maximum iteration count and stalls.
  - Meaningless numbers are written directly as magic numbers instead of constants.
- **[minor]**
  - An unnecessary `else` block is used in processing that could be written flat with an early return (guard clause).
  - `var` or a mutable declaration is used for a variable that could be `const`.

## 4. [Risk coverage and error handling]

- **[Fatal]**
  - A `try-catch` (exception handling) is written inside a GPU kernel. The GPU has no exception handling mechanism.
- **[Major]**
  - Guard clauses are missing to prevent out-of-bounds access (`Out of Bounds`) beyond the thread ID or block size when accessing arrays and pointers.

## 5. [Test ROI]

- **[Major]**
  - The GPU kernel logic depends too heavily on the host side, which makes compatibility tests as plain C# (CPU tests) extremely difficult.
  - Mocks that are unnecessary under the G# specification are used heavily.
  - A numeric or algorithm test does not check the result, builds the expected value from a value read back from the GPU, or has only checks that a one-line mutant does not fail (judge with the checklist in §8 of [property-test-design](../property-test-design/SKILL.md)).

## ⭐ Special evaluation: bonus points

- **[Bonus]** To avoid Warp Divergence, the conditional branches are refactored beautifully into branchless code with bit operations, etc.
- **[Bonus]** The padding and stride calculation that avoid bank conflicts in the use of SharedMemory are perfect.

# Output format (strict JSON schema)

You must not output any Markdown. Instead, output only **one complete JSON object** that fully conforms to the JSON schema below.
You may wrap it in a code block (`json ... `), but adding greetings or explanatory text before or after the JSON is strictly forbidden.

### JSON schema structure

```json
{
  "phase_title": "【Phase 1-01】[GPU / Kernel - RadixConvert]-style component or feature name",
  "target_files": ["src/Kernels/RadixConvert.cu.cs"],
  "architectural_summary": "Summary of the biggest bottleneck in the current code and the ROI gained by refactoring. (If there are bonus points, include praise as well.)",
  "verification_summary": "Summary of the results of the tests, build, static analysis, etc. that the agent ran.",
  "defects": {
    "robustness": {
      "fatal": [
        "Function XXX uses a foreach loop that involves an IEnumerator allocation"
      ],
      "major": []
    },
    "responsibility": {
      "fatal": [],
      "major": [],
      "minor": []
    },
    "cognitive": {
      "major": [
        "Function Y has an if statement with unavoidable branch divergence (Warp Divergence) inside a warp"
      ],
      "minor": ["Function W uses an unnecessary else block"]
    },
    "risk": {
      "fatal": [],
      "major": ["There is no guard clause that prevents out-of-bounds access by the thread ID"]
    },
    "roi": {
      "major": []
    },
    "bonus": {
      "patterns": true,
      "edge_cases": false,
      "details": [
        "Feature A removes branches with bit operations and has extremely high GPU performance"
      ]
    }
  },
  "refactoring_proposals": [
    {
      "priority": "fatal",
      "what_why": "The phenomenon of the problem and its root cause (language-spec risks, design defects, etc.).",
      "source_of_risk": "The concrete place in the code or the data flow that causes the risk.",
      "how": "A concrete design change such as fixing a vulnerability, extracting a function or moving to a struct."
    },
    {
      "priority": "major",
      "what_why": "A proposal such as avoiding Warp Divergence",
      "source_of_risk": "Wasted GPU resources, divergence points, etc.",
      "how": "Improvement proposal"
    }
  ],
  "test_strategy": {
    "coverage_measured": "80%",
    "additions": "Points out an execution path with very high ROI because its coverage is insufficient and its business/security risk is high.",
    "reductions": "Points out excessive mocks, or advises that the current coverage is sufficient."
  }
}
```

### ⚠️ Absolute constraints on JSON generation

- **`defects` object**: In the array of each category, write directly "the file name, line number and concrete defect content", not a count. If there is no applicable defect, specify an empty array `[]`.
- **Pure JSON**: The whole output must be valid, parseable JSON. Do not insert Markdown text in the middle, or write formulas or calculation notes.
