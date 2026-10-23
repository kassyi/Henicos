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

### 1.3. Ban on Null-Forgiving Operators (`!`)

- **DO NOT use the null-forgiving operator (`!`) when avoidable.** If an explicit `if` check, pattern matching, null-coalescing throw (`?? throw`), empty collection initialization (`= []`), or properly typed nullable signature (`T?`) can prevent it, using `!` is strictly forbidden.
- **Strict Exception & Mandatory Justification Comment**:
  - The use of `!` is permitted **ONLY** in rare cases where language/API limitations make avoiding it excessively convoluted (e.g., deliberate `null!` argument passing in unit tests asserting `ArgumentNullException`, or downstream Roslyn AST/generator pipelines with invariant non-null semantics).
  - Whenever `!` is used, it **MUST be accompanied by a clear comment explaining why it is unavoidable and safe** (e.g., `// Intentionally passing null to assert ArgumentNullException guard`, `// Roslyn AST guarantees non-null child when parent is valid`). Uncommented `!` is strictly prohibited.
- **Enforcement & Alternatives:**
  - **Defensive guards:** Check explicitly with pattern matching or guards (`if (foo is null) throw ...` or `foo ?? throw ...`).
  - **Uninitialized fields:** For benchmark/lifecycle fields without constructor injection, initialize with empty values (`= []`, `= Array.Empty<T>()`) or make them explicitly nullable (`T?`).
  - **Domain modeling:** If a property or parameter can be null under failure or before execution, model its type as nullable (`T?`) or use a result pattern rather than concealing nullability with `null!`.

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

### 2.3. Mathematical Domain Naming & Academic Conventions

While .NET Framework Design Guidelines (FDG) generally avoid abbreviations, specialized mathematical, cryptographic, and algorithmic notations (e.g., $P, Q, T$ in hypergeometric binary splitting, $X, Y, Z, W$ in vectors, $P, Q, D$ in `RSAParameters`) represent domain-standard exceptions. In these scenarios, adhere to the following scope separation principle:

- **Type Names (Classes / Structs):** Must use full, descriptive words without abbreviations, accurately representing the overarching concept (e.g., prefer `GpuHypergeometricTerms` or concept-oriented type names over standalone acronyms).
- **Member Names (Properties / Fields):** Preserve canonical mathematical symbols (e.g., `P`, `Q`, `T`, `X`, `Y`, `Z`, `M11`) when translating them into verbose English phrases would break 1:1 parity with underlying research papers or mathematical formulas.
- **Parameters & Arguments:** Single-letter mathematical arguments (e.g., `x`, `y`) are permissible in arithmetic operations where they correspond directly to formal equations (matching `System.Math`).
- **Mandatory XML Documentation:** Whenever single-letter symbols or domain-specific abbreviations are used for members, you **MUST** provide comprehensive XML documentation explicitly linking each symbol to its mathematical definition:

```csharp
/// <summary>
/// Represents the binary splitting state <c>(P, Q, T)</c> for hypergeometric series summation.
/// </summary>
/// <remarks>
/// Defined according to the Binary Splitting algorithm:
/// <list type="bullet">
///   <item><description><see cref="P"/>: <c>P(a, b) = \prod p(k)</c></description></item>
///   <item><description><see cref="Q"/>: <c>Q(a, b) = \prod q(k)</c></description></item>
///   <item><description><see cref="T"/>: <c>T(a, b) = \sum ...</c></description></item>
/// </list>
/// </remarks>
```

### 2.4. Zero-Allocation & LINQ Policy (`RS0030` — `System.Linq` Banned / `StackLinq` Permitted)

