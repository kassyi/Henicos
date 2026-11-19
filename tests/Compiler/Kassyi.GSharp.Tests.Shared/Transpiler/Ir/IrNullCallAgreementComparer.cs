#nullable enable
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Numeric;
using Kassyi.GSharp.Compiler.Transpiler.Managed;
using Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;
using Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Compares <see cref="IrCallDecision"/> against legacy call classification, argument checks, conversions, and
/// managed routing (#1561).
/// </summary>
internal static class IrNullCallAgreementComparer
{
    public static void CompareCall(IrCall call, InvocationExpressionSyntax syntax, IMethodSymbol called,
        SemanticModel model, Compilation compilation, HashSet<ISymbol> frameSymbols, string where,
        IrNullAgreementCounts counts)
    {
        var d = call.Decided;
        var label = $"{where}: {syntax.Kind()} {syntax.Span}";
        var legacyClaim = ComputeLegacyClaim(called, syntax, model, compilation);
        if (d.Claim != legacyClaim)
        {
            counts.Mismatches.Add($"{label}: Claim syntax {legacyClaim} IR {d.Claim}");
        }

        CompareCallTarget(d, called, syntax, model, label, counts);
        CompareCallArguments(call, d, called, syntax, model, compilation, frameSymbols, where, label, counts);
        CompareManagedCall(d, called, compilation, label, counts);
    }

    private static IrCallClaim ComputeLegacyClaim(IMethodSymbol called, InvocationExpressionSyntax syntax,
        SemanticModel model, Compilation compilation)
    {
        var userTree = RemovedCalls.GetUserTree(syntax, model);
        if (userTree is not null && RemovedCalls.IsRemoved(called, userTree))
        {
            return IrCallClaim.None;
        }

        if (BclIntrinsicMapBuilder.GetOrBuildMap(compilation).ContainsKey(called.OriginalDefinition))
        {
            return IrCallClaim.BclIntrinsicMap;
        }

        var method = (called.ReducedFrom ?? called).OriginalDefinition;
        if (GpuMacroClassifier.Classify(method).Kind != GpuMacroKind.None ||
            SpanWriteClassifier.ClassifyMethod(called) != SpanMemberKind.None ||
            NullableGetValueOrDefaultRule.Claims(syntax, called) ||
            MathAbsTranspilerRule.Claims(method) ||
            StackLinqHelper.IsStackLinqMethod(method))
        {
            return IrCallClaim.OtherRule;
        }

        return IrCallClaim.None;
    }

    private static void CompareCallTarget(IrCallDecision d, IMethodSymbol called, InvocationExpressionSyntax syntax,
        SemanticModel model, string label, IrNullAgreementCounts counts)
    {
        var legacyKind = CallTargetClassifier.Classify(called, syntax, model, HostDialects.Cpu).Kind;
        if (CppNameResolver.IsEmittedDeviceFunction(called, HostDialects.Cpu) !=
            (d.Target == CallTargetKind.DeviceFunction))
        {
            counts.EmittedTargetDifferences.Add(
                $"{label}: IsEmitted={CppNameResolver.IsEmittedDeviceFunction(called, HostDialects.Cpu)} " +
                $"Target={d.Target} LegacyKind={legacyKind} ({called.Name})");
        }

        if (d.Claim == IrCallClaim.BclIntrinsicMap)
        {
            if (d.Target != CallTargetKind.BclIntrinsicMap)
            {
                counts.Mismatches.Add($"{label}: BclMap Target expected BclIntrinsicMap IR {d.Target}");
            }
            return;
        }

        if (d.Claim == IrCallClaim.OtherRule)
        {
            if (d.Target is not null)
            {
                counts.Mismatches.Add($"{label}: OtherRule Target expected null IR {d.Target}");
            }
            return;
        }

        if (d.Target is { } targetKind)
        {
            if (targetKind != legacyKind)
            {
                counts.Mismatches.Add($"{label}: Target syntax {legacyKind} IR {targetKind}");
            }
        }
        else if (legacyKind is not (CallTargetKind.MisusedMacro or CallTargetKind.ClaimedByRule or
                 CallTargetKind.ArrayViewMember or CallTargetKind.Unemitted or CallTargetKind.Unmapped))
        {
            counts.Mismatches.Add($"{label}: Target null unexpected for syntax {legacyKind}");
        }
    }

