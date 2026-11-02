#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// In-memory generator and transpiler verification for Lane D6 language-feature cases that currently produce
/// invalid C++ or unexpected diagnostics (<c>Q07 = a</c> / <c>Q21 = a</c>: kept out of <c>.cu.cs</c> native build
/// targets so <c>Tests.Cpu</c> and <c>Tests.Cuda</c> compile cleanly while pinning each bug's current emission).
/// </summary>
[Category(TestCategory.Compiler)]
[Scenario(TestScenario.SadPath)]
public class LanguageFeaturesPendingBugsTests
{
    private static string GetGeneratedDeviceCode(GeneratorDriver driver, string? treeHintSubstring = null)
    {
        var runResult = driver.GetRunResult();
        var trees = treeHintSubstring is null
            ? runResult.GeneratedTrees
            : runResult.GeneratedTrees.Where(t => t.FilePath.Contains(treeHintSubstring, StringComparison.Ordinal));
        return string.Join(
            "\n",
            trees.Select(static t => t.GetText().ToString()));
    }

    // S35: Generic [GpuDeviceFunction] lacks `template <typename __gsharp_tp_T>` header and drops explicit type arguments at call site.
    [Fact]
    public void Test_S35_GenericDeviceFunction_LacksTemplateHeaderAndCallTypeArgsInCpp()
    {
        const string Source = """
            using Kassyi.GSharp.Core;

            public static partial class S35GenericFunctionCase
            {
                [GpuDeviceFunction]
                public static T Pick<T>(T a, T b, bool takeFirst) where T : unmanaged =>
                    takeFirst ? a : b;

                [GpuKernel]
                public static void Kernel(int[] xs, int[] r)
                {
                    int tid = ThreadIndex.X;
                    r[tid] = Pick<int>(xs[tid], 42, true);
                }
            }
            """;

        var (driver, _, _) = RoslynTestUtility.RunGenerator(Source, "Cpu");
        string code = GetGeneratedDeviceCode(driver);

        // Generic [GpuDeviceFunction] fails with "unsubstituted type parameter" (no `template <typename ...>`)
        // and the call site drops `<int>`: `__gsharp_fn_S35GenericFunctionCase__Pick_a1(...)`
        Assert.Contains("unsubstituted type parameter", code, StringComparison.Ordinal);
        Assert.Contains("__gsharp_fn_S35GenericFunctionCase__Pick_a1(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("template <typename __gsharp_tp_T>", code, StringComparison.Ordinal);
    }

    // S36 (#464 / #790): 2-arg indexed StackLinq Where((x, i) => ...) / Select((x, i) => ...) emits 1-arg call in Fused C++,
    // and TakeWhile((x, i) => ...) / SkipWhile((x, i) => ...) emits non-At helper in Unfused C++.
    [Fact]
    public void Test_S36_StackLinqIndexedOverloads_EmitOneArgFusedCallAndNonAtUnfusedCall()
    {
        const string Source = """
            using System;
            using Kassyi.GSharp.Core;
            using Kassyi.ZeroAlloc.Linq;

            public static partial class S36IndexedStackLinqCase
            {
                [GpuKernel]
                public static void Kernel(int[] xs, int[] r)
                {
                    var slice = xs.AsSpan().Slice(0, 4);
                    int fused = slice.AsStackEnumerable().Where((x, i) => i < 2 && x > 0).Select((x, i) => x + i).Sum();
                    var unfused = slice.AsStackEnumerable();
                    int unfusedTw = unfused.TakeWhile((x, i) => i < 2).Sum();
                    int unfusedSw = unfused.SkipWhile((x, i) => i < 1).Sum();
                    r[0] = fused + unfusedTw + unfusedSw;
                }
            }
            """;

        var (driver, _, _) = RoslynTestUtility.RunGenerator(Source, "Cpu");
        string code = GetGeneratedDeviceCode(driver, "S36IndexedStackLinqCase");

        // Fused loop invokes the 2-parameter lambda `(auto __gsharp_id_4_x, auto __gsharp_id_5_i)` with a single `(__gsharp_val_0)` argument
        Assert.Contains("auto __gsharp_id_4_x, auto __gsharp_id_5_i", code, StringComparison.Ordinal);
        Assert.Contains(")(__gsharp_val_0))", code, StringComparison.Ordinal);

        // Unfused TakeWhile/SkipWhile with 2-arg lambda emits `StackLinq::TakeWhile(` instead of `StackLinq::TakeWhileAt(`
        Assert.Contains("StackLinq::TakeWhile(", code, StringComparison.Ordinal);
        Assert.Contains("StackLinq::SkipWhile(", code, StringComparison.Ordinal);
    }

    // S37 (#640 & new): StackLinq Aggregate called as static method `StackLinqExtensions.Aggregate(seq, func)`
    // or with reordered named arguments `seq.Aggregate(func: ..., seed: ...)` swaps seed and func in C++.
    [Fact]
    public void Test_S37_StackLinqAggregateStaticAndNamedArgs_MisbindsSeedAndAccumulatorLambda()
    {
        const string Source = """
            using System;
            using Kassyi.GSharp.Core;
            using Kassyi.ZeroAlloc.Linq;

            public static partial class S37AggregateBindingCase
            {
                [GpuKernel]
                public static void Kernel(int[] xs, int[] r)
                {
                    var slice = xs.AsSpan().Slice(0, 4);
                    int staticCall = StackLinqExtensions.Aggregate(slice.AsStackEnumerable(), (a, b) => a + b);
                    int namedReordered = slice.AsStackEnumerable().Aggregate(func: (a, b) => a + b, seed: 10);
                    r[0] = staticCall + namedReordered;
                }
            }
            """;

        var (driver, _, _) = RoslynTestUtility.RunGenerator(Source, "Cpu");
        string code = GetGeneratedDeviceCode(driver, "S37AggregateBindingCase");

        // In the named-argument call `Aggregate(func: ..., seed: 10)`, `AggregateStrategy` reads `Arguments[0]` (the lambda) as the seed
        // and invokes `Arguments[1]` (`10`) as the accumulator function: `(10)(__gsharp_fusion_res_1, __gsharp_val_0)`
        Assert.Contains("(10)(", code, StringComparison.Ordinal);
        Assert.Contains(", __gsharp_val_0);", code, StringComparison.Ordinal);
    }

    // S38: TakeLast / SkipLast followed by an unfused terminal (Single / ElementAt) falls back to `StackLinq::TakeLast`,
    // which is not defined in StackLinq.cuh.
    [Fact]
    public void Test_S38_StackLinqTakeLastWithUnfusedTerminal_EmitsUndefinedStackLinqTakeLast()
    {
        const string Source = """
            using System;
            using Kassyi.GSharp.Core;
            using Kassyi.ZeroAlloc.Linq;

            public static partial class S38TakeLastUnfusedCase
            {
                [GpuKernel]
                public static void Kernel(int[] xs, int[] r)
                {
                    var slice = xs.AsSpan().Slice(0, 4);
                    r[0] = slice.AsStackEnumerable().TakeLast(1).Single();
                }
            }
            """;

        var (driver, _, _) = RoslynTestUtility.RunGenerator(Source, "Cpu");
        string code = GetGeneratedDeviceCode(driver, "S38TakeLastUnfusedCase");

        Assert.Contains("StackLinq::Single(Kassyi::ZeroAlloc::Linq::StackLinq::TakeLast(", code, StringComparison.Ordinal);
    }

    // S39: ManagedExecutionRewriter_Statements checks `Identifier.Text == "InlinePtx"` syntactically without checking the symbol,
    // rewriting user-defined `[GpuDeviceFunction] InlinePtx` calls into `throw new NotSupportedException(...)`.
    [Fact]
    public void Test_S39_UserDefinedInlinePtxMethod_IsRewrittenToThrowNotSupportedExceptionOnManaged()
    {
        const string Source = """
            using Kassyi.GSharp.Core;

            public static partial class S39UserInlinePtxCase
            {
                [GpuDeviceFunction]
                public static void InlinePtx(ref int x)
                {
                    x += 42;
                }

                [GpuKernel]
                public static void Kernel(int[] r)
                {
                    int v = 10;
                    S39UserInlinePtxCase.InlinePtx(ref v);
                    r[0] = v;
                }
            }
            """;

        var (driver, _, _) = RoslynTestUtility.RunGenerator(Source, "Cpu");
        string code = GetGeneratedDeviceCode(driver, "S39UserInlinePtxCase");

        Assert.Contains("throw new global::System.NotSupportedException(\"Gpu.InlinePtx is not supported by the StandardCppDialect dialect\")", code, StringComparison.Ordinal);
    }

    // S40: A struct's Dispose() or CompareTo() containing a [GpuOperatorMacro] assignment is expanded in C++,
    // but ManagedVersionAnalysis / ManagedExecutionRewriter does not rewrite struct instance Dispose()/CompareTo().
    [Fact]
    public void Test_S40_OperatorMacroInsideStructDispose_ExpandsInCppButLeavesManagedStructDisposeUnrewritten()
    {
        const string Source = """
            using System;
            using Kassyi.GSharp.Core;

            public struct S40MacroVal
            {
                public int V;

                [GpuOperatorMacro(typeof(S40MacroOps), nameof(S40MacroOps.Add))]
                public static S40MacroVal operator +(S40MacroVal a, S40MacroVal b) => throw new GpuOnlyException();
            }

            public static partial class S40MacroOps
            {
                [GpuDeviceFunction]
                public static void Add(out S40MacroVal res, S40MacroVal a, S40MacroVal b)
                {
                    res = new S40MacroVal { V = a.V + b.V };
                }
            }

            public struct S40DisposableWithMacro : IDisposable
            {
                public int[] Log;
                public int X;

                public void Dispose()
                {
                    S40MacroVal a = new S40MacroVal { V = X };
                    S40MacroVal b = new S40MacroVal { V = 5 };
                    S40MacroVal c = default;
                    c = a + b;
                    Log[0] = c.V;
                }
            }

            public static partial class S40KernelHolder
            {
                [GpuKernel]
                public static void Kernel(int[] r)
                {
                    using var d = new S40DisposableWithMacro { Log = r, X = 10 };
                }
            }
            """;

        var (driver, _, _) = RoslynTestUtility.RunGenerator(Source, "Cpu");
        string code = GetGeneratedDeviceCode(driver, "S40KernelHolder");

        // C++ __gsharp_dispose expands the operator macro to __gsharp_fn_S40MacroOps__Add
        Assert.Contains("__gsharp_fn_S40MacroOps__Add(&__gsharp_id_", code, StringComparison.Ordinal);
        // Managed state machine still uses `using var d = ...` which calls `S40DisposableWithMacro.Dispose()` directly
        Assert.DoesNotContain("__gsharp_managed_Dispose", code, StringComparison.Ordinal);
    }

    // S41: Non-readonly struct property getter is emitted as `const` in C++ (failing MSVC C3490 when mutating a field),
    // and `using var` local is emitted as mutable `auto` without defensive copy on non-readonly member access.
    [Fact]
    public void Test_S41_MutatingStructPropertyGetterAndUsingVar_EmitsConstMemberFunctionAndMutableUsingVarInCpp()
    {
        const string Source = """
            using System;
            using Kassyi.GSharp.Core;

            public struct S41MutatingGuard : IDisposable
            {
                public int[] Log;
                public int State;

                public int StepState
                {
                    get
                    {
                        State += 100;
                        return State;
                    }
                }

                public void Dispose()
                {
                    Log[0] = State;
                }
            }

            public static partial class S41KernelHolder
            {
                [GpuKernel]
                public static void Kernel(int[] r)
                {
                    using var g = new S41MutatingGuard { Log = r, State = 5 };
                    r[1] = g.StepState;
                }
            }
            """;

        var (driver, _, _) = RoslynTestUtility.RunGenerator(Source, "Cpu");
        string code = GetGeneratedDeviceCode(driver);

        // Emits `const` on the mutating getter (which fails C++ compilation with C3490) and mutable `auto` for the `using var`
        Assert.Contains("__device__ inline int __gsharp_mb_StepState() const", code, StringComparison.Ordinal);
        Assert.Contains("auto __gsharp_id_1_g =", code, StringComparison.Ordinal);
    }

    // S42: Nested struct `Outer<T>.Inner` inside a generic outer class leaves outer type parameter `__gsharp_tp_T`
    // un-substituted in emitted C++ constructor/property bodies.
    [Fact]
    public void Test_S42_NestedStructInsideGenericOuterType_LeavesOuterTypeParameterUnsubstitutedInCpp()
    {
        const string Source = """
            using Kassyi.GSharp.Core;

            public static class S42GenericOuter<T> where T : unmanaged
            {
                public struct InnerBox
                {
                    public T Value;

                    public InnerBox(T v)
                    {
                        Value = default(T);
                        Value = v;
                    }
                }
            }

            public static partial class S42KernelHolder
            {
                [GpuKernel]
                public static void Kernel(int[] r)
                {
                    var box = new S42GenericOuter<int>.InnerBox(42);
                    r[0] = box.Value;
                }
            }
            """;

        var (driver, _, _) = RoslynTestUtility.RunGenerator(Source, "Cpu");
        string code = GetGeneratedDeviceCode(driver, "S42KernelHolder");

        // Outer type parameter `T` on `S42GenericOuter<int>.InnerBox` is not substituted by `StructTranspiler.SubstituteTypeParametersCore`,
        // causing `YamlDialectBase.AppendType` to fail with `GSH0003: ... 'unsubstituted type parameter'`
        Assert.Contains("GSH0003:", code, StringComparison.Ordinal);
        Assert.Contains("unsubstituted type parameter", code, StringComparison.Ordinal);
    }

    // S43: MemoryExtensions.Sort with an unqualified generic method group `CompareGeneric<int>` fails IsDeviceFunction with GSH0003.
    [Fact]
    public void Test_S43_SpanSortWithUnqualifiedGenericMethodGroup_ReportsGSH0003()
    {
        const string Source = """
            using System;
            using Kassyi.GSharp.Core;

            public static partial class S43GenericSortComparisonCase
            {
                [GpuDeviceFunction]
                public static int CompareGeneric<T>(T a, T b) where T : unmanaged => 0;

                [GpuKernel]
                public static void Kernel(int[] r)
                {
                    Span<int> s = stackalloc int[4];
                    s.Sort(CompareGeneric<int>);
                    r[0] = s[0];
                }
            }
            """;

        var (driver, _, _) = RoslynTestUtility.RunGenerator(Source, "Cpu");
        string code = GetGeneratedDeviceCode(driver, "S43GenericSortComparisonCase");
        Assert.Contains("GSH0003:", code, StringComparison.Ordinal);
    }

    // S44 (#674 & #963): Local function forward call, user-defined `explicit operator`, and custom struct indexer
    // produce C++ code that lacks forward declarations / explicit operator / custom operator[].
    [Fact]
    public void Test_S44_ExplicitConversionAndCustomIndexer_OmittedFromEmittedCppStruct()
    {
        const string Source = """
            using Kassyi.GSharp.Core;

            public struct S44ExplicitAndIndexedStruct
            {
                public int A;
                public int B;

                public static explicit operator S44ExplicitAndIndexedStruct(int v) =>
                    new S44ExplicitAndIndexedStruct { A = v * 2, B = v + 1 };

                public int this[int idx]
                {
                    get => idx == 0 ? A : B;
                    set
                    {
                        if (idx == 0) A = value;
                        else B = value;
                    }
                }
            }

            public static partial class S44KernelHolder
            {
                [GpuKernel]
                public static void Kernel(int[] xs, int[] r)
                {
                    int x = xs[0];
                    var s = (S44ExplicitAndIndexedStruct)x;
                    s[1] = x + 99;
                    r[0] = s[0] + s[1];
                }
            }
            """;

        var (driver, _, _) = RoslynTestUtility.RunGenerator(Source, "Cpu");
        string code = GetGeneratedDeviceCode(driver, "S44KernelHolder");

        // Emits C-style cast `(struct)x` and `s[1] = ...` in C++, while the struct definition has neither constructor/conversion nor operator[]
        Assert.Contains("(__gsharp_ty_S44ExplicitAndIndexedStruct)__gsharp_id_2_x", code, StringComparison.Ordinal);
        Assert.DoesNotContain("operator[](", code, StringComparison.Ordinal);
    }
}
