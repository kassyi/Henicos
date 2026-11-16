using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.ReadOnlyArrays;

/// <summary>The GSH0118 analysis: where a method writes an array that is <c>[GpuReadOnly]</c>.</summary>
/// <remarks>
/// <c>[GpuReadOnly]</c> promises that no thread writes the array while the kernel runs, through any alias. The writes
/// the compiler can see are: an element assignment, <c>++</c> and compound assignment, a <c>ref</c> or <c>out</c>
/// element argument, a <c>ref</c> foreach variable over the array, a span or array method that writes its operand
/// (<c>Fill</c>, <c>Clear</c>, <c>Reverse</c>, <c>Sort</c>, <c>CopyTo</c> into it, <c>Array.Copy</c> into it), and
/// passing the array to a method of this compilation that writes that parameter. The array can be reached through a
/// conversion, a slice, <c>AsSpan()</c> or a variable that holds only such values (<see cref="ReadOnlyArrayRoots"/>).
/// The same host array passed to a <c>[GpuReadOnly]</c> parameter and a writable one is not visible here and breaks
/// the promise as well.
/// </remarks>
internal sealed class ReadOnlyArrayWriteAnalysis(Compilation compilation)
{
    public const string ElementWrite = "an element is assigned";
    public const string ElementUpdate = "an element is incremented or decremented";
    public const string ByReference = "an element is passed by ref or out";
    public const string RefVariable = "a ref foreach variable can write its elements";
    public const string WritingMethod = "a method that writes its operand is called on it";
    public const string PassedToWriter = "it is passed to a method that writes that parameter";

    // Operand positions that a span or array method writes: the receiver of an instance or extension call counts as
    // the first operand
    private static readonly ImmutableArray<int> s_firstOperand = ImmutableArray.Create(0);
    private static readonly ImmutableArray<int> s_secondOperand = ImmutableArray.Create(1);
    private static readonly ImmutableArray<int> s_thirdOperand = ImmutableArray.Create(2);
    private static readonly ImmutableArray<int> s_sortOperands = ImmutableArray.Create(0, 1);

    private readonly Dictionary<IParameterSymbol, bool> _parameterWritten = new(SymbolEqualityComparer.Default);
    private readonly HashSet<IParameterSymbol> _inProgress = new(SymbolEqualityComparer.Default);

    /// <summary>The writes in the body of a method, in source order.</summary>
    public ImmutableArray<ReadOnlyArrayWrite> Run(SyntaxNode body, SemanticModel model)
    {
        var writes = ImmutableArray.CreateBuilder<ReadOnlyArrayWrite>();
        writes.AddRange(Writes(body, model, null));
        writes.Sort(static (x, y) => x.Node.SpanStart.CompareTo(y.Node.SpanStart));
        return writes.ToImmutable();
    }

    // The writes of a body, one at a time, so that a caller that only asks whether there is one stops at the first
    private IEnumerable<ReadOnlyArrayWrite> Writes(SyntaxNode body, SemanticModel model,
        IParameterSymbol? checkedParameter)
    {
        // A callee is checked for the one parameter; a write to another [GpuReadOnly] parameter of it is its own report
        var roots = new ReadOnlyArrayRoots(model, checkedParameter, checkedParameter == null,
            AliasSources.Collect(body, model));
        roots.Solve();

        foreach (var node in body.DescendantNodes())
        {
            foreach (var write in WritesOf(node, model, roots))
            {
                yield return write;
            }
        }
    }

    private IEnumerable<ReadOnlyArrayWrite> WritesOf(SyntaxNode node, SemanticModel model, ReadOnlyArrayRoots roots)
    {
        switch (node)
        {
            case AssignmentExpressionSyntax { Left: ElementAccessExpressionSyntax target }:
                return Report(roots, target.Expression, target, ElementWrite);
            case PrefixUnaryExpressionSyntax
            {
                Operand: ElementAccessExpressionSyntax prefixed,
                RawKind: (int)SyntaxKind.PreIncrementExpression or (int)SyntaxKind.PreDecrementExpression
            }:
                return Report(roots, prefixed.Expression, prefixed, ElementUpdate);
            case PostfixUnaryExpressionSyntax
            {
                Operand: ElementAccessExpressionSyntax postfixed,
                RawKind: (int)SyntaxKind.PostIncrementExpression or (int)SyntaxKind.PostDecrementExpression
            }:
                return Report(roots, postfixed.Expression, postfixed, ElementUpdate);
            case ArgumentSyntax
            {
                Expression: ElementAccessExpressionSyntax element,
                RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword or (int)SyntaxKind.OutKeyword
            }:
                return Report(roots, element.Expression, element, ByReference);
            case ForEachStatementSyntax { Type: RefTypeSyntax { ReadOnlyKeyword.RawKind: 0 } variable } loop:
                return Report(roots, loop.Expression, variable, RefVariable);
            case InvocationExpressionSyntax invocation:
                return WritesOfCall(invocation, model, roots);
            default:
                return ImmutableArray<ReadOnlyArrayWrite>.Empty;
        }
    }

    private static IEnumerable<ReadOnlyArrayWrite> Report(ReadOnlyArrayRoots roots, ExpressionSyntax array,
        SyntaxNode at, string reason)
    {
        if (roots.RootOf(array) is { } root)
        {
            yield return new ReadOnlyArrayWrite(at, root.Name, reason);
        }
    }

