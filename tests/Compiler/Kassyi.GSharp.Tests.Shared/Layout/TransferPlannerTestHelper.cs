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

internal static class TransferPlannerTestHelper
{
    internal static readonly ITranspilerDialect[] s_dialects = [HostDialects.Cuda, HostDialects.Cpu];

    internal static readonly string[] s_layoutFiles = ["HostLayoutCases.cs", "ExplicitUnionOrder.cs"];

    // Structs that the device cannot lay out, and the kernels that take them; Roslyn only, not compiled
    internal static readonly string[] s_diagnosticFiles =
    [
        "StructLayoutDiagnostics.cs", "StructLayoutSizeDiagnostics.cs", "StructLayoutEventDiagnostics.cs"
    ];

    // Structs whose plan is decided by a member's Nullable, array or inline array plan; Roslyn only, not compiled
    internal static readonly string s_transferPlanCases = Path.Combine("Layout", "TransferPlanCases.cs");

    // The type arguments that each generic struct of the cases is planned with
    internal static readonly Dictionary<string, Type[]> s_typeArguments = new()
    {
        ["GenericBox`1"] = [typeof(int), typeof(double), typeof(Float4)],
        ["SizedGeneric`1"] = [typeof(int), typeof(double)]
    };

    // ExplicitUnionOrder.cs is a snapshot case and is not compiled into the test assembly; its IntLongUnion is declared
    // exactly as the one in HostLayoutCases.cs, which stands in for it at run time
    internal static readonly Dictionary<string, Type> s_runtimeStandIns = new()
    {
        ["ExplicitUnionOrderTest.IntLongUnion"] = typeof(IntLongUnion)
    };