    private static void CompareCallArguments(IrCall call, IrCallDecision d, IMethodSymbol called,
        InvocationExpressionSyntax syntax, SemanticModel model, Compilation compilation, HashSet<ISymbol> frameSymbols,
        string where, string label, IrNullAgreementCounts counts)
    {
        var syntaxArgs = syntax.ArgumentList.Arguments;
        var checksCaller = CppNameResolver.IsEmittedDeviceFunction(called, HostDialects.Cpu) &&
                           !called.IsExtensionMethod &&
                           NonNullProof.ChecksArguments(called) &&
                           d.Target != CallTargetKind.Removed;
        var bclMap = BclIntrinsicMapBuilder.GetOrBuildMap(compilation);

        // The syntax path writes the receiver of a reduced extension call apart from the arguments, with no check and
        // no conversion
        if (called.ReducedFrom is not null && call.Arguments is [{ Kind: IrArgumentKind.ExtensionReceiver }, ..])
        {
            Interlocked.Increment(ref counts.ArgumentCheckCount);
            if (d.Arguments[0].Check.Kind != ArgumentCheckKind.None || d.Arguments[0].ConvertedTo is not null)
            {
                counts.Mismatches.Add($"{label}: extension receiver IR {d.Arguments[0].Check.Kind} " +
                                      $"{d.Arguments[0].ConvertedTo?.Name}");
            }
        }

        for (var j = 0; j < syntaxArgs.Count; j++)
        {
            if (called.FindParameter(syntaxArgs, j) is not { } parameter)
            {
                continue;
            }

            var irIndex = FindIrArgumentIndex(call, called, parameter);
            if (irIndex < 0)
            {
                continue;
            }

            var irArg = d.Arguments[irIndex];
            var argExpr = syntaxArgs[j].Expression;
            CompareSingleArgCheck(irArg.Check, checksCaller, argExpr, parameter, model, frameSymbols, where, label,
                counts);
            CompareSingleArgConversion(irArg.ConvertedTo, d, called, syntaxArgs[j], parameter, model, bclMap, label,
                counts);
        }
    }

    // The IR holds the static form of an extension method called on its receiver: its parameter k + 1 is the parameter
    // k of the reduced method the syntax binds
    private static int FindIrArgumentIndex(IrCall call, IMethodSymbol called, IParameterSymbol parameter)
    {
        var ordinal = called.ReducedFrom is null ? -1 : parameter.Ordinal + 1;
        for (var i = 0; i < call.Arguments.Length; i++)
        {
            var irParameter = call.Arguments[i].Parameter;
            if (ordinal >= 0
                    ? irParameter.Ordinal == ordinal
                    : SymbolEqualityComparer.Default.Equals(irParameter, parameter))
            {
                return i;
            }
        }
        return -1;
    }

    private static void CompareSingleArgCheck(IrArgumentCheck irCheck, bool checksCaller, ExpressionSyntax argExpr,
        IParameterSymbol parameter, SemanticModel model, HashSet<ISymbol> frameSymbols, string where, string label,
        IrNullAgreementCounts counts)
    {
        Interlocked.Increment(ref counts.ArgumentCheckCount);
        var legacyKind = checksCaller && ArgumentNullCheck.ChecksArgument(argExpr, parameter, model)
            ? ArgumentNullCheck.GetArgumentCheckKind(parameter)
            : ArgumentCheckKind.None;

        if (irCheck.Kind != legacyKind)
        {
            if (!(irCheck.Kind == ArgumentCheckKind.None && legacyKind != ArgumentCheckKind.None &&
                  IrNullAgreementFrameTracker.TryRecordFrameVariable(argExpr, model, frameSymbols, where, counts)))
            {
                counts.Mismatches.Add($"{label}: ArgCheck({parameter.Name}) syntax {legacyKind} IR {irCheck.Kind}");
            }
            return;
        }

        if (legacyKind == ArgumentCheckKind.Array)
        {
            var expectedMsg = ArgumentNullCheck.FormatArgumentMessage(parameter, null, parameter.Type, null);
            if (irCheck.ArraySite is not { } site || site.Message != expectedMsg ||
                site.Source.Span != argExpr.Span)
            {
                counts.Mismatches.Add($"{label}: ArgArraySite mismatch for {parameter.Name}");
            }
        }
        else if (legacyKind == ArgumentCheckKind.Struct)
        {
            CompareStructArgChecks(irCheck, argExpr, parameter, label, counts);
        }
    }

