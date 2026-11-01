namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

/// <summary>
/// Transpiles the six <c>System.MemoryExtensions.Sort</c> overloads to the port of the .NET introsort in the Prelude
/// (<c>Sort.hpp</c>), which sorts the span (and the items) in place in the calling thread and gives the order of .NET.
/// </summary>
/// <remarks>
/// The overload is decided from the method symbol. The span, the items and the comparer are evaluated once each, in the
/// C# order. A form that device code cannot run is reported as GSH0003 at the argument that causes it.
/// </remarks>
public sealed class MemoryExtensionsTranspilerRule : IInvocationTranspilerRule
{
    /// <summary>The GSH0003 text for an element type that has no default order on the device.</summary>
    public const string NoDefaultOrder =
        "MemoryExtensions.Sort of elements without a default order on the device (only numbers other than nint and nuint, char, bool, enums and structs that implement IComparable<T> in this compilation have one)";

    /// <summary>The GSH0003 text for a comparer that is not a struct of this compilation.</summary>
    public const string ComparerNotStruct =
        "MemoryExtensions.Sort with a comparer that is not a non-generic struct implementing IComparer<T> in this compilation";

    /// <summary>The GSH0003 text for a comparer struct that implements <c>IComparer&lt;T&gt;</c> for more than one <c>T</c>.</summary>
    public const string SeveralComparerInterfaces =
        "MemoryExtensions.Sort with a comparer struct that implements IComparer<T> for more than one T";

    /// <summary>The GSH0003 text for a comparison that is not a lambda or a device function.</summary>
    public const string ComparisonNotFunction =
        "MemoryExtensions.Sort with a Comparison<T> that is not a lambda or a [GpuDeviceFunction] method group";

    /// <summary>The GSH0003 text for a null comparison, which .NET always rejects (ArgumentNullException).</summary>
    public const string NullComparison = "MemoryExtensions.Sort with a null Comparison<T>";

    /// <summary>The GSH0003 text for named arguments written in another order than the parameters.</summary>
    public const string ReorderedArguments = "MemoryExtensions.Sort with named arguments in another order than the parameters";

    private enum ComparisonKind
    {
        Default,
        Comparer,
        Comparison
    }

    // The shape of one Sort call and the comparison it sorts by, passed from one stage of TryTranspile to the next
    private readonly record struct SortCall(
        ExpressionSyntax? Receiver,
        bool HasItems,
        ArgumentSyntax? ComparerArgument,
        bool IsComparison,
        ITypeSymbol KeyType,
        ComparisonKind Kind = ComparisonKind.Default,
        IMethodSymbol? CompareMember = null);

    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        var method = symbol.GetConstructedReducedFrom() ?? symbol;
        if (SpanWriteClassifier.ClassifyMethod(symbol) != SpanMemberKind.Sort)


        {
            return false;
        }