    // Compares plan with the layout model's answer for type, member by member; returns the number of plans compared
    internal static int ComparePlan(TransferPlan plan, ITypeSymbol type, ITranspilerDialect dialect,
        ImmutableHashSet<string> ancestors, string where, List<string> failures)
    {
        type = type.UnwrapNullableArrayLike() ?? type;
        if (type.TryGetArrayLikeElementType(out var element))
        {
            return CompareArrayPlan(plan, element, dialect, ancestors, where, failures);
        }

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableType)
        {
            return CompareNullablePlan(plan, nullableType, dialect, ancestors, where, failures);
        }

        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } structType ||
            type.TypeKind == TypeKind.Enum || dialect.TryGetTypeLayout(type, out _))
        {
            if (plan is not TransferPlan.Blit { Members.Length: 0 })
            {
                failures.Add($"{where}: {plan.GetType().Name}, expected a leaf Blit");
            }

            return 1;
        }

        return CompareStructPlan(plan, structType, dialect, ancestors, where, failures);
    }

    // An array of an unsupported element is the element's plan itself
    private static int CompareArrayPlan(TransferPlan plan, ITypeSymbol element, ITranspilerDialect dialect,
        ImmutableHashSet<string> ancestors, string where, List<string> failures)
    {
        switch (plan)
        {
            case TransferPlan.ArrayView view:
                return 1 + ComparePlan(view.Element, element, dialect, ancestors, $"{where}[]", failures);
            case TransferPlan.Unsupported when plan == TransferPlanner.Plan(element, dialect):
                return 1;
            default:
                failures.Add($"{where}: {plan}, expected ArrayView or the element's Unsupported");
                return 1;
        }
    }

    // Unsupported when the value is; a value that holds a managed reference converts through GetValueOrDefault (#972)
    private static int CompareNullablePlan(TransferPlan plan, INamedTypeSymbol nullableType,
        ITranspilerDialect dialect, ImmutableHashSet<string> ancestors, string where, List<string> failures)
    {
        var valueType = nullableType.TypeArguments[0];
        var valuePlan = TransferPlanner.Plan(valueType, dialect);
        switch (plan)
        {
            case TransferPlan.Nullable nullable when valuePlan is not TransferPlan.Unsupported:
                return 1 + ComparePlan(nullable.Value, valueType, dialect, ancestors, $"{where}?", failures);
            case TransferPlan.Unsupported when plan == valuePlan:
                return 1;
            default:
                failures.Add($"{where}: {plan}, expected Nullable or Unsupported");
                return 1;
        }
    }

    private static int CompareStructPlan(TransferPlan plan, INamedTypeSymbol structType, ITranspilerDialect dialect,
        ImmutableHashSet<string> ancestors, string where, List<string> failures)
    {
        var csharpType = structType.GetFullyQualifiedCsharpName();
        if (ancestors.Contains(csharpType))
        {
            if (plan is not TransferPlan.StructRef reference || reference.CSharpType != csharpType)
            {
                failures.Add($"{where}: {plan.GetType().Name}, expected StructRef");
            }

            return 1;
        }

        var layout = TypeLayoutCalculator.GetStructLayout(structType, dialect);
        if (layout.Unsupported is { } unsupported)
        {
            CompareUnsupportedLayout(plan, unsupported, where, failures);
            return 1;
        }

        if (TryCompareUnsupportedMember(plan, layout, dialect, where, failures))
        {
            return 1;
        }

        return 1 + CompareMemberPlans(plan, layout, dialect, ancestors.Add(csharpType), where, failures);
    }

    private static void CompareUnsupportedLayout(TransferPlan plan, UnsupportedLayout unsupported, string where,
        List<string> failures)
    {
        var expectedId = unsupported.IsMissingDeviceType ? "GSH0079" : "GSH0078";
        var expectedTypeName = unsupported.FieldType is { } fieldType
            ? TypeLayoutCalculator.FormatTypeName(fieldType)
            : null;
        if (plan is not TransferPlan.Unsupported actual ||
            actual.DiagnosticId != expectedId ||
            actual.Reason != unsupported.Reason ||
            actual.FieldPath != unsupported.FieldPath ||
            actual.FieldTypeName != expectedTypeName)
        {
            failures.Add($"{where}: {plan}, expected Unsupported({expectedId}, {unsupported.Reason}, " +
                $"{unsupported.FieldPath}, {expectedTypeName})");
        }
    }

    // The layout model accepts the struct, but a member's own plan (a Nullable, an array) is unsupported: the struct is
    // unsupported with that member's reason. Returns whether such a member exists (and was compared).
    private static bool TryCompareUnsupportedMember(TransferPlan plan, StructLayoutInfo layout,
        ITranspilerDialect dialect, string where, List<string> failures)
    {
        var unsupportedMember = layout.DeviceFields
            .Select(f => (f.Member, Plan: TransferPlanner.Plan(f.Member.Type, dialect)))
            .FirstOrDefault(static m => m.Plan is TransferPlan.Unsupported);
        if (unsupportedMember.Plan is not TransferPlan.Unsupported memberProblem)
        {
            return false;
        }

        var expectedPath = memberProblem.FieldTypeName is null ? null
            : memberProblem.FieldPath is null ? unsupportedMember.Member.Name
            : unsupportedMember.Member.Kind == StructAbiMemberKind.InlineArrayElement ? memberProblem.FieldPath
            : $"{unsupportedMember.Member.Name}.{memberProblem.FieldPath}";
        if (plan is not TransferPlan.Unsupported actual ||
            actual.DiagnosticId != (expectedPath is null ? "GSH0078" : "GSH0079") ||
            !actual.Reason.EndsWith($": {memberProblem.Reason}", StringComparison.Ordinal) ||
            actual.FieldPath != expectedPath ||
            actual.FieldTypeName != memberProblem.FieldTypeName)
        {
            failures.Add(
                $"{where}: {plan}, expected the Unsupported of member '{unsupportedMember.Member.Name}' " +
                $"({memberProblem})");
        }

        return true;
    }

    // The member plans of a Blit or Convert against the device fields of the layout; returns the plans compared below
    private static int CompareMemberPlans(TransferPlan plan, StructLayoutInfo layout, ITranspilerDialect dialect,
        ImmutableHashSet<string> inner, string where, List<string> failures)
    {
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
                return 0;
        }

        if (members.Length != layout.DeviceFields.Length)
        {
            failures.Add($"{where}: {members.Length} member plans for {layout.DeviceFields.Length} fields");
            return 0;
        }

        var compared = 0;
        for (var i = 0; i < members.Length; i++)
        {
            var field = layout.DeviceFields[i];
            if (members[i].Name != field.Member.Name || members[i].DeviceOffset != field.Offset)
            {
                failures.Add(
                    $"{where}: member {i} is {members[i].Name} at {members[i].DeviceOffset}, " +
                    $"expected {field.Member.Name} at {field.Offset}");
            }

            compared += ComparePlan(
                members[i].Plan, field.Member.Type, dialect, inner, $"{where}.{field.Member.Name}", failures);
        }

        return compared;
    }

    internal static (Compilation Compilation, SyntaxTree Tree, SemanticModel Model) GetContext(string relativePath) =>
        RoslynTestUtility.GetTestCaseContext(relativePath);

    internal static INamedTypeSymbol GetTransferPlanCase(string name) =>
        GetTransferPlanCase(GetContext(s_transferPlanCases).Compilation, name);

    internal static INamedTypeSymbol GetTransferPlanCase(Compilation compilation, string name) =>
        compilation.GetTypeByMetadataName($"TransferPlanCasesTest.{name}") ??
        throw new InvalidOperationException(name);

    internal static INamedTypeSymbol MakeNullable(Compilation compilation, ITypeSymbol value) =>
        compilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(value);

    internal static INamedTypeSymbol GetLayoutCaseStruct(string name) =>
        EnumerateLayoutCaseStructs().First(c => c.Symbol.Name == name).Symbol;

    // Every struct declared in TestCases/Layout, a generic one constructed with each of its type arguments, with the
    // runtime type it is compiled to
    internal static IEnumerable<(INamedTypeSymbol Symbol, Type RuntimeType)> EnumerateLayoutCaseStructs()
    {
        var assembly = typeof(SimpleSequential).Assembly;
        foreach (var file in s_layoutFiles)
        {
            var (compilation, tree, model) = GetContext(Path.Combine("Layout", file));
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<StructDeclarationSyntax>())
            {
                var symbol = model.GetDeclaredSymbol(declaration) ??
                             throw new InvalidOperationException(declaration.Identifier.Text);
                var runtimeType = FindRuntimeType(compilation, symbol, assembly);

                if (!symbol.IsGenericType)
                {
                    yield return (symbol, runtimeType);
                    continue;
                }

                if (!s_typeArguments.TryGetValue(symbol.MetadataName, out var arguments))
                {
                    throw new InvalidOperationException($"Add type arguments for {symbol.MetadataName}");
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

    // The runtime type that symbol is compiled to: its stand-in, or the type of the test assembly whose full name
    // resolves back to symbol in the compilation of its case file
    private static Type FindRuntimeType(Compilation compilation, INamedTypeSymbol symbol, Assembly assembly)
    {
        foreach (var (metadataName, standIn) in s_runtimeStandIns)
        {
            if (SymbolEqualityComparer.Default.Equals(compilation.GetTypeByMetadataName(metadataName), symbol))
            {
                return standIn;
            }
        }

        var runtimeType = assembly.GetType(GetMetadataName(symbol));
        return runtimeType?.FullName is { } fullName &&
               SymbolEqualityComparer.Default.Equals(compilation.GetTypeByMetadataName(fullName), symbol)
            ? runtimeType
            : throw new InvalidOperationException($"{symbol.MetadataName} is not compiled into the test assembly");
    }

    // Namespace.Outer+Inner`1, as Assembly.GetType and Compilation.GetTypeByMetadataName spell a type
    private static string GetMetadataName(INamedTypeSymbol symbol)
    {
        if (symbol.ContainingType is { } containingType)
        {
            return GetMetadataName(containingType) + "+" + symbol.MetadataName;
        }

        var name = symbol.MetadataName;
        for (var ns = symbol.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
        {
            name = ns.Name + "." + name;
        }

        return name;
    }

    internal static IEnumerable<INamedTypeSymbol> EnumerateUnsupportedCaseStructs() =>
        s_diagnosticFiles
            .Select(static file => Path.Combine("Diagnostics", file))
            .Append(s_transferPlanCases)
            .SelectMany(static file =>
            {
                var (_, tree, model) = GetContext(file);
                return tree.GetRoot().DescendantNodes()
                    .OfType<StructDeclarationSyntax>()
                    .Select(d => model.GetDeclaredSymbol(d) ?? throw new InvalidOperationException(d.Identifier.Text));
            })
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default);

    // The launch probes of HostLayoutCases.cs and the kernels of the diagnostic cases
    internal static IEnumerable<IMethodSymbol> EnumerateLaunchMethods()
    {
        var (compilation, _, _) = GetContext(Path.Combine("Layout", "HostLayoutCases.cs"));
        var probes = compilation.GetTypeByMetadataName(
            $"Kassyi.GSharp.Tests.Shared.TestCases.Layout.{nameof(TransferPlanProbes)}") ??
            throw new InvalidOperationException(nameof(TransferPlanProbes));
        foreach (var method in probes.GetMembers().OfType<IMethodSymbol>().Where(
            static m => m.MethodKind == MethodKind.Ordinary))
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
    internal static IEnumerable<MemberPlan> GetMembers(TransferPlan plan) => plan switch
    {
        TransferPlan.Blit blit => blit.Members,
        TransferPlan.Convert convert => convert.Members,
        _ => []
    };

    internal static bool ContainsReferences(Type type) =>
#pragma warning disable RS0030 // The CLR is the oracle: whether the runtime type holds references
        (bool)(typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.IsReferenceOrContainsReferences))
                   ?.MakeGenericMethod(type).Invoke(null, null) ??
               throw new InvalidOperationException("RuntimeHelpers.IsReferenceOrContainsReferences"));
#pragma warning restore RS0030

    internal static int SizeOf(Type type) =>
#pragma warning disable RS0030 // The CLR is the oracle: the size the planned size must match
        (int)(typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))?.MakeGenericMethod(type).Invoke(null, null) ??
              throw new InvalidOperationException("Unsafe.SizeOf"));