    private static void CompareStructArgChecks(IrArgumentCheck irCheck, ExpressionSyntax argExpr,
        IParameterSymbol parameter, string label, IrNullAgreementCounts counts)
    {
        var paths = NonNullProof.GetNonNullableArrayFields(parameter.Type);
        if (irCheck.StructChecks.Length != paths.Length)
        {
            counts.Mismatches.Add($"{label}: ArgStructChecks count mismatch for {parameter.Name}");
            return;
        }

        for (var k = 0; k < paths.Length; k++)
        {
            var fieldPath = ArgumentNullCheck.FormatFieldPath(parameter, paths[k]);
            var expectedMsg = ArgumentNullCheck.FormatArgumentMessage(parameter, fieldPath, paths[k][^1].Type, null);
            var check = irCheck.StructChecks[k];
            if (check.FieldPath != fieldPath || check.Site.Message != expectedMsg ||
                check.Site.Source.Span != argExpr.Span)
            {
                counts.Mismatches.Add($"{label}: ArgStructCheck[{k}] mismatch for {parameter.Name}");
            }
        }
    }

    private static void CompareSingleArgConversion(ITypeSymbol? irConverted, IrCallDecision d, IMethodSymbol called,
        ArgumentSyntax argSyntax, IParameterSymbol parameter, SemanticModel model,
        ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> bclMap, string label, IrNullAgreementCounts counts)
    {
        Interlocked.Increment(ref counts.ArgumentConversionCount);
        ITypeSymbol? expected = null;
        if (parameter.RefKind == RefKind.None && parameter.Type.SpecialType.IsPrimitiveValue())
        {
            var converts = d.Claim switch
            {
                IrCallClaim.BclIntrinsicMap when bclMap.TryGetValue(called.OriginalDefinition, out var info) =>
                    parameter.Ordinal != 0 || info.CastFirstArgumentTo is null,
                IrCallClaim.None when d.Target != CallTargetKind.Removed =>
                    CallTargetClassifier.ConvertsArguments(called, HostDialects.Cpu),
                _ => false
            };

            if (converts && model.GetSafeOperation(argSyntax) is IArgumentOperation { Value: { } val })
            {
                var sourceType = val is IConversionOperation { IsImplicit: true, Operand.Type: { } inner }
                    ? inner
                    : val.Type;
                if (sourceType is not null && sourceType.SpecialType.IsPrimitiveValue() &&
                    ArithmeticConversions.IsConverted(sourceType, parameter.Type))
                {
                    expected = parameter.Type;
                }
            }
        }

        if (!SymbolEqualityComparer.Default.Equals(irConverted, expected))
        {
            counts.Mismatches.Add(
                $"{label}: ArgConvertedTo({parameter.Name}) syntax {expected?.Name} IR {irConverted?.Name}");
        }
    }

    private static void CompareManagedCall(IrCallDecision d, IMethodSymbol called, Compilation compilation,
        string label, IrNullAgreementCounts counts)
    {
        Interlocked.Increment(ref counts.ManagedCount);
        var expectedRouted = called.ReducedFrom is null
            ? (ManagedRouting.IsRoutedReference(called, compilation) ? called : null)
            : ManagedRouting.GetRoutedExtensionCall(called, compilation);
        var expectedCallerInfo = ManagedCallForms.AddsCallerInfo(called);

        if (!SymbolEqualityComparer.Default.Equals(d.Managed.RoutedMethod, expectedRouted) ||
            d.Managed.AddsCallerInfo != expectedCallerInfo)
        {
            counts.Mismatches.Add(
                $"{label}: Managed syntax ({expectedRouted?.Name},{expectedCallerInfo}) " +
                $"IR ({d.Managed.RoutedMethod?.Name},{d.Managed.AddsCallerInfo})");
        }
    }
}