        if (!IsInParameterOrder(node.ArgumentList.Arguments, symbol))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, ReorderedArguments);
            return true;
        }

        if (TryResolveComparison(ReadShape(node, symbol, method), node, ref context, out var call))
        {
            WriteSortCall(in call, node, symbol, ref context);
        }

        return true;
    }

    // The receiver, the items and the comparer argument of the call, from the parameters of the static method:
    // (keys[, items][, comparer])
    private static SortCall ReadShape(InvocationExpressionSyntax node, IMethodSymbol symbol, IMethodSymbol method)
    {
        // The reduced call keys.Sort(...) has the span as its receiver; the static call has it as the first argument
        var receiver = symbol.ReducedFrom != null && node.Expression is MemberAccessExpressionSyntax access
            ? access.Expression
            : null;

        var arguments = node.ArgumentList.Arguments;
        var definition = method.OriginalDefinition;
        var hasItems = definition.Parameters.Length >= 2 && (IsSpan(definition.Parameters[1].Type) || definition.Parameters[1].Type is IArrayTypeSymbol);
        var comparerArgument = definition.Parameters.Length == (hasItems ? 3 : 2)
            ? arguments[arguments.Count - 1]
            : null;
        var isComparison = comparerArgument != null &&
                           definition.Parameters[definition.Parameters.Length - 1].Type is not ITypeParameterSymbol;
        return new SortCall(receiver, hasItems, comparerArgument, isComparison, method.TypeArguments[0]);
    }

    // The comparison: the classified comparer argument, or the default order of the key; a receiver that the arguments
    // interfere with is reported. Registers the comparison member that the call uses.
    private static bool TryResolveComparison(in SortCall shape, InvocationExpressionSyntax node,
        ref TranspileContext context, out SortCall call)
    {
        call = shape;
        if (!TryClassifyComparison(shape.ComparerArgument, shape.IsComparison, ref context, out var kind,
                out var compareMember))
        {
            return false;
        }

        if (kind == ComparisonKind.Default && !TryGetDefaultOrder(shape.KeyType, node, ref context, out compareMember))
        {
            return false;
        }

        if (shape.Receiver != null &&
            ReceiverInterferes(shape.Receiver, node.ArgumentList.Arguments, context.Query.SemanticModel))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node,
                ExpressionRules.InvocationExpressionTranspilerRule.ExtensionReceiverInterferes);
            return false;
        }

        if (compareMember != null)
        {
            SortComparisonMembers.Use(compareMember, ref context);
        }

        call = shape with { Kind = kind, CompareMember = compareMember };
        return true;
    }

    // <sort>(<keys>[, <items>][, <comparison>]) where the comparison of a comparer struct is
    // [__gsharp_comparer = <comparer>](auto& l, auto& r) mutable { return __gsharp_comparer.__gsharp_compare(l, r); }
    // and the CompareTo of struct keys is [](auto& l, auto& r) { return l.__gsharp_compare_to(r); }.
    // [WHY] A lambda, not a pointer to the member: the sort calls it directly, so nvcc inlines the comparison.
    private static void WriteSortCall(in SortCall call, InvocationExpressionSyntax node, IMethodSymbol symbol,
        ref TranspileContext context)
    {
        var arguments = node.ArgumentList.Arguments;
        var comparerParameter = call.ComparerArgument != null
            ? symbol.FindParameter(arguments, arguments.Count - 1)
            : null;
        using var sequence = EvaluationOrder.BeginCall(ref context, arguments, symbol, receiver: null, spillAll: false);
        var writer = context.Emitter.Writer;
        writer.Append(GetFunctionName(call.HasItems, call.Kind, call.CompareMember != null));
        using (writer.Parentheses())
        {
            if (call.Receiver != null)
            {
                context.Visitor.Visit(call.Receiver);
            }

            var written = call.Receiver != null ? 1 : 0;
            var spans = call.ComparerArgument != null ? arguments.Count - 1 : arguments.Count;
            for (var i = 0; i < spans; i++)
            {
                writer.AppendIf(written > 0, ",");
                sequence.AppendArgument(arguments[i], symbol.FindParameter(arguments, i), ref context);
                written++;
            }

            if (call.Kind == ComparisonKind.Comparison && call.ComparerArgument != null)
            {
                writer.Append(",");
                sequence.AppendArgument(call.ComparerArgument, comparerParameter, ref context);
            }
            else if (call.CompareMember != null)
            {
                writer.Append(",");
                var comparer = call.Kind == ComparisonKind.Comparer ? call.ComparerArgument : null;
                AppendMemberLambda(call.CompareMember, comparer, comparerParameter, in sequence, ref context);
            }
        }
    }

    private static void AppendMemberLambda(IMethodSymbol member, ArgumentSyntax? comparer, IParameterSymbol? parameter,
        in EvaluationOrder.CallSequence sequence, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        var left = GSharpInternalNames.Sort.LeftOperand;
        var right = GSharpInternalNames.Sort.RightOperand;
        if (comparer != null)
        {
            using (writer.Brackets())
            {
                writer.Append($"{GSharpInternalNames.Sort.ComparerCapture} = ");
                sequence.AppendArgument(comparer, parameter, ref context);
            }

            writer.Append($"(auto& {left}, auto& {right}) mutable ");
            using (writer.Braces())
            {
                writer.Append($"return {GSharpInternalNames.Sort.ComparerCapture}.");
                SortComparisonMembers.AppendMemberName(member, writer);
                writer.Append($"({left}, {right});");
            }

            return;
        }

        writer.Append($"[](auto& {left}, auto& {right}) ");
        using (writer.Braces())
        {
            writer.Append($"return {left}.");
            SortComparisonMembers.AppendMemberName(member, writer);
            writer.Append($"({right});");
        }
    }

    private static string GetFunctionName(bool hasItems, ComparisonKind kind, bool hasMember) =>
        (hasItems, kind, hasMember) switch
        {
            (false, ComparisonKind.Default, true) => GSharpInternalNames.Sort.KeysComparable,
            (false, _, _) => GSharpInternalNames.Sort.Keys,
            (true, ComparisonKind.Default, true) => GSharpInternalNames.Sort.PairsComparable,
            (true, _, _) => GSharpInternalNames.Sort.Pairs
        };

    // The comparer argument: none, null or Comparer<T>.Default (the default order), a comparer struct, or a comparison
    private static bool TryClassifyComparison(ArgumentSyntax? argument, bool isComparison, ref TranspileContext context,
        out ComparisonKind kind, out IMethodSymbol? compareMember)
    {
        kind = ComparisonKind.Default;
        compareMember = null;
        if (argument == null)
        {
            return true;
        }

        var model = context.Query.SemanticModel;
        var expression = argument.Expression;
        var isNull = model.GetSafeConstantValue(expression) is { HasValue: true, Value: null };
        if (isComparison)
        {
            if (isNull)
            {
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, argument, NullComparison);
                return false;
            }

            if (!IsDeviceFunction(expression, model, context.Emitter.Dialect))
            {
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, argument, ComparisonNotFunction);
                return false;
            }

            kind = ComparisonKind.Comparison;
            return true;
        }

        if (isNull || IsDefaultComparer(expression, model))
        {
            return true;
        }

        if (model.GetTypeSymbol(expression) is not INamedTypeSymbol { TypeKind: TypeKind.Struct, IsGenericType: false } comparer ||
            !GlobalCppNames.IsEmittedUserType(comparer))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, argument, ComparerNotStruct);
            return false;
        }

        if (!SortComparisonMembers.TryGetUniqueComparerKey(comparer, out var key))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, argument, SeveralComparerInterfaces);
            return false;
        }

        if (!SortComparisonMembers.TryGetCompare(comparer, key, model.Compilation, out compareMember))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, argument, ComparerNotStruct);
            return false;
        }

        kind = ComparisonKind.Comparer;
        return true;
    }

    // The default order of a key: a primitive (the Prelude compares it), or the CompareTo of a user struct
    private static bool TryGetDefaultOrder(ITypeSymbol key, InvocationExpressionSyntax node, ref TranspileContext context,
        out IMethodSymbol? compareTo)
    {
        compareTo = null;
        if (HasPrimitiveOrder(key))
        {
            return true;
        }

        if (key is INamedTypeSymbol { TypeKind: TypeKind.Struct, IsGenericType: false } named &&
            GlobalCppNames.IsEmittedUserType(named) &&
            SortComparisonMembers.TryGetCompareTo(named, context.Query.SemanticModel.Compilation, out compareTo))
        {
            return true;
        }

        context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, NoDefaultOrder);
        return false;
    }

    // Numbers, char, bool and enums: .NET orders them by value (an enum by its underlying value)
    private static bool HasPrimitiveOrder(ITypeSymbol type) =>
        type.TypeKind == TypeKind.Enum || type.SpecialType.IsPrimitiveValue();

    // A lambda, an anonymous method or a method group of a [GpuDeviceFunction]
    private static bool IsDeviceFunction(ExpressionSyntax expression, SemanticModel model, ITranspilerDialect dialect) =>
        expression is AnonymousFunctionExpressionSyntax ||
        (expression is IdentifierNameSyntax or MemberAccessExpressionSyntax &&
         model.GetSymbol(expression) is IMethodSymbol target &&
         CppNameResolver.IsEmittedDeviceFunction(target, dialect));

    // Comparer<T>.Default, which .NET sorts with the default order (GenericArraySortHelper)
    private static bool IsDefaultComparer(ExpressionSyntax expression, SemanticModel model) =>
        model.GetSymbol(expression) is IPropertySymbol { Name: "Default", IsStatic: true } property &&
        property.ContainingType.OriginalDefinition is { Name: "Comparer", TypeParameters.Length: 1 } comparer &&
        comparer.ContainingNamespace is { Name: "Generic", ContainingNamespace: { Name: "Collections", ContainingNamespace: { } system } } &&
        system.IsSystemNamespace();

    private static bool IsSpan(ITypeSymbol type) =>
        type.OriginalDefinition is INamedTypeSymbol { Name: "Span", TypeParameters.Length: 1 } span &&
        span.ContainingNamespace.IsSystemNamespace();

    // Each argument goes to the parameter at its position (named arguments may be written, in order)
    private static bool IsInParameterOrder(SeparatedSyntaxList<ArgumentSyntax> arguments, IMethodSymbol symbol)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].NameColon != null &&
                (i >= symbol.Parameters.Length || symbol.FindParameter(arguments, i) is not { } parameter ||
                 !parameter.IsEqualTo(symbol.Parameters[i])))
            {
                return false;
            }
        }

        return true;
    }

    // Whether an argument writes what the receiver reads or reads what it writes (the receiver is written first in C++,
    // but the temporaries of EvaluationOrder are evaluated before it)
    private static bool ReceiverInterferes(ExpressionSyntax receiver, SeparatedSyntaxList<ArgumentSyntax> arguments,
        SemanticModel model)
    {
        var receiverEffects = ExpressionEffects.Of(receiver, model);
        return arguments
            .AsStackEnumerable()
            .Any((Receiver: receiverEffects, Model: model), static (argument, s) =>
                ExpressionEffects.Of(argument.Expression, s.Model) is var effects &&
                (s.Receiver.InterferesWith(effects) || effects.InterferesWith(s.Receiver)));
    }
}
