namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Chooses the overload of an operator macro's target as C# overload resolution would choose it for a call with the
/// operator's types (#478), and formats the operands and signatures for the messages of GSH0003.
/// </summary>
internal static class OperatorMacroOverloadResolver
{
    // The result of comparing two candidates, or two conversions of the same argument (Roslyn's BetterResult)
    private const int Neither = 0;
    private const int Left = 1;
    private const int Right = 2;

    // The overload of <type>.<name> that a call with the operator's types (result, operands) selects, and whether it
    // takes a context; otherwise null, and <failure> completes "Operator macro target 'T.M' ..."
    internal static (IMethodSymbol Method, bool HasContext)? ResolveOverload(INamedTypeSymbol type, string name,
        IMethodSymbol operatorMethod, Compilation compilation, out string failure)
    {
        var argumentTypes = GetArgumentTypes(operatorMethod);
        var applicable = CollectApplicable(type.GetMembers(name), argumentTypes, compilation, out var rejections);
        if (applicable.Length == 0)
        {
            failure = OperatorMacroMessages.NoApplicableOverload(FormatOperands(operatorMethod),
                rejections ?? OperatorMacroMessages.NoMethodOfThatName);
            return null;
        }

        if (FindBest(applicable, argumentTypes, compilation) is { } best)
        {
            failure = string.Empty;
            return best;
        }

        failure = DescribeAmbiguity(applicable, argumentTypes, compilation);
        return null;
    }

    // The types of the arguments of a call with the operator's types: its return type for the result, then the types
    // of its parameters
    private static ITypeSymbol[] GetArgumentTypes(IMethodSymbol operatorMethod)
    {
        var argumentTypes = new ITypeSymbol[operatorMethod.Parameters.Length + 1];
        argumentTypes[0] = operatorMethod.ReturnType;
        for (var i = 0; i < operatorMethod.Parameters.Length; i++)
        {
            argumentTypes[i + 1] = operatorMethod.Parameters[i].Type;
        }

        return argumentTypes;
    }

    // The candidates among <members> that apply to <argumentTypes> (TryApply), in declaration order; <rejections>
    // lists why each other method does not ("<signature>: <reason>; ..."), or is null when there is none
    private static ImmutableArray<(IMethodSymbol Method, bool HasContext)> CollectApplicable(
        ImmutableArray<ISymbol> members, ITypeSymbol[] argumentTypes, Compilation compilation, out string? rejections)
    {
        var applicable = ImmutableArray.CreateBuilder<(IMethodSymbol Method, bool HasContext)>();
        using var reasons = new SourceWriter();
        foreach (var member in members)
        {
            if (member is not IMethodSymbol method)
            {
                continue;
            }

            if (TryApply(method, argumentTypes, compilation, out var hasContext, out var reason))
            {
                applicable.Add((method, hasContext));
                continue;
            }

            reasons.AppendIf(reasons.Length > 0, "; ");
            reasons.Append($"{method.FormatSignature()}: {reason}");
        }

        rejections = reasons.Length > 0 ? reasons.ToString() : null;
        return applicable.ToImmutable();
    }

    // The candidate that is better than every other one (C# spec §12.6.4.3), or null when there is none
    private static (IMethodSymbol Method, bool HasContext)? FindBest(
        ImmutableArray<(IMethodSymbol Method, bool HasContext)> applicable, ITypeSymbol[] argumentTypes,
        Compilation compilation)
    {
        for (var i = 0; i < applicable.Length; i++)
        {
            var best = true;
            for (var j = 0; j < applicable.Length && best; j++)
            {
                best = i == j || CompareCandidates(applicable[i], applicable[j], argumentTypes, compilation) == Left;
            }

            if (best)
            {
                return applicable[i];
            }
        }

        return null;
    }

    // "is ambiguous between A, B and C": the candidates that no other one is better than, in declaration order
    private static string DescribeAmbiguity(ImmutableArray<(IMethodSymbol Method, bool HasContext)> applicable,
        ITypeSymbol[] argumentTypes, Compilation compilation)
    {
        using var names = new SourceWriter();
        var tied = 0;
        var last = string.Empty;
        for (var i = 0; i < applicable.Length; i++)
        {
            if (IsDominated(i, applicable, argumentTypes, compilation))
            {
                continue;
            }

            if (tied++ > 0)
            {
                names.AppendIf(names.Length > 0, ", ");
                names.Append(last);
            }

            last = applicable[i].Method.FormatSignature();
        }

        return OperatorMacroMessages.Ambiguous(names.ToString(), last);
    }

