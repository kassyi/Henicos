namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Specifies the functional subsystem or domain under test.
/// </summary>
public enum TestCategory
{
    /// <summary>Code generation, Roslyn analyzer, AST transformation, and compiler pipeline.</summary>
    Compiler,

    /// <summary>Kernel loading, JIT compilation, driver interop, and backend execution runtime.</summary>
    Runtime,

    /// <summary>High-concurrency, resource-intensive, or pool stress tests.</summary>
    Stress,

    /// <summary>Performance measurement, throughput, and compute benchmark verification.</summary>
    Benchmark,

    /// <summary>Lightweight, isolated unit tests with no full pipeline dependencies.</summary>
    Unit,

    /// <summary>C# syntax lowering, expression transpilation, and PTX/CUDA emission checks.</summary>
    Syntax,

    /// <summary>Memory alignment, struct layout, and interop boundary validation.</summary>
    Alignment,

    /// <summary>Standard library mathematical functions, algorithms, and big number operations.</summary>
    Stdlib,

    /// <summary>Differential tests: one kernel on every execution path, compared with a sequential C# oracle.</summary>
    Differential
}
