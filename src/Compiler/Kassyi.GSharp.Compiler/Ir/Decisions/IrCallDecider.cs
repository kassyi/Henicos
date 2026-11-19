using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Transpiler.Managed;

namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>Computes the call target, argument and managed routing decisions of <see cref="IrCall"/> (#1561).</summary>
internal static class IrCallDecider
{
    /// <summary>
    /// Decides claim, target plan, per-argument decisions, and managed routing for <paramref name="call"/>.
    /// </summary>
    public static IrCallDecision DecideCall(IrCall call, IrDecisionContext context)
    {
        DecideClaimAndTarget(call, context, out var claim, out var targetPlan);
        var arguments = DecideArguments(call, claim, targetPlan, context);
        var managed = DecideManaged(call, context);
        return new IrCallDecision(targetPlan, claim, arguments, managed);
    }

    private static void DecideClaimAndTarget(IrCall call, IrDecisionContext context, out IrCallClaim claim,
        out CallTargetPlan? targetPlan)
    {
        var trees = SemanticModelExtensions.GetSyntaxTrees(context.Compilation);
        var tree = trees[call.Source.TreeOrdinal];
        var classified = CallTargetClassifier.ClassifyMethod(call.Method, tree, context.Compilation,
            context.NativeDialect);
        if (classified is { Kind: CallTargetKind.Removed })
        {
            claim = IrCallClaim.None;
            targetPlan = classified;
            return;
        }

        switch (CallTargetClassifier.ClaimOf(call.Method, call.Receiver is not null, context.Compilation))
        {
            case InvocationRuleClaim.BclIntrinsicMap:
                claim = IrCallClaim.BclIntrinsicMap;
                targetPlan = new CallTargetPlan(CallTargetKind.BclIntrinsicMap);
                return;
            case InvocationRuleClaim.Other:
                claim = IrCallClaim.OtherRule;
                targetPlan = null;
                return;
            default:
                claim = IrCallClaim.None;
                targetPlan = classified;
                return;
        }
    }

    private static ImmutableArray<IrArgumentDecision> DecideArguments(IrCall call, IrCallClaim claim,
        CallTargetPlan? targetPlan, IrDecisionContext context)
    {
        var notRemoved = targetPlan?.Kind != CallTargetKind.Removed;
        var checksCaller = notRemoved &&
                           CppNameResolver.IsEmittedDeviceFunction(call.Method, context.NativeDialect) &&
                           !call.Method.IsExtensionMethod &&
                           NonNullProof.ChecksArguments(call.Method);
        var convertsStandard = notRemoved &&
                               claim == IrCallClaim.None &&
                               CallTargetClassifier.ConvertsArguments(call.Method, context.NativeDialect);
        var bclMapping = claim == IrCallClaim.BclIntrinsicMap &&
                         BclIntrinsicMapBuilder.GetOrBuildMap(context.Compilation)
                             .TryGetValue(call.Method.OriginalDefinition, out var info)
            ? info
            : (IntrinsicMappingInfo?)null;

        var builder = ImmutableArray.CreateBuilder<IrArgumentDecision>(call.Arguments.Length);
        foreach (var argument in call.Arguments)
        {
            var convertedTo = DecideArgumentConversion(argument, convertsStandard, bclMapping, context);
            var check = checksCaller
                ? DecideArgumentCheck(argument, context)
                : IrArgumentCheck.None;
            builder.Add(new IrArgumentDecision(convertedTo, check));
        }

        return builder.MoveToImmutable();
    }

    private static ITypeSymbol? DecideArgumentConversion(IrArgument argument, bool convertsStandard,
        IntrinsicMappingInfo? bclMapping, IrDecisionContext context)
    {
        var parameter = argument.Parameter;
        if (parameter.RefKind != RefKind.None || !parameter.Type.SpecialType.IsPrimitiveValue())
        {
            return null;
        }

        var converts = convertsStandard ||
                       (bclMapping is { } info &&
                        (parameter.Ordinal != 0 || info.CastFirstArgumentTo is null));
        if (!converts)
        {
            return null;
        }

        var source = IrNumericFacts.SourceType(argument.Value, context.TemporaryInitializers);
        if (source is not null &&
            source.SpecialType.IsPrimitiveValue() &&
            ArithmeticConversions.IsConverted(source, parameter.Type))
        {
            return parameter.Type;
        }

        return null;
    }

    private static IrArgumentCheck DecideArgumentCheck(IrArgument argument, IrDecisionContext context)
    {
        var parameter = argument.Parameter;
        var kind = ArgumentNullCheck.GetArgumentCheckKind(parameter);
        if (kind == ArgumentCheckKind.None)
        {
            return IrArgumentCheck.None;
        }

        if (kind == ArgumentCheckKind.Array)
        {
            if (IrNonNullProof.IsProvenNonNull(argument.Value, context))
            {
                return IrArgumentCheck.None;
            }

            var message = ArgumentNullCheck.FormatArgumentMessage(parameter, null, parameter.Type, null);
            var argumentSource = IrNonNullProof.UnwrapImplicitReferenceConversions(argument.Value).SpellingSource;
            var site = new IrErrorSite(argumentSource, ArgumentNullCheck.ArgumentTrapKind, message);
            return new IrArgumentCheck(ArgumentCheckKind.Array, site, ImmutableArray<IrStructFieldCheck>.Empty);
        }

        if (IrNonNullProof.IsProvenStruct(argument.Value, context))
        {
            return IrArgumentCheck.None;
        }

        return new IrArgumentCheck(ArgumentCheckKind.Struct, null, DecideStructChecks(argument, parameter));
    }

    private static ImmutableArray<IrStructFieldCheck> DecideStructChecks(IrArgument argument,
        IParameterSymbol parameter)
    {
        var paths = NonNullProof.GetNonNullableArrayFields(parameter.Type);
        var argumentSource = IrNonNullProof.UnwrapImplicitReferenceConversions(argument.Value).SpellingSource;
        var builder = ImmutableArray.CreateBuilder<IrStructFieldCheck>(paths.Length);
        foreach (var path in paths)
        {
            var fieldPath = ArgumentNullCheck.FormatFieldPath(parameter, path);
            var trapKind = ArgumentNullCheck.ArgumentTrapKind + "#" + fieldPath;
            var message = ArgumentNullCheck.FormatArgumentMessage(parameter, fieldPath, path[^1].Type, null);
            var site = new IrErrorSite(argumentSource, trapKind, message);
            builder.Add(new IrStructFieldCheck(site, path, fieldPath));
        }

        return builder.MoveToImmutable();
    }

    private static IrManagedCall DecideManaged(IrCall call, IrDecisionContext context)
    {
        // [WHY] The operation gives the static form of an extension method called on its receiver (x.F(a)), so the
        // builder marks the receiver; that call routes to the declared extension method, as the reduced symbol does
        var routed = call.Arguments is [{ Kind: IrArgumentKind.ExtensionReceiver }, ..]
            ? (ManagedRouting.IsRouted(call.Method.OriginalDefinition, context.Compilation)
                ? call.Method.OriginalDefinition
                : null)
            : (ManagedRouting.IsRoutedReference(call.Method, context.Compilation) ? call.Method : null);
        var addsCallerInfo = ManagedCallForms.AddsCallerInfo(call.Method);
        return new IrManagedCall(routed, addsCallerInfo);
    }
}