#pragma warning restore RS0030

    // The offset the CLR gives a field: fill a boxed instance with 0xFF, write the field's default (all zero bytes)
    // through reflection, and find the first zero byte
    internal static int OffsetOf(Type structType, string memberName)
    {
        var field = FindField(structType, memberName);
        var box = RuntimeHelpers.GetUninitializedObject(structType);
        var bytes = MemoryMarshal.CreateSpan(ref Unsafe.As<RawBox>(box).Data, SizeOf(structType));
        bytes.Fill(0xFF);
#pragma warning disable RS0030 // The CLR is the oracle: writing the field shows the offset it gave it
        field.SetValue(box, DefaultOf(field.FieldType));
#pragma warning restore RS0030
        return bytes.IndexOf((byte)0);
    }

    internal static FieldInfo FindField(Type structType, string memberName)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var name = memberName.TrimStart('@');
        return structType.GetField(name, Flags) ??
               structType.GetField($"<{name}>k__BackingField", Flags) ??
               throw new InvalidOperationException($"{structType}.{name} has no field");
    }

    internal static unsafe object? DefaultOf(Type type) =>
#pragma warning disable RS0030 // The CLR is the oracle: the default of a field type known at run time
        type.IsPointer ? Pointer.Box(null, type) : type.IsValueType ? Activator.CreateInstance(type) : null;
#pragma warning restore RS0030

    // The data of a boxed value starts where the first field of any class does
    internal sealed class RawBox
    {
        public byte Data;
    }

    internal struct AlignmentProbe<T>
    {
#pragma warning disable CS0649 // Only the layout is read
        public byte Head;
        public T Value;
#pragma warning restore CS0649
    }
}
