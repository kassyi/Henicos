---
name: qa-testing-guidelines
description: Guideline for writing test code for C# and the GPU. A comprehensive set of QA rules that covers not only the happy path but also data-driven boundary values, concurrency stress, alignment and sad paths, and applies Trait attributes based on Feature IDs together with proper file separation and naming rules.
---

# QA Testing Guidelines (comprehensive test-writing guideline for agents)

When creating or modifying test code (xUnit) in this project (Henicos / GSharp), the agent **must** self-assess whether the following QA test viewpoints, structure rules and Feature ID traceability are met, and when something is missing, extend, add or rebuild the test cases on its own.

## 0. [Required] Test-first AI vibe coding (Phase 1)

When you start an implementation, **always generate the test code before writing any production code (the implementation)** (for transpiler-related tasks a Golden Master / Snapshot Test is required; otherwise write an appropriate Unit Test).

- This is the "common first phase" when starting a roadmap task.
- Based not only on the normal path but also on the "Sad Path tests" described below and the QA quality criteria, **design even which exceptions should be thrown** before starting the implementation.

## 1. Data-Driven Edge Case Testing

Do not stop at happy-path (normal value) tests; always inject extreme boundary values with `[Theory]`.

- **Numeric types**: `0`, `1`, `-1`, the minimum (`MinValue`), the maximum (`MaxValue`), only the most significant bit set (`0x80000000`), alternating bits (`0x55555555`), and so on.
- **Control flow**: Values at the boundary of an `if` or `switch` (for example, for `> 10`: `9`, `10`, `11`), and the fallback to the `default` route.
- **Arrays and Span**: An empty array (length 0), a one-element array, and a size right at the memory boundary.

For numeric and algorithm tests (BigInt / BigFloat / NTT in GpuMath, CPU-vs-GPU comparisons, and so on), settle how to define the properties, the input that reaches each check, and the confirmation that breaking one line makes it fail, following [property-test-design](../property-test-design/SKILL.md).

## 2. GPU-specific Concurrency & Stress Testing

For code that involves threads, warps, shared memory or atomic operations, deliberately create a high-load, high-contention situation and test it.

- **Atomic operations**: Write to and add to (`AtomicAdd`, etc.) the same memory address from thousands of threads at once (for example 256 threads x 16 blocks = 4096 threads), and check that the result matches exactly.
- **Barrier synchronization**: Check that reads and writes of shared memory across warp boundaries are synchronized correctly before and after `Gpu.SyncThreads()`.
- **Host side (C#)**: For components that require thread safety, such as `Logger` and the JIT compiler (`NvrtcCompiler`), use `Parallel.For` or similar to verify that concurrent access causes no deadlock or crash. Here, mind the Thread Local Context constraint of the CUDA API, and bind the context inside the worker thread as needed.

## 3. Alignment & Padding Testing

When handling structures (`struct`) and memory blocks, verify that the memory layout matches between the C# side and the GPU (CUDA C++) side.

- Define a structure that mixes `byte`, `double`, `short` and so on, and check that unintended padding does not shift offsets (an alignment violation).
- Check that accesses across a boundary are safe in `MemoryMarshal.Cast` and pointer arithmetic.

## 4. Sad Path / Exception Testing

Verify that when input or a situation that should produce an error is given, the system does not crash or fall into an infinite loop, but **detects the error properly and shuts it down safely**.

- **Static analysis (Analyzer) / Source Generator**: When unsupported syntax or an invalid type is used, the build must stop with an appropriate `Diagnostic` (for example `GSH0001`) instead of passing compilation.
- **Run time (Runtime)**: When an invalid argument is passed (for example a non-existent function name, a broken PTX string, or an invalid architecture such as `sm_999`), a clear exception (such as `CudaException`) must be thrown instead of being silently ignored.

## 5. Test naming rules and consistent use of domain natural language

Because the codebase has grown large, **hard-coding Feature IDs / ticket numbers in C# code has been abolished**.
Name test code in natural language that directly expresses the domain intent, in line with the C# Framework Design Guidelines. The link between roadmap tasks and tests is managed in external metadata (JSON / `roadmap.json`), not in the code.

- **Applying the orthogonal 3-axis metadata attributes (`TestTarget` / `Category` / `Scenario`)**:
    - Do **not** attach a ticket number or Feature ID as a `[Trait]`.
    - For classifying and filtering tests, use the following strongly typed (Enum) attributes (defined in `tests/Shared/Kassyi.GSharp.Tests.Infrastructure` / namespace `Kassyi.GSharp.Tests.Infrastructure`, and globally imported across the test projects):
        - **Axis 1 (execution target)**: `[TestTarget(ExecutionTarget.GpuExecution)]` / `[TestTarget(ExecutionTarget.CpuExecution)]` (`tests/Shared/Kassyi.GSharp.Tests.Infrastructure/ExecutionTarget.cs`)
        - **Axis 2 (feature domain)**: `[Category(TestCategory.Compiler)]` / `[Category(TestCategory.Runtime)]` / `[Category(TestCategory.Syntax)]` / `[Category(TestCategory.Stdlib)]` and so on (`tests/Shared/Kassyi.GSharp.Tests.Infrastructure/TestCategory.cs`)
        - **Axis 3 (test scenario)**: `[Scenario(TestScenario.HappyPath)]` / `[Scenario(TestScenario.SadPath)]` / `[Scenario(TestScenario.Boundary)]` (`tests/Shared/Kassyi.GSharp.Tests.Infrastructure/TestScenario.cs`)
    ```csharp
    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task GeneratorSnapshots_ThrowsOnInvalidSyntax(string fileName)
    ```
- **Naming rules for test methods, classes and files**:
    - Do not put a feature-ID prefix such as `GEN-08_` or `GBI-06_` in file names, class names or method names; use a natural-language name that expresses the behavior under test (for example `KernelFusionTests.cs`, `GpuBigIntegerTests.cs`, `Test_MemoryPoolAllocation`, `Test_SwitchExpression_Transpilation`).

## 6. Placement strategy for GPU kernel files (Colocation vs Centralization)

Use the following two strategies for placing test kernels and code under analysis (`.cs` / `.cu.cs`) strictly according to purpose.

- **Execution tests (Integration / Runtime / Syntax): colocate in the same folder (Colocation)**
    - For a test that actually launches a `[GpuKernel]` from the host and verifies the execution result (for example `Syn01_91_ControlFlowTests.cs`), place the kernel file it calls **in the same directory as the test file** (for example `Syn01_91_ControlFlowKernels.cu.cs`).
    - Reason: this keeps the test and the kernel highly cohesive and lets developers move between the execution contexts easily.

- **Static analysis and meta tests (Analyzer / Generator): isolate in TestCases (Centralization)**
    - Code that is loaded as a string (SyntaxTree) to be analyzed, and code that deliberately produces compile errors or warnings (Sad Path), must always be **isolated under the `TestCases/` directory (Source Separation)** so that it does not pollute the main test assembly or the build tree.
    - Location: `tests/Compiler/Kassyi.GSharp.Tests.Shared/TestCases/Analyzers/` and so on.