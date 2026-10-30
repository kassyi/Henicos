#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Helpers;
using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Kassyi.GSharp.Tests.Shared.TestCases.Layout;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Layout;

// The transfer plan against the runtime itself and against the layout model it replaces: every struct of
// TestCases/Layout is planned under both dialects, the host size and offsets are compared with Unsafe.SizeOf and the
// offsets the CLR gives the fields, and the kinds, diagnostics and launch-time layout checks with the current ones
public class TransferPlannerTests
{
    private static readonly ITranspilerDialect[] s_dialects = [HostDialects.Cuda, HostDialects.Cpu];

    private static readonly string[] s_layoutFiles = ["HostLayoutCases.cs", "ExplicitUnionOrder.cs"];

    // Structs that the device cannot lay out, and the kernels that take them; Roslyn only, not compiled
    private static readonly string[] s_diagnosticFiles =
    [
        "StructLayoutDiagnostics.cs", "StructLayoutSizeDiagnostics.cs", "StructLayoutEventDiagnostics.cs"
    ];

    // The type arguments that each generic struct of the cases is planned with
    private static readonly Dictionary<string, Type[]> s_typeArguments = new()
    {
        ["GenericBox`1"] = [typeof(int), typeof(double), typeof(Float4)],
        ["SizedGeneric`1"] = [typeof(int), typeof(double)]
    };