- **Banned `System.Linq`**: Standard `System.Linq` is strictly prohibited across the codebase due to heap allocations, enumerator boxing, and closure captures (`RS0030`).
- **Permitted & Encouraged `Kassyi.ZeroAlloc.Linq` (`StackLinq`)**:
  - Our in-house `StackLinq` (`using Kassyi.ZeroAlloc.Linq;`) is a `ref struct` / value-type zero-allocation LINQ engine. It is **explicitly allowed** across host code, compiler/runtime utilities, tests, and GPU kernels (where supported by `StackLinqTranspilerRule` / `StackLinqFusionRule`).
  - **Quick Example**:
    ```csharp
    using Kassyi.ZeroAlloc.Linq;

    // Fluent pipeline executed purely on stack/registers (0 B heap allocation)
    int sum = data
        .AsStackEnumerable()
        .Where(x => x > 0)
        .Select(x => x * 2)
        .Sum();
    ```
  - For detailed architecture, API reference, and benchmarks, see: [StackLinq Architecture & Specification](../../docs/spec/en/03_bcl_api/linq/00_index.md).
  - **In the G# compiler** (`src/Compiler/Kassyi.GSharp.Compiler`, a netstandard2.0 analyzer): use the copy built into the compiler, `Kassyi.ZeroAlloc.Linq.Embedded` (globally imported). `Kassyi.ZeroAlloc.Linq.Export` generates it from the net10.0 sources on every StackLinq build, so only those sources are maintained. It has no span enumerators, `AsParallel` or generic-math `Sum`. Start a query with `.AsStackEnumerable()` on an array, `ImmutableArray<T>`, `EquatableArray<T>`, `IReadOnlyList<T>`, `HashSet<T>`, `Dictionary<TKey, TValue>`, `SyntaxList<T>`, `SeparatedSyntaxList<T>`, `SyntaxTokenList` or a node walk (`DescendantNodes()` and other `IEnumerable<SyntaxNode>`; no other lazy `IEnumerable`), and prefer it to a hand-written loop for a query (`Any`, `All`, `Count`, `First`, `Where` / `Select` chains), whether the loop is a `foreach` or an indexed `for`. The host-only operators cover the loops that collect or act on each element: `AddTo` / `AddIf` (add to a collection the caller owns), `Do` / `DoIf` (run an action per element), `SelectMany` (flatten), `TryFirst` (first match and whether one was found) and `IndexOf`. A kernel reports them with GSH0003. Keep a loop when it emits output while iterating, passes a `ref` local, returns several values through `out`, looks at neighbouring elements or groups into a dictionary. Files that `Kassyi.GSharp.Compiler.Generators` (a net10.0 tool) also links (`SourceWriter.cs`, `YamlSubsetParser.cs`, `NativeApiYamlReader.cs`, `NativeApiDefinition.cs`, `NativeApiDocumentation.cs`, `NativeIntrinsicEmitter.cs`) cannot use the embedded copy; keep their loops. Use `static` lambdas so that no closure is allocated; when the lambda needs an outer value, pass it through the state overload (`Where`, `Select`, `Count`, `Any`, `All`, `First`, `FirstOrDefault`), as in `.Any(shortName, static (a, name) => a.IsAttribute(name))`. Pass several values as a tuple. The state overloads are host-only; inside a kernel, a lambda reads outer values directly.
  - **Chain layout**: put every call of the chain on its own line, `.AsStackEnumerable()` included, one level deeper than the source, even when only one operator follows. A long condition then grows downward instead of to the right. When an argument spans several lines (a multi-line lambda, a nested chain), close the call on its own line, aligned with the `.Operator(` that opened it; never stack closers (`)))`). A call that follows such a closer continues on the closer line (`).AddTo(names);`).
    ```csharp
    bool hasAttribute = symbol.GetAttributes()
        .AsStackEnumerable()
        .Any(shortName, static (a, name) => a.IsAttribute(name));

    int count = parameters
        .AsStackEnumerable()
        .Where(static p => !p.IsConstantParameter())
        .Select(static p => p.Type)
        .Count();

    bool takesOwnership = type.Constructors
        .AsStackEnumerable()
        .Any(static ctor => ctor.Parameters
            .AsStackEnumerable()
            .Any(HasTakesOwnershipAttribute)
        );

    type.Constructors
        .AsStackEnumerable()
        .SelectMany(static ctor => ctor.Parameters
            .AsStackEnumerable()
        ).Select(static p => p.Name)
        .AddTo(names);
    ```
- **Prohibited Over-Refactoring (Preserve Imperative Loops)**:
  - **GPU Kernels (`*.cu`, `*.cu.cs`)**: Never use LINQ. Keep imperative `for` loops so AST lowering can map cleanly to native unrolling (`#pragma unroll`).
  - **Benchmark Measurement Loops**: Keep raw imperative loops in CPU baseline comparisons (e.g., `SgemmBenchmark`, `BlackScholesBenchmark`) to avoid skewing benchmark overhead.
  - **State Machines & Stream Parsers**: Keep `while` / `for` for IO streams, reader states, and buffer initializations (e.g., `leftArray[i] = random.Next()`).

---

## 3. GPU KERNEL DEVELOPMENT CONVENTIONS

### 3.1. GPU Kernel Naming Convention (.cu.cs)

Any C# file that contains G# GPU kernel implementations (e.g., methods annotated with `[GpuKernel]` or `[GpuDeviceFunction]`) **MUST** use the `.cu.cs` extension (e.g., `GpuBigIntMulKernels.cu.cs`). This convention signals to the system and developers that the file contains transpilation targets for CUDA C++.

- **Separation in Test Projects (`tests/`):**
  - Test files containing test runner/assertion logic (e.g., `FooTests.cs`) **MUST NOT** directly contain `[GpuKernel]` implementations.
  - Test helper kernels **MUST** be separated into dedicated `.cu.cs` files matching the corresponding test file name (e.g., `FooTests.cs` -> `FooTestKernels.cu.cs` or `FooKernels.cu.cs`).
  - This ensures clean separation between test execution code and transpiler target kernels, avoiding rule violations and keeping wrapper generation predictable.

---

## 4. OUTPUT VALIDATION & RESPONSE STYLE RULE

Before presenting any code modification, refactored files, or responding to the user:

### 4.1. Static Code Analysis Validation

1. **Mental Check:** Validate your generated code against this guidelines document.
2. **Diagnostic Pre-emption:** Ensure your output does not trigger any warnings or errors from Roslyn Analyzers or ReSharper. Leave the workspace clean and compliant.
3. **Unused Members Suppression:** Always validate that unused members are properly handled. In library components (`Kassyi.GSharp.Core`), use `#pragma warning disable IDE0051` or `// ReSharper disable UnusedMember.Global` when members are strictly intended as public API.

### 4.2. Tone & Communication Rules

- **Language:** Conduct all explanations, walkthroughs, Q&As, and thinking processes in Japanese.
- **Professional Demeanor:** Keep your tone quiet, highly professional, concise, and dry—like a seasoned senior backend engineer. Do not adopt an overly optimistic or apologetic persona. Focus entirely on logic, architecture, and safety.