    private IEnumerable<ReadOnlyArrayWrite> WritesOfCall(InvocationExpressionSyntax invocation, SemanticModel model,
        ReadOnlyArrayRoots roots)
    {
        if (model.GetSymbol(invocation) is not IMethodSymbol method)
        {
            return ImmutableArray<ReadOnlyArrayWrite>.Empty;
        }

        var kind = SpanWriteClassifier.ClassifyMethod(method);
        return kind != SpanMemberKind.None
            ? WritesOfSpanMethod(invocation, method, kind, model, roots)
            : WritesOfWritingCallee(invocation, model, roots);
    }

    // Fill, Clear, Reverse, Sort, CopyTo and Array.Copy write the operands that WrittenIndexes names
    private static IEnumerable<ReadOnlyArrayWrite> WritesOfSpanMethod(InvocationExpressionSyntax invocation,
        IMethodSymbol method, SpanMemberKind kind, SemanticModel model, ReadOnlyArrayRoots roots)
    {
        var operands = OperandsOf(invocation, method);
        foreach (var index in WrittenIndexes(kind))
        {
            // Array.Sort(keys, items) reorders the items only when they are an array
            var isSecondSortOperand = kind == SpanMemberKind.Sort && index == 1;
            if (index >= operands.Length ||
                (isSecondSortOperand && !model.GetTypeInfo(operands[index]).Type.TryGetArrayLikeElementType(out _)))
            {
                continue;
            }

            foreach (var write in Report(roots, operands[index], invocation, WritingMethod))
            {
                yield return write;
            }
        }
    }

    private static ImmutableArray<ExpressionSyntax> OperandsOf(InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        var operands = ImmutableArray.CreateBuilder<ExpressionSyntax>();
        if (invocation.Expression is MemberAccessExpressionSyntax access &&
            (!method.IsStatic || method.ReducedFrom != null))
        {
            operands.Add(access.Expression);
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            operands.Add(argument.Expression);
        }

        return operands.ToImmutable();
    }

    private static ImmutableArray<int> WrittenIndexes(SpanMemberKind kind) => kind switch
    {
        SpanMemberKind.Fill or SpanMemberKind.Clear or SpanMemberKind.Reverse or
            SpanMemberKind.ArrayFill2 or SpanMemberKind.ArrayFill4 or SpanMemberKind.ArrayClear1 or
            SpanMemberKind.ArrayClear3 or SpanMemberKind.ArrayReverse1 or
            SpanMemberKind.ArrayReverse3 => s_firstOperand,
        SpanMemberKind.Sort => s_sortOperands,
        SpanMemberKind.CopyTo or SpanMemberKind.TryCopyTo or SpanMemberKind.ArrayCopy3 => s_secondOperand,
        SpanMemberKind.ArrayCopy5 => s_thirdOperand,
        _ => ImmutableArray<int>.Empty
    };

    // A method of this compilation that writes the parameter that a read-only array is passed to
    private IEnumerable<ReadOnlyArrayWrite> WritesOfWritingCallee(InvocationExpressionSyntax invocation,
        SemanticModel model, ReadOnlyArrayRoots roots)
    {
        if (model.GetOperation(invocation) is not IInvocationOperation call)
        {
            yield break;
        }

        foreach (var argument in call.Arguments)
        {
            if (argument is { Syntax: ArgumentSyntax written, Parameter: { } parameter } &&
                parameter.Type.TryGetArrayLikeElementType(out _) &&
                !parameter.HasAttribute(KnownAttributes.GpuReadOnly) &&
                roots.RootOf(written.Expression) is { } root &&
                ParameterIsWritten(parameter))
            {
                yield return new ReadOnlyArrayWrite(written, root.Name, PassedToWriter);
            }
        }
    }

    // Whether the method that declares the parameter writes it. A method without a declaration in this compilation (of
    // another assembly), and a cycle of calls, count as not writing
    private bool ParameterIsWritten(IParameterSymbol parameter)
    {
        if (_parameterWritten.TryGetValue(parameter, out var known))
        {
            return known;
        }

        if (!_inProgress.Add(parameter))
        {
            return false;
        }

        try
        {
            var written = parameter.ContainingSymbol is IMethodSymbol owner &&
                          owner.TryGetDeclaringSyntax<SyntaxNode>(out var declaration) &&
                          BodyOf(declaration) is { } body &&
                          WritesAny(body, compilation.GetSemanticModel(declaration.SyntaxTree), parameter);
            _parameterWritten[parameter] = written;
            return written;
        }
        finally
        {
            _inProgress.Remove(parameter);
        }
    }

    // Whether the body writes the parameter, found at the first write
    private bool WritesAny(SyntaxNode body, SemanticModel model, IParameterSymbol parameter)
    {
        using var writes = Writes(body, model, parameter).GetEnumerator();
        return writes.MoveNext();
    }

    private static SyntaxNode? BodyOf(SyntaxNode declaration) => declaration switch
    {
        MethodDeclarationSyntax method => method.GetBodyNode(),
        LocalFunctionStatementSyntax local => (CSharpSyntaxNode?)local.Body ?? local.ExpressionBody,
        _ => null
    };
}