    // ExplicitUnionOrder.cs is a snapshot case and is not compiled into the test assembly; its IntLongUnion is declared
    // exactly as the one in HostLayoutCases.cs, which stands in for it at run time
    private static readonly Dictionary<string, Type> s_runtimeStandIns = new()
    {
        ["ExplicitUnionOrderTest.IntLongUnion"] = typeof(IntLongUnion)
    };

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_EveryLayoutCaseStruct_MatchesTheRuntimeHostLayout()
    {
        var failures = new List<string>();
        var checkedOffsets = 0;

        foreach (var (symbol, runtimeType) in EnumerateLayoutCaseStructs())
        {
            foreach (var dialect in s_dialects)
            {
                var plan = TransferPlanner.Plan(symbol, dialect);
                var where = $"{symbol.ToDisplayString()} ({dialect.GetType().Name})";

                if (plan is TransferPlan.Unsupported unsupported)
                {
                    failures.Add($"{where}: unexpectedly unsupported: {unsupported.Reason}");
                    continue;
                }

                var hostIsRuntimeDecided = ContainsReferences(runtimeType) ||
                                           runtimeType.StructLayoutAttribute?.Value == LayoutKind.Auto;
                if (plan.HostSize is not { } hostSize)
                {
                    if (!hostIsRuntimeDecided)
                    {
                        failures.Add($"{where}: the host layout is unknown, but the CLR lays it out sequentially");
                    }

                    continue;
                }

                if (hostIsRuntimeDecided)
                {
                    failures.Add($"{where}: the plan predicts a host layout that the CLR decides at run time");
                    continue;
                }

                var runtimeSize = SizeOf(runtimeType);
                if (hostSize != runtimeSize)
                {
                    failures.Add($"{where}: host size {hostSize}, runtime {runtimeSize}");
                }

                foreach (var member in GetMembers(plan))
                {
                    var runtimeOffset = OffsetOf(runtimeType, member.Name);
                    checkedOffsets++;
                    if (member.HostOffset != runtimeOffset)
                    {
                        failures.Add($"{where}: field '{member.Name}' at host offset {member.HostOffset}, runtime {runtimeOffset}");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        // The check above is reached: the cases have well over a hundred fields with a predicted offset
        Assert.True(checkedOffsets > 100, $"only {checkedOffsets} offsets were compared");
    }

    // The plan of every user struct, and of each of its members down the tree, has the kind that the current layout
    // model gives the same type; an unsupported one carries the same diagnostic, field path and field type
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_UserStructs_HaveTheKindOfTheCurrentTransferClass()
    {
        var failures = new List<string>();
        var kinds = new HashSet<Type>();
        var compared = 0;
        foreach (var symbol in EnumerateLayoutCaseStructs().Select(static c => c.Symbol).Concat(EnumerateDiagnosticCaseStructs()))
        {
            Assert.True(TypeLayoutCalculator.IsUserStruct(symbol), symbol.ToDisplayString());
            foreach (var dialect in s_dialects)
            {
                var plan = TransferPlanner.Plan(symbol, dialect);
                kinds.Add(plan.GetType());
                compared += ComparePlan(plan, symbol, dialect, [], $"{symbol.ToDisplayString()} ({dialect.GetType().Name})", failures);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.True(compared > 300, $"only {compared} plans were compared");
        Assert.Contains(typeof(TransferPlan.Blit), kinds);
        Assert.Contains(typeof(TransferPlan.Convert), kinds);
        Assert.Contains(typeof(TransferPlan.Unsupported), kinds);
    }

    // The launch-time layout checks built from the plans are the ones KernelHostModel collects from the symbols
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void TransferPlanGuards_EqualTheLayoutGuardsOfKernelHostModel()
    {
        var launches = 0;
        var guardCount = 0;
        foreach (var method in EnumerateLaunchMethods())
        {
            var model = KernelHostModel.Create(method, default);
            foreach (var (dialect, expected) in new[] { (HostDialects.Cuda, model.CudaLayoutGuards), (HostDialects.Cpu, model.CpuLayoutGuards) })
            {
                var plans = method.Parameters.Select(p => TransferPlanner.Plan(p.Type, dialect)).ToList();
                if (!method.ReturnsVoid)
                {
                    plans.Add(TransferPlanner.Plan(method.ReturnType, dialect));
                }

                var actual = TransferPlanGuards.Collect([.. plans], method.ContainingAssembly.Name);
                Assert.True(expected == actual, $"{method.ToDisplayString()} ({dialect.GetType().Name}):{Environment.NewLine}expected {expected}{Environment.NewLine}actual   {actual}");
                launches++;
                guardCount += actual.Length;
            }
        }

        Assert.True(launches > 30, $"only {launches} launches were compared");
        Assert.True(guardCount > 40, $"only {guardCount} guards were compared");
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void TransferPlanGuards_NameOnlyTheFieldsALauncherCanName()
    {
        var symbol = GetLayoutCaseStruct(nameof(AccessMix));
        var plan = Assert.IsType<TransferPlan.Blit>(TransferPlanner.Plan(symbol, HostDialects.Cpu));
        var own = symbol.ContainingAssembly.Name;

        var guards = TransferPlanGuards.Collect([plan], own);
        Assert.Equal(new[] { "A", "B" }, Assert.Single(guards).Fields.Select(static f => f.Name));
        Assert.Equal(new[] { "A" }, Assert.Single(TransferPlanGuards.Collect([plan], "Other")).Fields.Select(static f => f.Name));

        // _c is private, P a pointer, D an auto-property: their offsets are left to the size check
        Assert.Equal(5, plan.Members.Length);
        Assert.All(
            plan.Members.Where(static m => m.Name is "_c" or "P" or "D"),
            static m => Assert.Equal(MemberAccess.NotNameable, m.Access));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Plan_VectorStruct_IsBlitOnTheCpuAndConvertOnCuda()
    {
        var symbol = GetLayoutCaseStruct(nameof(SystemNumericsVectorsStruct));

        var cpu = Assert.IsType<TransferPlan.Blit>(TransferPlanner.Plan(symbol, HostDialects.Cpu));
        var cuda = Assert.IsType<TransferPlan.Convert>(TransferPlanner.Plan(symbol, HostDialects.Cuda));

        Assert.Equal(Unsafe.SizeOf<SystemNumericsVectorsStruct>(), cpu.Size);
        Assert.Equal(Unsafe.SizeOf<SystemNumericsVectorsStruct>(), cuda.HostSize);
        var v2 = cuda.Members.First(static m => m.Name == nameof(SystemNumericsVectorsStruct.V2));
        Assert.Equal(4, v2.HostOffset);
        Assert.Equal(8, v2.DeviceOffset);
        Assert.IsType<TransferPlan.Blit>(v2.Plan);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ArrayFieldAndNullableMembers_HaveTheirOwnPlans()
    {
        var cuda = HostDialects.Cuda;

        var arrays = Assert.IsType<TransferPlan.Convert>(TransferPlanner.Plan(GetLayoutCaseStruct(nameof(DirectArrayFieldStruct)), cuda));
        Assert.Null(arrays.HostSize);
        var values = arrays.Members.First(static m => m.Name == nameof(DirectArrayFieldStruct.Values));
        Assert.Null(values.HostOffset);
        var view = Assert.IsType<TransferPlan.ArrayView>(values.Plan);
        Assert.Equal(TransferPlan.ArrayView.HostProxySize, view.HostSize);
        Assert.Equal(cuda.ArrayViewLayout.Size, view.DeviceSize);
        // A leaf has an empty member list, not a default one, so it equals a plan written with []
        Assert.Equal(new TransferPlan.Blit(4, "global::System.Int32", null, ImmutableArray<MemberPlan>.Empty.AsEquatableArray()), view.Element);

        var nullables = TransferPlanner.Plan(GetLayoutCaseStruct(nameof(NullableFieldsStruct)), cuda);
        var members = Assert.IsType<TransferPlan.Convert>(nullables).Members;
        var float2 = members.First(static m => m.Name == nameof(NullableFieldsStruct.NullableFloat2));
        var nullable = Assert.IsType<TransferPlan.Nullable>(float2.Plan);
        // Float2 is 4-aligned on the host (12 bytes in all) and 8-aligned on CUDA (16 bytes, the value at 8)
        Assert.Equal(Unsafe.SizeOf<Float2?>(), nullable.HostSize);
        Assert.Equal(16, nullable.DeviceSize);
        Assert.Equal(8, nullable.ValueDeviceOffset);
    }

    // A struct with an array of its own type is converted, as the layout model says, and not reported
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Plan_StructWithArrayOfItself_RefersBackToTheEnclosingPlan()
    {
        var symbol = GetLayoutCaseStruct(nameof(TreeNode));
        foreach (var dialect in s_dialects)
        {
            Assert.Equal(TransferClass.Convert, TypeLayoutCalculator.GetStructLayout(symbol, dialect).Transfer);

            var plan = Assert.IsType<TransferPlan.Convert>(TransferPlanner.Plan(symbol, dialect));
            var children = plan.Members.First(static m => m.Name == nameof(TreeNode.Children));
            var reference = Assert.IsType<TransferPlan.StructRef>(Assert.IsType<TransferPlan.ArrayView>(children.Plan).Element);
            Assert.Equal(plan.CSharpType, reference.CSharpType);
            Assert.Equal(plan.ShadowPath, reference.ShadowPath);
            Assert.Equal(plan.DeviceSize, reference.DeviceSize);
        }
    }

    // A struct outside the user structs that has no device representation is GSH0078 with no field to name
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Plan_DecimalOutsideAStruct_IsGsh0078WithoutAField()
    {
        var (compilation, _, _) = GetContext(Path.Combine("Layout", "HostLayoutCases.cs"));
        var plan = Assert.IsType<TransferPlan.Unsupported>(
            TransferPlanner.Plan(compilation.GetSpecialType(SpecialType.System_Decimal), HostDialects.Cuda));

        Assert.Equal("GSH0078", plan.DiagnosticId);
        Assert.Equal("'decimal' has no device representation", plan.Reason);
        Assert.Null(plan.FieldPath);
        Assert.Null(plan.FieldTypeName);
    }

    // The planner keeps no symbol: two compilations of the same source give equal plans, as the generator cache needs
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_SameSourceInTwoCompilations_GivesEqualPlans()
    {
        var source = RoslynTestUtility.ReadTestCase(Path.Combine("Layout", "HostLayoutCases.cs"));
        var first = RoslynTestUtility.CreateCompilation(source);
        var second = RoslynTestUtility.CreateCompilation(source);
        string[] names =
        [
            nameof(SimpleSequential), nameof(SystemNumericsVectorsStruct), nameof(DirectArrayFieldStruct),
            nameof(NullableFieldsStruct), nameof(StructWithDivergentInlineArray), nameof(TreeNode), nameof(AccessMix)
        ];

        foreach (var name in names)
        {
            var metadataName = $"Kassyi.GSharp.Tests.Shared.TestCases.Layout.{name}";
            var a = first.GetTypeByMetadataName(metadataName) ?? throw new InvalidOperationException(metadataName);
            var b = second.GetTypeByMetadataName(metadataName) ?? throw new InvalidOperationException(metadataName);
            Assert.NotSame(a, b);
            foreach (var dialect in s_dialects)
            {
                var planA = TransferPlanner.Plan(a, dialect);
                var planB = TransferPlanner.Plan(b, dialect);
                Assert.Equal(planA, planB);
                Assert.Equal(planA.GetHashCode(), planB.GetHashCode());
            }
        }
    }

    // The host leaf table is the CLR's: the size is Unsafe.SizeOf, the alignment the offset of the value after one byte
    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(typeof(bool))]
    [InlineData(typeof(byte))]
    [InlineData(typeof(sbyte))]
    [InlineData(typeof(char))]
    [InlineData(typeof(short))]
    [InlineData(typeof(ushort))]
    [InlineData(typeof(int))]
    [InlineData(typeof(uint))]
    [InlineData(typeof(long))]
    [InlineData(typeof(ulong))]
    [InlineData(typeof(float))]
    [InlineData(typeof(double))]
    [InlineData(typeof(Vector2))]
    [InlineData(typeof(Vector3))]
    [InlineData(typeof(Vector4))]
    public void HostLeafLayouts_MatchTheRuntime(Type type)
    {
        var (compilation, _, _) = GetContext(Path.Combine("Layout", "HostLayoutCases.cs"));
        var symbol = compilation.GetTypeByMetadataName(type.FullName ?? type.Name) ??
                     throw new InvalidOperationException(type.FullName);

        Assert.True(HostLeafLayouts.TryGet(symbol, out var layout));
        Assert.Equal(SizeOf(type), layout.Size);
        Assert.Equal(OffsetOf(typeof(AlignmentProbe<>).MakeGenericType(type), nameof(AlignmentProbe<int>.Value)),
            layout.Alignment);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void HostLeafLayouts_StructsAndDeviceVectors_AreNotLeaves()
    {
        var (compilation, _, _) = GetContext(Path.Combine("Layout", "HostLayoutCases.cs"));
        foreach (var name in new[] { "Kassyi.GSharp.Core.Float2", "System.Decimal", "System.IntPtr" })
        {
            var symbol = compilation.GetTypeByMetadataName(name) ?? throw new InvalidOperationException(name);
            Assert.False(HostLeafLayouts.TryGet(symbol, out _), name);
        }

        Assert.Equal(new TypeLayout(IntPtr.Size, IntPtr.Size), HostLeafLayouts.Pointer);
    }

    // Compares plan with the layout model's answer for type, member by member; returns the number of plans compared
    private static int ComparePlan(TransferPlan plan, ITypeSymbol type, ITranspilerDialect dialect,
        ImmutableHashSet<string> ancestors, string where, List<string> failures)
    {
        type = type.UnwrapNullableArrayLike() ?? type;
        var compared = 1;

        if (type.TryGetArrayLikeElementType(out var element))
        {
            if (plan is TransferPlan.ArrayView view)
            {
                return compared + ComparePlan(view.Element, element, dialect, ancestors, $"{where}[]", failures);
            }

            failures.Add($"{where}: {plan.GetType().Name}, expected ArrayView");
            return compared;
        }

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableType)
        {
            if (plan is TransferPlan.Nullable nullable)
            {
                return compared + ComparePlan(nullable.Value, nullableType.TypeArguments[0], dialect, ancestors, $"{where}?", failures);
            }

            if (plan is not TransferPlan.Unsupported)
            {
                failures.Add($"{where}: {plan.GetType().Name}, expected Nullable or Unsupported");
            }

            return compared;
        }

        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } structType ||
            type.TypeKind == TypeKind.Enum || dialect.TryGetTypeLayout(type, out _))
        {
            if (plan is not TransferPlan.Blit { Members.Length: 0 })
            {
                failures.Add($"{where}: {plan.GetType().Name}, expected a leaf Blit");
            }

            return compared;
        }

        var csharpType = structType.GetFullyQualifiedCsharpName();
        if (ancestors.Contains(csharpType))
        {
            if (plan is not TransferPlan.StructRef reference || reference.CSharpType != csharpType)
            {
                failures.Add($"{where}: {plan.GetType().Name}, expected StructRef");
            }

            return compared;
        }

        var layout = TypeLayoutCalculator.GetStructLayout(structType, dialect);
        if (layout.Unsupported is { } unsupported)
        {
            var expectedId = unsupported.IsMissingDeviceType ? "GSH0079" : "GSH0078";
            var expectedTypeName = unsupported.FieldType is { } fieldType ? TypeLayoutCalculator.FormatTypeName(fieldType) : null;
            if (plan is not TransferPlan.Unsupported actual ||
                actual.DiagnosticId != expectedId ||
                actual.Reason != unsupported.Reason ||
                actual.FieldPath != unsupported.FieldPath ||
                actual.FieldTypeName != expectedTypeName)
            {
                failures.Add($"{where}: {plan}, expected Unsupported({expectedId}, {unsupported.Reason}, {unsupported.FieldPath}, {expectedTypeName})");
            }

            return compared;
        }

        EquatableArray<MemberPlan> members;
        switch (plan)
        {
            case TransferPlan.Blit blit when layout.Transfer == TransferClass.Identical:
                members = blit.Members;
                break;
            case TransferPlan.Convert convert when layout.Transfer == TransferClass.Convert:
                members = convert.Members;
                break;
            default:
                failures.Add($"{where}: {plan.GetType().Name}, expected the kind of {layout.Transfer}");
                return compared;
        }

        if (members.Length != layout.DeviceFields.Length)
        {
            failures.Add($"{where}: {members.Length} member plans for {layout.DeviceFields.Length} fields");
            return compared;
        }

        var inner = ancestors.Add(csharpType);
        for (var i = 0; i < members.Length; i++)
        {
            var field = layout.DeviceFields[i];
            if (members[i].Name != field.Member.Name || members[i].DeviceOffset != field.Offset)
            {
                failures.Add($"{where}: member {i} is {members[i].Name} at {members[i].DeviceOffset}, expected {field.Member.Name} at {field.Offset}");
            }

            compared += ComparePlan(members[i].Plan, field.Member.Type, dialect, inner, $"{where}.{field.Member.Name}", failures);
        }

        return compared;
    }

    private static (Compilation Compilation, SyntaxTree Tree, SemanticModel Model) GetContext(string relativePath) =>
        RoslynTestUtility.GetTestCaseContext(relativePath);

    private static INamedTypeSymbol GetLayoutCaseStruct(string name) =>
        EnumerateLayoutCaseStructs().First(c => c.Symbol.Name == name).Symbol;

    // Every struct declared in TestCases/Layout, a generic one constructed with each of its type arguments, with the
    // runtime type it is compiled to
    private static IEnumerable<(INamedTypeSymbol Symbol, Type RuntimeType)> EnumerateLayoutCaseStructs()
    {
        var assembly = typeof(SimpleSequential).Assembly;
        foreach (var file in s_layoutFiles)
        {
            var (compilation, tree, model) = GetContext(Path.Combine("Layout", file));
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<StructDeclarationSyntax>())
            {
                var symbol = model.GetDeclaredSymbol(declaration) ?? throw new InvalidOperationException(declaration.Identifier.Text);
                var metadataName = $"{symbol.ContainingNamespace.ToDisplayString()}.{symbol.MetadataName}";
                var runtimeType = s_runtimeStandIns.TryGetValue(metadataName, out var standIn)
                    ? standIn
                    : assembly.GetType(metadataName) ?? throw new InvalidOperationException($"{metadataName} is not compiled into the test assembly");

                if (!symbol.IsGenericType)
                {
                    yield return (symbol, runtimeType);
                    continue;
                }

                if (!s_typeArguments.TryGetValue(symbol.MetadataName, out var arguments))
                {
                    throw new InvalidOperationException($"Add type arguments for {metadataName}");
                }

                foreach (var argument in arguments)
                {
                    var argumentSymbol = compilation.GetTypeByMetadataName(argument.FullName ?? argument.Name) ??
                                         throw new InvalidOperationException(argument.FullName);
                    yield return (symbol.Construct(argumentSymbol), runtimeType.MakeGenericType(argument));
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateDiagnosticCaseStructs() =>
        s_diagnosticFiles
            .SelectMany(static file =>
            {
                var (_, tree, model) = GetContext(Path.Combine("Diagnostics", file));
                return tree.GetRoot().DescendantNodes()
                    .OfType<StructDeclarationSyntax>()
                    .Select(d => model.GetDeclaredSymbol(d) ?? throw new InvalidOperationException(d.Identifier.Text));
            })
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default);

    // The launch probes of HostLayoutCases.cs and the kernels of the diagnostic cases
    private static IEnumerable<IMethodSymbol> EnumerateLaunchMethods()
    {
        var (compilation, _, _) = GetContext(Path.Combine("Layout", "HostLayoutCases.cs"));
        var probes = compilation.GetTypeByMetadataName($"Kassyi.GSharp.Tests.Shared.TestCases.Layout.{nameof(TransferPlanProbes)}") ??
                     throw new InvalidOperationException(nameof(TransferPlanProbes));
        foreach (var method in probes.GetMembers().OfType<IMethodSymbol>().Where(static m => m.MethodKind == MethodKind.Ordinary))
        {
            yield return method;
        }

        foreach (var file in s_diagnosticFiles)
        {
            var (_, tree, model) = GetContext(Path.Combine("Diagnostics", file));
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is { } method &&
                    method.GetAttributes().Any(static a => a.AttributeClass?.Name == "GpuKernelAttribute"))
                {
                    yield return method;
                }
            }
        }
    }

    // The host offsets that a plan predicts: the members of a block-copied or converted struct
    private static IEnumerable<MemberPlan> GetMembers(TransferPlan plan) => plan switch
    {
        TransferPlan.Blit blit => blit.Members,
        TransferPlan.Convert convert => convert.Members,
        _ => []
    };

    private static bool ContainsReferences(Type type) =>
        (bool)(typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.IsReferenceOrContainsReferences))
                   ?.MakeGenericMethod(type).Invoke(null, null) ??
               throw new InvalidOperationException("RuntimeHelpers.IsReferenceOrContainsReferences"));

    private static int SizeOf(Type type) =>
        (int)(typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))?.MakeGenericMethod(type).Invoke(null, null) ??
              throw new InvalidOperationException("Unsafe.SizeOf"));

    // The offset the CLR gives a field: fill a boxed instance with 0xFF, write the field's default (all zero bytes)
    // through reflection, and find the first zero byte
    private static int OffsetOf(Type structType, string memberName)
    {
        var field = FindField(structType, memberName);
        var box = RuntimeHelpers.GetUninitializedObject(structType);
        var bytes = MemoryMarshal.CreateSpan(ref Unsafe.As<RawBox>(box).Data, SizeOf(structType));
        bytes.Fill(0xFF);
        field.SetValue(box, DefaultOf(field.FieldType));
        return bytes.IndexOf((byte)0);
    }

    private static FieldInfo FindField(Type structType, string memberName)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var name = memberName.TrimStart('@');
        return structType.GetField(name, Flags) ??
               structType.GetField($"<{name}>k__BackingField", Flags) ??
               throw new InvalidOperationException($"{structType}.{name} has no field");
    }

    private static unsafe object? DefaultOf(Type type) =>
        type.IsPointer ? Pointer.Box(null, type) : type.IsValueType ? Activator.CreateInstance(type) : null;

    // The data of a boxed value starts where the first field of any class does
    private sealed class RawBox
    {
        public byte Data;
    }

    private struct AlignmentProbe<T>
    {
#pragma warning disable CS0649 // Only the layout is read
        public byte Head;
        public T Value;
#pragma warning restore CS0649
    }
}
