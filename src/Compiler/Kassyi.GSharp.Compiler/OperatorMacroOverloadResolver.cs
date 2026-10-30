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

    // The overload of <type>.<name> that a call with the operator's types (result, operands) selects, and whether it takes
    // a context; otherwise null, and <failure> completes "Operator macro target 'T.M' ..."
    internal static (IMethodSymbol Method, bool HasContext)? ResolveOverload(INamedTypeSymbol type, string name,
        IMethodSymbol operatorMethod, Compilation compilation, out string failure)
    {
        failure = string.Empty;
        var operandTypes = new ITypeSymbol[operatorMethod.Parameters.Length + 1];
        operandTypes[0] = operatorMethod.ReturnType;
        for (var i = 0; i < operatorMethod.Parameters.Length; i++)
        {
            operandTypes[i + 1] = operatorMethod.Parameters[i].Type;
        }

        var applicable = ImmutableArray.CreateBuilder<(IMethodSymbol Method, bool HasContext)>();
        var reasons = new StringBuilder();
        foreach (var member in type.GetMembers(name))
        {
            if (member is not IMethodSymbol method)
            {
                continue;
            }

            if (TryApply(method, operandTypes, compilation, out var hasContext, out var reason))
            {
                applicable.Add((method, hasContext));
                continue;
            }

            reasons.Append(reasons.Length > 0 ? "; " : string.Empty)
                .Append(FormatSignature(method))
                .Append(": ")
                .Append(reason);
        }

        if (applicable.Count == 0)
        {
            var why = reasons.Length > 0 ? reasons.ToString() : "no method has that name";
            failure = $"has no overload that takes ({FormatOperands(operatorMethod)}): {why}";
            return null;
        }

        // The candidate that is better than every other one
        for (var i = 0; i < applicable.Count; i++)
        {
            var best = true;
            for (var j = 0; j < applicable.Count && best; j++)
            {
                best = i == j || CompareCandidates(applicable[i], applicable[j], operandTypes, compilation) == Left;
            }

            if (best)
            {
                return applicable[i];
            }
        }

        // Ambiguous: the candidates that no other one is better than, in declaration order
        var names = new StringBuilder();
        var tied = 0;
        var last = string.Empty;
        for (var i = 0; i < applicable.Count; i++)
        {
            var dominated = false;
            for (var j = 0; j < applicable.Count && !dominated; j++)
            {
                dominated = i != j && CompareCandidates(applicable[j], applicable[i], operandTypes, compilation) == Left;
            }

            if (dominated)
            {
                continue;
            }

            if (tied++ > 0)
            {
                names.Append(names.Length > 0 ? ", " : string.Empty).Append(last);
            }

            last = FormatSignature(applicable[i].Method);
        }

        failure = $"is ambiguous between {names} and {last}";
        return null;
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
            reason = "not a static [GpuDeviceFunction] method";
            return false;
        }

        if (method.IsGenericMethod)
        {
            reason = "generic methods are not candidates";
            return false;
        }

        var parameters = method.Parameters;
        hasContext = parameters.Length == operandTypes.Length + 1 && parameters[0].Type.IsValueType;
        if (parameters.Length != operandTypes.Length && !hasContext)
        {
            reason = $"takes {parameters.Length} parameters, but the operator passes {operandTypes.Length} (or a context of a value type and {operandTypes.Length})";
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
            var keyword = FormatRefKind(parameter.RefKind).TrimEnd();
            if (parameter.RefKind is RefKind.Ref or RefKind.Out && !isResult)
            {
                reason = $"'{keyword}' parameter '{parameter.Name}' is not the result";
                return false;
            }

            if (!SymbolEqualityComparer.Default.Equals(from, parameter.Type))
            {
                reason = $"'{keyword}' parameter '{parameter.Name}' takes only '{FormatType(parameter.Type)}', not '{FormatType(from)}'";
                return false;
            }

            return true;
        }

        if (!compilation.ClassifyConversion(from, parameter.Type).IsImplicit)
        {
            reason = $"'{FormatType(from)}' does not convert to '{FormatType(parameter.Type)}' for '{parameter.Name}'";
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
        var text = new StringBuilder();
        text.Append(FormatType(operatorMethod.ReturnType)).Append(" result");
        foreach (var parameter in operatorMethod.Parameters)
        {
            text.Append(", ").Append(FormatType(parameter.Type)).Append(' ').Append(parameter.Name);
        }

        return text.ToString();
    }

    // Shift(ref OverloadCtx, uint[], uint[], int)
    private static string FormatSignature(IMethodSymbol method)
    {
        var text = new StringBuilder();
        text.Append(method.Name).Append('(');
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];
            text.Append(i > 0 ? ", " : string.Empty)
                .Append(FormatRefKind(parameter.RefKind))
                .Append(FormatType(parameter.Type));
        }

        return text.Append(')').ToString();
    }

    internal static string FormatRefKind(RefKind refKind) => refKind switch
    {
        RefKind.Ref => "ref ",
        RefKind.Out => "out ",
        RefKind.In => "in ",
        RefKind.RefReadOnlyParameter => "ref readonly ",
        _ => string.Empty
    };

    // uint[], OverloadCtx: the short C# spelling of a type in a message
    internal static string FormatType(ITypeSymbol type) => IteratorTypeResolver.FormatType(type);
}