    // Whether another candidate is better than candidate <index>
    private static bool IsDominated(int index, ImmutableArray<(IMethodSymbol Method, bool HasContext)> applicable,
        ITypeSymbol[] argumentTypes, Compilation compilation)
    {
        for (var j = 0; j < applicable.Length; j++)
        {
            if (j != index && CompareCandidates(applicable[j], applicable[index], argumentTypes, compilation) == Left)
            {
                return true;
            }
        }

        return false;
    }

    // Whether <method> can take (result, operands), optionally after a context of a value type; otherwise <reason> says
    // why not
    private static bool TryApply(IMethodSymbol method, ITypeSymbol[] operandTypes, Compilation compilation,
        out bool hasContext, out string reason)
    {
        hasContext = false;
        if (method.MethodKind != MethodKind.Ordinary || !method.IsStatic ||
            !method.HasAttribute(KnownAttributes.GpuDeviceFunction))
        {
            reason = OperatorMacroMessages.NotStaticDeviceFunction;
            return false;
        }

        if (method.IsGenericMethod)
        {
            reason = OperatorMacroMessages.GenericCandidate;
            return false;
        }

        var parameters = method.Parameters;
        hasContext = parameters.Length == operandTypes.Length + 1 && parameters[0].Type.IsValueType;
        if (parameters.Length != operandTypes.Length && !hasContext)
        {
            reason = OperatorMacroMessages.ParameterCount(parameters.Length, operandTypes.Length);
            return false;
        }

        var offset = hasContext ? 1 : 0;
        for (var i = 0; i < operandTypes.Length; i++)
        {
            if (!CanPass(operandTypes[i], parameters[i + offset], isResult: i == 0, compilation, out reason))
            {
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    // Whether <from> passes to <parameter> (C# spec §12.6.4.2): a value parameter takes an implicit conversion; a
    // by-reference parameter takes only its own type (G# passes the address), and only the result may be ref or out
    private static bool CanPass(ITypeSymbol from, IParameterSymbol parameter, bool isResult, Compilation compilation,
        out string reason)
    {
        reason = string.Empty;
        if (parameter.RefKind != RefKind.None)
        {
            var keyword = parameter.RefKind.FormatRefKind().TrimEnd();
            if (parameter.RefKind is RefKind.Ref or RefKind.Out && !isResult)
            {
                reason = OperatorMacroMessages.ReferenceNotResult(keyword, parameter.Name);
                return false;
            }

            if (!SymbolEqualityComparer.Default.Equals(from, parameter.Type))
            {
                reason = OperatorMacroMessages.ReferenceTakesOnlyItsType(keyword, parameter, from);
                return false;
            }

            return true;
        }

        if (!compilation.ClassifyConversion(from, parameter.Type).IsImplicit)
        {
            reason = OperatorMacroMessages.DoesNotConvert(from, parameter);
            return false;
        }

        return true;
    }

    // Left when <first> is the better function member, Right when <second> is, otherwise Neither: Roslyn's
    // OverloadResolution.BetterFunctionMember, reduced to an operator macro call (one argument of a known type per
    // result and operand; no params, optional or generic candidates). The context is not an argument and is not compared
    // (decision V8). C# spec §12.6.4.3.
    private static int CompareCandidates((IMethodSymbol Method, bool HasContext) first,
        (IMethodSymbol Method, bool HasContext) second, ITypeSymbol[] operandTypes, Compilation compilation)
    {
        var result = Neither;
        var allSame = true;
        for (var i = 0; i < operandTypes.Length; i++)
        {
            var p1 = first.Method.Parameters[i + (first.HasContext ? 1 : 0)];
            var p2 = second.Method.Parameters[i + (second.HasContext ? 1 : 0)];
            allSame &= SymbolEqualityComparer.Default.Equals(p1.Type, p2.Type);
            var better = BetterConversionFromType(operandTypes[i], p1.Type, p2.Type, compilation);
            if (better == Neither)
            {
                continue;
            }

            // "for each argument, the implicit conversion from EX to QX is not better than the implicit conversion
            // from EX to PX, and for at least one argument, the conversion from EX to PX is better"
            if (result != Neither && result != better)
            {
                return Neither;
            }

            result = better;
        }

        // The tie-breakers apply only when the parameter type sequences are the same; of Roslyn's, the one an operator
        // macro can reach is preferValOverInOrRefInterpolatedHandlerParameters: a value parameter is better than an
        // 'in' or 'ref readonly' parameter for the same argument
        return result != Neither || !allSame ? result : PreferValueOverIn(first, second, operandTypes.Length);
    }

    // C# spec §12.6.4.5 (Roslyn BetterConversionFromExpression) for an argument of type <from>: a conversion to the type
    // that <from> is (exactly matches) is better; otherwise the better conversion target decides
    private static int BetterConversionFromType(ITypeSymbol from, ITypeSymbol t1, ITypeSymbol t2, Compilation compilation)
    {
        if (SymbolEqualityComparer.Default.Equals(t1, t2))
        {
            return Neither;
        }

        if (SymbolEqualityComparer.Default.Equals(from, t1))
        {
            return Left;
        }

        return SymbolEqualityComparer.Default.Equals(from, t2) ? Right : BetterConversionTarget(t1, t2, compilation);
    }

    // C# spec §12.6.4.7, Roslyn OverloadResolution.BetterConversionTargetCore:
    //   "Given two different types T1 and T2, T1 is a better conversion target than T2 if no implicit conversion from T2 to
    //   T1 exists, and at least one of the following holds: An implicit conversion from T1 to T2 exists ... T1 is a
    //   signed integral type and T2 is an unsigned integral type."
    // The Task<T>, delegate and span cases cannot occur for device function parameters.
    private static int BetterConversionTarget(ITypeSymbol t1, ITypeSymbol t2, Compilation compilation)
    {
        var oneToTwo = compilation.ClassifyConversion(t1, t2).IsImplicit;
        var twoToOne = compilation.ClassifyConversion(t2, t1).IsImplicit;
        if (oneToTwo != twoToOne)
        {
            return oneToTwo ? Left : Right;
        }

        if (oneToTwo)
        {
            // An implicit conversion both ways
            return Neither;
        }

        if (IsSignedIntegral(t1) && IsUnsignedIntegral(t2))
        {
            return Left;
        }

        return IsUnsignedIntegral(t1) && IsSignedIntegral(t2) ? Right : Neither;
    }

    private static bool IsSignedIntegral(ITypeSymbol type) => type.SpecialType is
        SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64;

    private static bool IsUnsignedIntegral(ITypeSymbol type) => type.SpecialType is
        SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64;

    // Left when <first> passes some argument by value where <second> takes it as 'in' or 'ref readonly' (and never the
    // other way round), Right for the reverse, otherwise Neither
    private static int PreferValueOverIn((IMethodSymbol Method, bool HasContext) first,
        (IMethodSymbol Method, bool HasContext) second, int count)
    {
        var result = Neither;
        for (var i = 0; i < count; i++)
        {
            var refKind1 = first.Method.Parameters[i + (first.HasContext ? 1 : 0)].RefKind;
            var refKind2 = second.Method.Parameters[i + (second.HasContext ? 1 : 0)].RefKind;
            var preferred = refKind1 == RefKind.None && refKind2 is RefKind.In or RefKind.RefReadOnlyParameter ? Left
                : refKind2 == RefKind.None && refKind1 is RefKind.In or RefKind.RefReadOnlyParameter ? Right
                : Neither;
            if (preferred == Neither)
            {
                continue;
            }

            if (result != Neither && result != preferred)
            {
                return Neither;
            }

            result = preferred;
        }

        return result;
    }

    // OverloadValue result, OverloadValue value, int shift
    private static string FormatOperands(IMethodSymbol operatorMethod)
    {
        using var text = new SourceWriter();
        text.Append($"{OperatorMacroMessages.FormatType(operatorMethod.ReturnType)} result");
        foreach (var parameter in operatorMethod.Parameters)
        {
            text.Append($", {OperatorMacroMessages.FormatType(parameter.Type)} {parameter.Name}");
        }

        return text.ToString();
    }
}
