#nullable enable
using System.Buffers;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Runtime.Interop;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

/// <summary>
/// Unit tests for <see cref="LaunchAliasPlan"/> verifying candidate selection and activation for structs holding
/// non-array view fields (including nested structs, inline arrays, nullable views, arrays of structs, and 0-candidate
/// multi-view structs).
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class LaunchAliasPlanTests
{
    private static readonly string Source = RoslynTestUtility.ReadTestCase("Marshaling/LaunchAliasPlanSource.cs");
    private static readonly Compilation SharedCompilation = RoslynTestUtility.CreateCompilation(Source);

    private static (LaunchAliasPlan Cuda, LaunchAliasPlan Cpu, KernelHostModel Model) PlanFor(string methodName)
    {
        var type = SharedCompilation.GetTypeByMetadataName(
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.LaunchAliasPlanTestKernels");
        Assert.NotNull(type);
        var method = type.GetMembers(methodName).OfType<IMethodSymbol>().Single();
        var model = KernelHostModel.Create(method, default);
        var cuda = ArraySharingPlan.ForCuda(model, isDevicePtr: false).Aliases;
        var cpu = ArraySharingPlan.ForCpu(model).Aliases;
        return (cuda, cpu, model);
    }

    private const string NestedCopyArraysSource = """
        using System;
        using System.Collections.Immutable;
        using System.Runtime.InteropServices;
        using Kassyi.GSharp.Core;

        namespace Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.CpuCopyArrays;

        public struct CopyArraysArrayBag
        {
            public int[] Data;
        }

        public struct CopyArraysMemoryArrayBag
        {
            public Memory<int>[] Views;
        }

        public struct CopyArraysJaggedArrayBag
        {
            public int[][] Arrays;
        }

        public struct CopyArraysNestedMemoryBag
        {
            public Memory<Memory<int>> Views;
        }

        public struct CopyArraysNullableMemoryArrayBag
        {
            public Memory<int>?[] OptionalViews;
        }

        public struct CopyArraysReadOnlyNestedMemoryBag
        {
            public ReadOnlyMemory<Memory<int>> Views;
        }

        public struct CopyArraysTwoReadOnlyNestedMemoryBags
        {
            public ReadOnlyMemory<Memory<int>> First;
            public ReadOnlyMemory<Memory<int>> Second;
        }

        public struct CopyArraysImmutableNestedMemoryBag
        {
            public ImmutableArray<Memory<int>> Views;
        }

        public struct CopyArraysThreeLevelMemoryBag
        {
            public Memory<Memory<Memory<int>>> Views;
        }

        public struct CopyArraysJaggedDeepBag
        {
            public CopyArraysArrayBag[][] Nested;
        }

        public static partial class NestedCopyArraysKernels
        {
            [GpuKernel]
            public static void InMemoryArrayBags([In] CopyArraysMemoryArrayBag[] bags, [In] Span<int> slice)
            {
            }

            [GpuKernel]
            public static void InJaggedArrayBags([In] CopyArraysJaggedArrayBag[] bags, [In] Span<int> slice)
            {
            }

            [GpuKernel]
            public static void InNestedMemoryBags([In] CopyArraysNestedMemoryBag[] bags, [In] Span<int> slice)
            {
            }

            [GpuKernel]
            public static void InNullableMemoryArrayBags([In] CopyArraysNullableMemoryArrayBag[] bags, [In] Span<int> slice)
            {
            }

            [GpuKernel]
            public static void InReadOnlyNestedMemoryBags([In] CopyArraysReadOnlyNestedMemoryBag[] bags, [In] Span<int> slice)
            {
            }

            [GpuKernel]
            public static void InTwoReadOnlyNestedMemoryBags([In] CopyArraysTwoReadOnlyNestedMemoryBags[] bags, [In] Span<int> slice)
            {
            }

            [GpuKernel]
            public static void InImmutableNestedMemoryBags([In] CopyArraysImmutableNestedMemoryBag[] bags, [In] Span<int> slice)
            {
            }

            [GpuKernel]
            public static void InThreeLevelMemoryBags([In] CopyArraysThreeLevelMemoryBag[] bags, [In] Span<int> slice)
            {
            }

            [GpuKernel]
            public static void InJaggedDeepBags([In] CopyArraysJaggedDeepBag[] bags, [In] int[] whole)
            {
            }
        }
        """;

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("SingleArrayBagAlone")]
    [InlineData("TwoArrayBagsAlone")]
    [InlineData("SingleViewBagAlone")]
    public void ForCuda_StructHoldersWithoutViewOverlapPossibility_RemainInactive(string methodName)
    {
        var (cuda, cpu, _) = PlanFor(methodName);

        Assert.False(cuda.IsActive);
        Assert.False(cpu.IsActive);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ForCuda_SingleViewBagWithWholeArrayCandidate_ActivatesWithCandidate()
    {
        var (cuda, cpu, model) = PlanFor("SingleViewBagWithArrayCandidate");

        Assert.True(model.Parameters[0].HoldsViewFields);
        Assert.False(model.Parameters[0].HoldsSeveralArrayLikes);
        Assert.True(cuda.IsActive);
        Assert.True(cuda.HasHeldArrays);
        Assert.Equal([0], cuda.HeldSources.ToArray());
        Assert.Equal([1], cuda.Candidates.ToArray());
        Assert.False(cpu.IsActive);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("StructWithTwoViewsAlone")]
    [InlineData("NestedTwoViewsAlone")]
    [InlineData("InlineArrayOfTwoViewsAlone")]
    [InlineData("TwoSingleViewBagsAlone")]
    [InlineData("ArrayBagAndViewBagAlone")]
    public void ForCuda_ZeroCandidateMultiViewLaunch_ActivatesWithEmptyCandidates(string methodName)
    {
        var (cuda, cpu, _) = PlanFor(methodName);

        Assert.True(cuda.IsActive);
        Assert.True(cuda.HasHeldArrays);
        Assert.Empty(cuda.Candidates);
        Assert.Equal(-1, cuda.MemberIndexOf(0));
        Assert.False(cpu.IsActive);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ForCuda_ArrayOfViewBagsAndWholeArray_ActivatesOnCudaAndCpu()
    {
        var (cuda, cpu, model) = PlanFor("ArrayOfViewBagsAndWholeArray");

        Assert.True(model.Parameters[0].HoldsViewFields);
        Assert.True(model.Parameters[0].HoldsSeveralArrayLikes);
        Assert.True(cuda.IsActive);
        Assert.Equal([0], cuda.HeldSources.ToArray());
        Assert.Equal([0, 1], cuda.Candidates.ToArray());
        Assert.True(cpu.IsActive);
        Assert.Equal([0], cpu.HeldSources.ToArray());
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ForCuda_OutViewBagAndSlice_ExcludesOutFromHeldSources()
    {
        var (cuda, _, _) = PlanFor("OutViewBagAndSlice");

        Assert.True(cuda.IsActive);
        Assert.False(cuda.HasHeldArrays);
        Assert.Equal([0, 1], cuda.Candidates.ToArray());
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ForCuda_NullableViewFieldAndWholeArray_ActivatesWithHeldSource()
    {
        var (cuda, _, model) = PlanFor("NullableViewFieldAndWholeArray");

        Assert.True(model.Parameters[0].HoldsViewFields);
        Assert.True(cuda.IsActive);
        Assert.True(cuda.HasHeldArrays);
        Assert.Equal([0], cuda.HeldSources.ToArray());
        Assert.Equal([1], cuda.Candidates.ToArray());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ForCpu_InArrayOfViewBagsAndInSlice_ActivatesWithHeldSourcesAndCopies()
    {
        var (cuda, cpu, _) = PlanFor("InArrayOfViewBagsAndInSlice");

        Assert.True(cuda.IsActive);
        Assert.True(cuda.HasHeldArrays);
        Assert.True(cpu.IsActive);
        Assert.True(cpu.HasHeldArrays);
        Assert.Equal([0], cpu.HeldSources.ToArray());
        Assert.Equal([0, 1], cpu.Candidates.ToArray());
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cuda")]
    [InlineData("Cpu")]
    public void EmitAndCompile_SiblingNestedViewFields_CompilesWithoutErrors(string targetDialect)
    {
        var (_, outputCompilation, generatorDiagnostics) = GeneratorTestRunner.RunGenerator(
            SharedCompilation, targetDialect);

        Assert.Empty(generatorDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
        var compileErrors = outputCompilation
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(compileErrors);
        Assert.NotNull(outputCompilation.GetTypeByMetadataName(
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.LaunchAliasPlanTestKernels+AotContexts"));

        using var stream = new MemoryStream();
        var emitResult = outputCompilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void AliasHeldArrays_NullableNestedViews_CollectsInnerArraysAndViews()
    {
        var (_, outputCompilation, generatorDiagnostics) = GeneratorTestRunner.RunGenerator(
            SharedCompilation, "Cuda");

        Assert.Empty(generatorDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emitResult = outputCompilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var aotContexts = assembly.GetType(
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.LaunchAliasPlanTestKernels+AotContexts");
        Assert.NotNull(aotContexts);

        int[] first = [1, 2];
        int[] second = [3, 4, 5];

        AssertCollectedCount(
            assembly, aotContexts,
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.AliasPlanNullableNestedMemoryBag",
            "AliasPlanNullableNestedMemoryBag",
            "Views",
            null,
            expectedCount: 0);
        AssertCollectedCount(
            assembly, aotContexts,
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.AliasPlanNullableNestedMemoryBag",
            "AliasPlanNullableNestedMemoryBag",
            "Views",
            (Memory<Memory<int>>?)new Memory<int>[] { first.AsMemory(), second.AsMemory() }.AsMemory(),
            expectedCount: 2);
        AssertCollectedCount(
            assembly, aotContexts,
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.AliasPlanNullableNestedArrayBag",
            "AliasPlanNullableNestedArrayBag",
            "Arrays",
            (Memory<int[]>?)new[] { first, second }.AsMemory(),
            expectedCount: 2);
        AssertCollectedCount(
            assembly, aotContexts,
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.AliasPlanNullableNestedOptionalMemoryBag",
            "AliasPlanNullableNestedOptionalMemoryBag",
            "OptionalViews",
            (Memory<Memory<int>?>?)new Memory<int>?[] { first.AsMemory(), null, second.AsMemory() }.AsMemory(),
            expectedCount: 2);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void HostShadowStructs_Collect_UnwrapsMultiLevelNestedViewsAndSkipsUnconvertedElements()
    {
        INamedTypeSymbol GetType(string name)
        {
            var symbol = SharedCompilation.GetTypeByMetadataName(
                $"Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.{name}");
            Assert.NotNull(symbol);
            return symbol;
        }

        var twoDeep = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        HostShadowStructs.Collect(GetType("AliasPlanTwoDeepNestedViewFields"), twoDeep);
        Assert.Contains(GetType("AliasPlanDeepLeafBag"), twoDeep);

        var memoryDef = SharedCompilation.GetTypeByMetadataName("System.Memory`1");
        var nullableDef = SharedCompilation.GetSpecialType(SpecialType.System_Nullable_T);
        Assert.NotNull(memoryDef);
        var nullableMemInt64 = nullableDef.Construct(
            memoryDef.Construct(SharedCompilation.GetSpecialType(SpecialType.System_Int64)));

        var threeLevelOpt = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        HostShadowStructs.Collect(GetType("AliasPlanThreeLevelOptionalViewBag"), threeLevelOpt);
        Assert.Contains(nullableMemInt64, threeLevelOpt);

        var nullableDeep = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        HostShadowStructs.Collect(GetType("AliasPlanNullableDeepNestedViewBag"), nullableDeep);
        Assert.Contains(GetType("AliasPlanDeepLeafBag"), nullableDeep);

        var nullableThreeLevelOpt = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        HostShadowStructs.Collect(GetType("AliasPlanNullableThreeLevelOptionalViewBag"), nullableThreeLevelOpt);
        Assert.Contains(nullableMemInt64, nullableThreeLevelOpt);

        var extraCompilation = RoslynTestUtility.CreateCompilation("""
            using System;
            namespace ExtraCases;
            public struct IdenticalPoint { public int X; public int Y; }
            public struct NullableIntMemoryBag { public Memory<int?>? Values; }
            """);
        var pointType = extraCompilation.GetTypeByMetadataName("ExtraCases.IdenticalPoint");
        Assert.NotNull(pointType);
        var pointArrayCollected = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        HostShadowStructs.Collect(extraCompilation.CreateArrayTypeSymbol(pointType), pointArrayCollected);
        Assert.Empty(pointArrayCollected);

        var nullableIntMemBag = extraCompilation.GetTypeByMetadataName("ExtraCases.NullableIntMemoryBag");
        Assert.NotNull(nullableIntMemBag);
        var nullableIntCollected = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        HostShadowStructs.Collect(nullableIntMemBag, nullableIntCollected);
        var nullableInt32 = extraCompilation.GetSpecialType(SpecialType.System_Nullable_T)
            .Construct(extraCompilation.GetSpecialType(SpecialType.System_Int32));
        Assert.DoesNotContain(nullableInt32, nullableIntCollected);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cuda")]
    [InlineData("Cpu")]
    public void AliasHeldArrays_DeepNestedAndNullableViews_CollectsLeafViewsAndDisposesPins(string targetDialect)
    {
        var (_, outputCompilation, generatorDiagnostics) = GeneratorTestRunner.RunGenerator(
            SharedCompilation, targetDialect);

        Assert.Empty(generatorDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emitResult = outputCompilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var aotContexts = assembly.GetType(
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.LaunchAliasPlanTestKernels+AotContexts");
        Assert.NotNull(aotContexts);

        var leafType = assembly.GetType(
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.AliasPlanDeepLeafBag");
        Assert.NotNull(leafType);
        var memLeafType = typeof(Memory<>).MakeGenericType(leafType);
        var memMemLeafType = typeof(Memory<>).MakeGenericType(memLeafType);

        object CreateDeepLeafMemory(Memory<int> data)
        {
#pragma warning disable RS0030 // Reflection to instantiate a struct from an assembly compiled at run time
            var leaf = Activator.CreateInstance(leafType);
#pragma warning restore RS0030
            Assert.NotNull(leaf);
#pragma warning disable RS0030 // Reflection to inspect a field on a dynamically compiled struct
            var dataField = leafType.GetField("Data");
#pragma warning restore RS0030
            Assert.NotNull(dataField);
#pragma warning disable RS0030 // Reflection to set a field on a dynamically compiled struct
            dataField.SetValue(leaf, data);
#pragma warning restore RS0030
            var leafArray = Array.CreateInstance(leafType, 1);
            leafArray.SetValue(leaf, 0);
#pragma warning disable RS0030 // Reflection to construct Memory<T> over a dynamically compiled struct
            var innerMem = Activator.CreateInstance(memLeafType, leafArray);
#pragma warning restore RS0030
            Assert.NotNull(innerMem);
            var outerArray = Array.CreateInstance(memLeafType, 1);
            outerArray.SetValue(innerMem, 0);
#pragma warning disable RS0030 // Reflection to construct Memory<Memory<T>> over a dynamically compiled struct
            var outerMem = Activator.CreateInstance(memMemLeafType, outerArray);
#pragma warning restore RS0030
            Assert.NotNull(outerMem);
            return outerMem;
        }

        var pin1 = new PinTrackingMemoryManager<int>([1, 2]);
        var pin2 = new PinTrackingMemoryManager<int>([3, 4]);
        AssertCollectedCountWithFields(
            assembly, aotContexts,
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.AliasPlanTwoDeepNestedViewFields",
            "AliasPlanTwoDeepNestedViewFields",
            [("First", CreateDeepLeafMemory(pin1.Memory)), ("Second", CreateDeepLeafMemory(pin2.Memory))],
            expectedCount: 2,
            onCollected: () =>
            {
                Assert.Equal(1, pin1.ActivePins);
                Assert.Equal(1, pin2.ActivePins);
            });
        Assert.Equal(0, pin1.ActivePins);
        Assert.Equal(0, pin2.ActivePins);

        var pinNull = new PinTrackingMemoryManager<int>([5, 6]);
        AssertCollectedCount(
            assembly, aotContexts,
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.AliasPlanNullableDeepNestedViewBag",
            "AliasPlanNullableDeepNestedViewBag",
            "Views",
            null,
            expectedCount: 0);
        AssertCollectedCountWithFields(
            assembly, aotContexts,
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.AliasPlanNullableDeepNestedViewBag",
            "AliasPlanNullableDeepNestedViewBag",
            [("Views", CreateDeepLeafMemory(pinNull.Memory))],
            expectedCount: 1,
            onCollected: () => Assert.Equal(1, pinNull.ActivePins));
        Assert.Equal(0, pinNull.ActivePins);

        var longPin1 = new PinTrackingMemoryManager<long>([10L, 20L]);
        var longPin2 = new PinTrackingMemoryManager<long>([30L, 40L]);
        var threeLevelOptValue = new Memory<Memory<long>?>[]
        {
            new Memory<long>?[] { longPin1.Memory, null, longPin2.Memory }.AsMemory()
        }.AsMemory();
        AssertCollectedCountWithFields(
            assembly, aotContexts,
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.AliasPlanThreeLevelOptionalViewBag",
            "AliasPlanThreeLevelOptionalViewBag",
            [("OptionalViews", threeLevelOptValue)],
            expectedCount: 2,
            onCollected: () =>
            {
                Assert.Equal(1, longPin1.ActivePins);
                Assert.Equal(1, longPin2.ActivePins);
            });
        Assert.Equal(0, longPin1.ActivePins);
        Assert.Equal(0, longPin2.ActivePins);

        AssertCollectedCount(
            assembly, aotContexts,
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.AliasPlanNullableThreeLevelOptionalViewBag",
            "AliasPlanNullableThreeLevelOptionalViewBag",
            "OptionalViews",
            null,
            expectedCount: 0);
        AssertCollectedCountWithFields(
            assembly, aotContexts,
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.AliasPlanNullableThreeLevelOptionalViewBag",
            "AliasPlanNullableThreeLevelOptionalViewBag",
            [("OptionalViews", (Memory<Memory<Memory<long>?>>?)threeLevelOptValue)],
            expectedCount: 2,
            onCollected: () =>
            {
                Assert.Equal(1, longPin1.ActivePins);
                Assert.Equal(1, longPin2.ActivePins);
            });
        Assert.Equal(0, longPin1.ActivePins);
        Assert.Equal(0, longPin2.ActivePins);
    }

    private sealed class PinTrackingMemoryManager<T>(T[] backing) : MemoryManager<T>
    {
        public int ActivePins { get; private set; }

        public override Span<T> GetSpan() => backing;

        public override unsafe MemoryHandle Pin(int elementIndex = 0)
        {
            ActivePins++;
            var handle = GCHandle.Alloc(backing, GCHandleType.Pinned);
            var ptr = Unsafe.Add<T>((void*)handle.AddrOfPinnedObject(), elementIndex);
            return new MemoryHandle(ptr, handle, this);
        }

        public override void Unpin() => ActivePins--;

        protected override void Dispose(bool disposing)
        {
        }
    }

    private static void AssertCollectedCount(System.Reflection.Assembly assembly, Type aotContexts,
        string holderFullName, string methodSuffix, string fieldName, object? fieldValue, int expectedCount) =>
        AssertCollectedCountWithFields(
            assembly, aotContexts, holderFullName, methodSuffix, [(fieldName, fieldValue)], expectedCount);

    private static void AssertCollectedCountWithFields(System.Reflection.Assembly assembly, Type aotContexts,
        string holderFullName, string methodSuffix, (string Name, object? Value)[] fields, int expectedCount,
        Action? onCollected = null)
    {
        var holderType = assembly.GetType(holderFullName);
        Assert.NotNull(holderType);

#pragma warning disable RS0030 // Reflection to call an assembly compiled at run time
        var method = aotContexts.GetMethod($"AliasHeldArrays_{methodSuffix}");
#pragma warning restore RS0030
        Assert.NotNull(method);

#pragma warning disable RS0030 // Reflection to instantiate a struct from an assembly compiled at run time
        var instance = Activator.CreateInstance(holderType);
#pragma warning restore RS0030
        Assert.NotNull(instance);

        foreach (var (fieldName, fieldValue) in fields)
        {
#pragma warning disable RS0030 // Reflection to call an assembly compiled at run time
            var field = holderType.GetField(fieldName);
#pragma warning restore RS0030
            Assert.NotNull(field);
#pragma warning disable RS0030 // Reflection to set a field on a struct from an assembly compiled at run time
            field.SetValue(instance, fieldValue);
#pragma warning restore RS0030
        }

        var held = new HostAliasHeldArrays { CopiesBack = true };
        try
        {
#pragma warning disable RS0030 // Reflection to invoke generated AliasHeldArrays_ in the compiled assembly
            method.Invoke(null, [instance, held]);
#pragma warning restore RS0030
            Assert.Equal(expectedCount, held.Count);
            onCollected?.Invoke();
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void EmitAndCompile_NestedArrayAndViewCopyArrays_CompilesWithoutErrors()
    {
        var compilation = RoslynTestUtility.CreateCompilation(NestedCopyArraysSource);
        var (_, outputCompilation, generatorDiagnostics) = GeneratorTestRunner.RunGenerator(compilation, "Cpu");

        Assert.Empty(generatorDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
        var compileErrors = outputCompilation
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(compileErrors);
        Assert.NotNull(outputCompilation.GetTypeByMetadataName(
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.CpuCopyArrays.NestedCopyArraysKernels+AotContexts"));

        using var stream = new MemoryStream();
        var emitResult = outputCompilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CopyArrays_NestedViewsAndArrays_SharesGroupCopiesAndIsolatesCaller()
    {
        var compilation = RoslynTestUtility.CreateCompilation(NestedCopyArraysSource);
        var (_, outputCompilation, generatorDiagnostics) = GeneratorTestRunner.RunGenerator(compilation, "Cpu");
        Assert.Empty(generatorDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));

        using var stream = new MemoryStream();
        var emitResult = outputCompilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var aotContexts = assembly.GetType(
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.CpuCopyArrays.NestedCopyArraysKernels+AotContexts");
        Assert.NotNull(aotContexts);

        const string Ns = "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.CpuCopyArrays";

        static TField CopyAndShare<TField>(
            System.Reflection.Assembly asm,
            Type contexts,
            string structName,
            string fieldName,
            TField fieldValue,
            int[] backing,
            out Memory<int> sharedSlice)
        {
            var holderType = asm.GetType($"{Ns}.{structName}");
            Assert.NotNull(holderType);
#pragma warning disable RS0030 // Reflection to inspect generated AliasHeldArrays_ and CopyArrays_ methods
            var aliasMethod = contexts.GetMethod($"AliasHeldArrays_{structName}");
            var copyMethod = contexts.GetMethod($"CopyArrays_{structName}");
            var field = holderType.GetField(fieldName);
            var instance = Activator.CreateInstance(holderType);
#pragma warning restore RS0030
            Assert.NotNull(aliasMethod);
            Assert.NotNull(copyMethod);
            Assert.NotNull(field);
            Assert.NotNull(instance);

#pragma warning disable RS0030 // Reflection to set struct field on runtime-compiled type
            field.SetValue(instance, fieldValue);
#pragma warning restore RS0030

            var held = new HostAliasHeldArrays { CopiesBack = false };
            try
            {
                // Add an overlapping view representing the [In] Span<int> slice in the same group
                held.AddView<int>(backing.AsMemory(1, 2), writable: true, deviceAlignment: 4);
                object?[] aliasArgs = [instance, held];
#pragma warning disable RS0030 // Reflection to invoke generated AliasHeldArrays_ on runtime-compiled type
                aliasMethod.Invoke(null, aliasArgs);
#pragma warning restore RS0030
                held.Pin();

                var ranges = held.Combine(ReadOnlySpan<nint>.Empty);
                var traits = held.Combine(Array.Empty<byte>());
                var groups = HostAliasGroups.Find(ranges, traits, held.Combine(""));
                Assert.NotNull(groups);

                var copies = new HostArrayCopies();
                held.ShareCopies(groups, 0, ranges, copies);

                object?[] copyArgs = [aliasArgs[0], copies];
#pragma warning disable RS0030 // Reflection to invoke generated CopyArrays_ on runtime-compiled type
                copyMethod.Invoke(null, copyArgs);
#pragma warning restore RS0030

                sharedSlice = backing.AsMemory(1, 2);
                Assert.False(copies.CopyView(ref sharedSlice));

#pragma warning disable RS0030 // Reflection to read copied struct field on runtime-compiled type
                var copiedValue = field.GetValue(copyArgs[0]);
#pragma warning restore RS0030
                Assert.NotNull(copiedValue);
                return (TField)copiedValue;
            }
            finally
            {
                held.Free();
            }
        }

        // 1. Memory<int>[]
        {
            int[] backing = [1, 2, 3, 4];
            Memory<int>[] outer = [backing.AsMemory(0, 3)];
            var copied = CopyAndShare(assembly, aotContexts, "CopyArraysMemoryArrayBag", "Views", outer, backing,
                out var slice);
            Assert.NotSame(outer, copied);
            copied[0].Span[1] = 111;
            Assert.Equal(111, slice.Span[0]);
            Assert.Equal(2, backing[1]);
        }

        // 2. int[][]
        {
            int[] backing = [1, 2, 3, 4];
            int[][] outer = [backing];
            var copied = CopyAndShare(assembly, aotContexts, "CopyArraysJaggedArrayBag", "Arrays", outer, backing,
                out var slice);
            Assert.NotSame(outer, copied);
            Assert.NotSame(backing, copied[0]);
            copied[0][1] = 222;
            Assert.Equal(222, slice.Span[0]);
            Assert.Equal(2, backing[1]);
        }

        // 3. Memory<Memory<int>>
        {
            int[] backing = [1, 2, 3, 4];
            var inner = new[] { backing.AsMemory(0, 3) };
            Memory<Memory<int>> outer = inner.AsMemory();
            var copied = CopyAndShare(assembly, aotContexts, "CopyArraysNestedMemoryBag", "Views", outer, backing,
                out var slice);
            copied.Span[0].Span[1] = 333;
            Assert.Equal(333, slice.Span[0]);
            Assert.Equal(2, backing[1]);
            Assert.Equal(2, inner[0].Span[1]);
        }

        // 4. Memory<int>?[]
        {
            int[] backing = [1, 2, 3, 4];
            Memory<int>?[] outer = [backing.AsMemory(0, 3), null];
            var copied = CopyAndShare(assembly, aotContexts, "CopyArraysNullableMemoryArrayBag", "OptionalViews",
                outer, backing, out var slice);
            Assert.NotSame(outer, copied);
            Assert.True(copied[0].HasValue);
            Assert.False(copied[1].HasValue);
            copied[0].GetValueOrDefault().Span[1] = 444;
            Assert.Equal(444, slice.Span[0]);
            Assert.Equal(2, backing[1]);
        }

        // 5. ReadOnlyMemory<Memory<int>>
        {
            int[] backing = [1, 2, 3, 4];
            var callerInner = new[] { backing.AsMemory(0, 3) };
            ReadOnlyMemory<Memory<int>> outer = callerInner.AsMemory();
            var copied = CopyAndShare(assembly, aotContexts, "CopyArraysReadOnlyNestedMemoryBag", "Views",
                outer, backing, out var slice);
            copied.Span[0].Span[1] = 555;
            Assert.Equal(555, slice.Span[0]);
            Assert.Equal(2, backing[1]);
            Assert.Equal(2, callerInner[0].Span[1]);
            // Verify caller's outer array still holds its original Memory<int> pointing to backing
            callerInner[0].Span[1] = 2;
        }

        // 6. ImmutableArray<Memory<int>>
        {
            int[] backing = [1, 2, 3, 4];
            var callerInner = new[] { backing.AsMemory(0, 3) };
            var outer = ImmutableCollectionsMarshal.AsImmutableArray(callerInner);
            var copied = CopyAndShare(assembly, aotContexts, "CopyArraysImmutableNestedMemoryBag", "Views",
                outer, backing, out var slice);
            Assert.NotSame(callerInner, ImmutableCollectionsMarshal.AsArray(copied));
            copied[0].Span[1] = 666;
            Assert.Equal(666, slice.Span[0]);
            Assert.Equal(2, backing[1]);
            Assert.Equal(2, callerInner[0].Span[1]);
        }

        // 7. Memory<Memory<Memory<int>>>
        {
            int[] backing = [1, 2, 3, 4];
            var lvl2 = new[] { backing.AsMemory(0, 3) };
            var lvl1 = new[] { lvl2.AsMemory() };
            Memory<Memory<Memory<int>>> outer = lvl1.AsMemory();
            var copied = CopyAndShare(assembly, aotContexts, "CopyArraysThreeLevelMemoryBag", "Views",
                outer, backing, out var slice);
            copied.Span[0].Span[0].Span[1] = 777;
            Assert.Equal(777, slice.Span[0]);
            Assert.Equal(2, backing[1]);
            Assert.Equal(2, lvl2[0].Span[1]);
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void HoldsArray_JaggedArrays_DetectsNestedArraysAndHandlesNull()
    {
        var compilation = RoslynTestUtility.CreateCompilation(NestedCopyArraysSource);
        var (_, outputCompilation, generatorDiagnostics) = GeneratorTestRunner.RunGenerator(compilation, "Cpu");
        Assert.Empty(generatorDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));

        using var stream = new MemoryStream();
        var emitResult = outputCompilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var aotContexts = assembly.GetType(
            "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.CpuCopyArrays.NestedCopyArraysKernels+AotContexts");
        Assert.NotNull(aotContexts);

        const string Ns = "Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.CpuCopyArrays";
        var jaggedType = assembly.GetType($"{Ns}.CopyArraysJaggedArrayBag");
        var deepType = assembly.GetType($"{Ns}.CopyArraysJaggedDeepBag");
        var leafType = assembly.GetType($"{Ns}.CopyArraysArrayBag");
        Assert.NotNull(jaggedType);
        Assert.NotNull(deepType);
        Assert.NotNull(leafType);

#pragma warning disable RS0030 // Reflection to inspect generated HoldsArray_ methods on runtime-compiled type
        var holdsJagged = aotContexts.GetMethod("HoldsArray_CopyArraysJaggedArrayBag", [jaggedType, typeof(object)]);
        var holdsDeep = aotContexts.GetMethod("HoldsArray_CopyArraysJaggedDeepBag", [deepType, typeof(object)]);
        var arraysField = jaggedType.GetField("Arrays");
        var nestedField = deepType.GetField("Nested");
        var dataField = leafType.GetField("Data");
        var defaultJagged = Activator.CreateInstance(jaggedType);
        var defaultDeep = Activator.CreateInstance(deepType);
#pragma warning restore RS0030
        Assert.NotNull(holdsJagged);
        Assert.NotNull(holdsDeep);
        Assert.NotNull(arraysField);
        Assert.NotNull(nestedField);
        Assert.NotNull(dataField);
        Assert.NotNull(defaultJagged);
        Assert.NotNull(defaultDeep);

        int[] inner1 = [1, 2];
        int[] unrelated = [9];
        int[]?[] outerJagged = [null, inner1];
#pragma warning disable RS0030 // Reflection to invoke generated HoldsArray_ on runtime-compiled type
        Assert.False(Assert.IsType<bool>(holdsJagged.Invoke(null, [defaultJagged, inner1])));
        arraysField.SetValue(defaultJagged, outerJagged);
        Assert.True(Assert.IsType<bool>(holdsJagged.Invoke(null, [defaultJagged, outerJagged])));
        Assert.True(Assert.IsType<bool>(holdsJagged.Invoke(null, [defaultJagged, inner1])));
        Assert.False(Assert.IsType<bool>(holdsJagged.Invoke(null, [defaultJagged, unrelated])));

        Assert.False(Assert.IsType<bool>(holdsDeep.Invoke(null, [defaultDeep, inner1])));
        var leaf = Activator.CreateInstance(leafType);
        Assert.NotNull(leaf);
        dataField.SetValue(leaf, inner1);
        var innerLeafArray = Array.CreateInstance(leafType, 1);
        innerLeafArray.SetValue(leaf, 0);
        var outerDeepArray = Array.CreateInstance(leafType.MakeArrayType(), 2);
        outerDeepArray.SetValue(null, 0);
        outerDeepArray.SetValue(innerLeafArray, 1);
        nestedField.SetValue(defaultDeep, outerDeepArray);

        Assert.True(Assert.IsType<bool>(holdsDeep.Invoke(null, [defaultDeep, outerDeepArray])));
        Assert.True(Assert.IsType<bool>(holdsDeep.Invoke(null, [defaultDeep, innerLeafArray])));
        Assert.True(Assert.IsType<bool>(holdsDeep.Invoke(null, [defaultDeep, inner1])));
        Assert.False(Assert.IsType<bool>(holdsDeep.Invoke(null, [defaultDeep, unrelated])));
#pragma warning restore RS0030
    }
}
