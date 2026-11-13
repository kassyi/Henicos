using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.ReadOnlyArrays;

/// <summary>A write to a read-only array: where to report it, the array's name, and the form of the write.</summary>
internal readonly record struct ReadOnlyArrayWrite(SyntaxNode Node, string Name, string Reason);

/// <summary>The GSH0118 analysis: where a method writes an array that is <c>[GpuReadOnly]</c>.</summary>
/// <remarks>
/// <c>[GpuReadOnly]</c> promises that no thread writes the array while the kernel runs, through any alias. The writes
/// the compiler can see are: an element assignment, <c>++</c> and compound assignment, a <c>ref</c> or <c>out</c>
/// element argument, a span or array method that writes its operand (<c>Fill</c>, <c>Clear</c>, <c>Reverse</c>, <c>Sort</c>,
/// <c>CopyTo</c> into it, <c>Array.Copy</c> into it), and passing the array to a method of this compilation that writes
/// that parameter. The array can be reached through a conversion, a slice, <c>AsSpan()</c> or a local that holds only
/// such values (<see cref="ReadOnlyArrayRoots"/>). The same host array passed to a <c>[GpuReadOnly]</c> parameter and a
/// writable one is not visible here and breaks the promise as well.
/// </remarks>
internal sealed class ReadOnlyArrayWriteAnalysis(Compilation compilation)
{
    public const string ElementWrite = "an element is assigned";
    public const string ElementUpdate = "an element is incremented or decremented";
    public const string ByReference = "an element is passed by ref or out";
    public const string WritingMethod = "a method that writes its operand is called on it";
    public const string PassedToWriter = "it is passed to a method that writes that parameter";

    private readonly Dictionary<IParameterSymbol, bool> _parameterWritten = new(SymbolEqualityComparer.Default);
    private readonly HashSet<IParameterSymbol> _inProgress = new(SymbolEqualityComparer.Default);

    /// <summary>The writes in the body of a method, in source order.</summary>
    public ImmutableArray<ReadOnlyArrayWrite> Run(SyntaxNode body, SemanticModel model)
    {
        var findings = ImmutableArray.CreateBuilder<ReadOnlyArrayWrite>();
        Scan(body, model, null, findings);
        findings.Sort(static (x, y) => x.Node.SpanStart.CompareTo(y.Node.SpanStart));
        return findings.ToImmutable();
    }

    private bool Scan(SyntaxNode body, SemanticModel model, IParameterSymbol? checkedParameter,
        ImmutableArray<ReadOnlyArrayWrite>.Builder? findings)
    {
        var roots = new ReadOnlyArrayRoots(model, checkedParameter, CollectLocalSources(body, model));
        roots.Solve();

        var any = false;
        foreach (var node in body.DescendantNodes())
        {
            var found = ImmutableArray.CreateBuilder<ReadOnlyArrayWrite>();
            switch (node)
            {
                case AssignmentExpressionSyntax { Left: ElementAccessExpressionSyntax target }:
                    ReportIfRoot(roots, target.Expression, target, ElementWrite, found);
                    break;
                case PrefixUnaryExpressionSyntax
                {
                    Operand: ElementAccessExpressionSyntax prefixed,
                    RawKind: (int)SyntaxKind.PreIncrementExpression or (int)SyntaxKind.PreDecrementExpression
                }:
                    ReportIfRoot(roots, prefixed.Expression, prefixed, ElementUpdate, found);
                    break;
                case PostfixUnaryExpressionSyntax
                {
                    Operand: ElementAccessExpressionSyntax postfixed,
                    RawKind: (int)SyntaxKind.PostIncrementExpression or (int)SyntaxKind.PostDecrementExpression
                }:
                    ReportIfRoot(roots, postfixed.Expression, postfixed, ElementUpdate, found);
                    break;
                case ArgumentSyntax
                {
                    Expression: ElementAccessExpressionSyntax element,
                    RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword or (int)SyntaxKind.OutKeyword
                }:
                    ReportIfRoot(roots, element.Expression, element, ByReference, found);
                    break;
                case InvocationExpressionSyntax invocation:
                    CheckInvocation(invocation, model, roots, found);
                    break;
            }

            if (found.Count == 0)
            {
                continue;
            }

            any = true;
            if (findings == null)
            {
                return true;
            }

            findings.AddRange(found);
        }

        return any;
    }

    private static void ReportIfRoot(ReadOnlyArrayRoots roots, ExpressionSyntax array, SyntaxNode at, string reason,
        ImmutableArray<ReadOnlyArrayWrite>.Builder found)
    {
        if (roots.RootOf(array) is { } root)
        {
            found.Add(new ReadOnlyArrayWrite(at, root.Name, reason));
        }
    }

    private void CheckInvocation(InvocationExpressionSyntax invocation, SemanticModel model, ReadOnlyArrayRoots roots,
        ImmutableArray<ReadOnlyArrayWrite>.Builder found)
    {
        if (model.GetSymbol(invocation) is not IMethodSymbol method)
        {
            return;
        }

        var kind = SpanWriteClassifier.ClassifyMethod(method);
        if (kind != SpanMemberKind.None)
        {
            foreach (var operand in WrittenOperands(invocation, method, kind, model))
            {
                ReportIfRoot(roots, operand, invocation, WritingMethod, found);
            }

            return;
        }

        if (model.GetOperation(invocation) is not IInvocationOperation call)
        {
            return;
        }

        foreach (var argument in call.Arguments)
        {
            if (argument is { Syntax: ArgumentSyntax written, Parameter: { } parameter } &&
                parameter.Type.TryGetArrayLikeElementType(out _) &&
                !parameter.HasAttribute(KnownAttributes.GpuReadOnly) &&
                roots.RootOf(written.Expression) is { } root &&
                ParameterIsWritten(parameter))
            {
                found.Add(new ReadOnlyArrayWrite(written, root.Name, PassedToWriter));
            }
        }
    }

    // The operands of a classified span or array method that it writes, counting the receiver of an instance or
    // extension call as the first operand
    private static List<ExpressionSyntax> WrittenOperands(InvocationExpressionSyntax invocation,
        IMethodSymbol method, SpanMemberKind kind, SemanticModel model)
    {
        var operands = new List<ExpressionSyntax>();
        if (invocation.Expression is MemberAccessExpressionSyntax access &&
            (!method.IsStatic || method.ReducedFrom != null))
        {
            operands.Add(access.Expression);
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            operands.Add(argument.Expression);
        }

        var written = new List<ExpressionSyntax>();
        switch (kind)
        {
            case SpanMemberKind.Fill or SpanMemberKind.Clear or SpanMemberKind.Reverse or
                SpanMemberKind.ArrayFill2 or SpanMemberKind.ArrayFill4 or SpanMemberKind.ArrayClear1 or
                SpanMemberKind.ArrayClear3 or SpanMemberKind.ArrayReverse1 or SpanMemberKind.ArrayReverse3:
                AddOperand(written, operands, 0);
                break;
            case SpanMemberKind.Sort:
                AddOperand(written, operands, 0);
                // Array.Sort(keys, items) reorders the items as well
                if (operands.Count > 1 && model.GetTypeInfo(operands[1]).Type.TryGetArrayLikeElementType(out _))
                {
                    AddOperand(written, operands, 1);
                }

                break;
            case SpanMemberKind.CopyTo or SpanMemberKind.TryCopyTo or SpanMemberKind.ArrayCopy3:
                AddOperand(written, operands, 1);
                break;
            case SpanMemberKind.ArrayCopy5:
                AddOperand(written, operands, 2);
                break;
        }

        return written;
    }

    private static void AddOperand(List<ExpressionSyntax> written, List<ExpressionSyntax> operands, int index)
    {
        if (index < operands.Count)
        {
            written.Add(operands[index]);
        }
    }

    // Whether the method that declares the parameter writes it. A method without a declaration in this compilation, and
    // a cycle of calls, count as not writing
    private bool ParameterIsWritten(IParameterSymbol parameter)
    {
        if (_parameterWritten.TryGetValue(parameter, out var known))
        {
            return known;
        }

        if (!_inProgress.Add(parameter) ||
            parameter.ContainingSymbol is not IMethodSymbol owner ||
            !owner.TryGetDeclaringSyntax<SyntaxNode>(out var declaration) ||
            BodyOf(declaration) is not { } body)
        {
            return false;
        }

        var written = Scan(body, compilation.GetSemanticModel(declaration.SyntaxTree), parameter, null);
        _inProgress.Remove(parameter);
        _parameterWritten[parameter] = written;
        return written;
    }

    private static SyntaxNode? BodyOf(SyntaxNode declaration) => declaration switch
    {
        MethodDeclarationSyntax method => method.GetBodyNode(),
        LocalFunctionStatementSyntax local => (CSharpSyntaxNode?)local.Body ?? local.ExpressionBody,
        _ => null
    };

    // Everything a local is given: its initializer and the right side of each simple assignment. A local that is also
    // assigned compound-wise, passed by ref or out, or deconstructed into has a source that cannot be read, so it is
    // left out and holds no read-only array
    private static Dictionary<ILocalSymbol, ImmutableArray<ExpressionSyntax>> CollectLocalSources(SyntaxNode body,
        SemanticModel model)
    {
        var sources = new Dictionary<ILocalSymbol, ImmutableArray<ExpressionSyntax>.Builder>(
            SymbolEqualityComparer.Default);
        var unreadable = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        void Add(ILocalSymbol local, ExpressionSyntax source)
        {
            if (!sources.TryGetValue(local, out var list))
            {
                sources[local] = list = ImmutableArray.CreateBuilder<ExpressionSyntax>();
            }

            list.Add(source);
        }

        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case VariableDeclaratorSyntax { Initializer.Value: { } value } declarator when
                    model.GetDeclaredSymbol(declarator) is ILocalSymbol declared &&
                    declared.Type.TryGetArrayLikeElementType(out _):
                    Add(declared, value);
                    break;
                case AssignmentExpressionSyntax { Left: IdentifierNameSyntax target } assignment when
                    model.GetSymbol(target) is ILocalSymbol local:
                    if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    {
                        Add(local, assignment.Right);
                    }
                    else
                    {
                        unreadable.Add(local);
                    }

                    break;
                case AssignmentExpressionSyntax
                {
                    Left: TupleExpressionSyntax or DeclarationExpressionSyntax
                } deconstruction:
                    foreach (var inner in deconstruction.Left.DescendantNodesAndSelf())
                    {
                        if (inner is IdentifierNameSyntax && model.GetSymbol(inner) is ILocalSymbol deconstructed)
                        {
                            unreadable.Add(deconstructed);
                        }
                    }

                    break;
                case ArgumentSyntax
                {
                    Expression: IdentifierNameSyntax passed,
                    RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword or (int)SyntaxKind.OutKeyword
                } when model.GetSymbol(passed) is ILocalSymbol byReference:
                    unreadable.Add(byReference);
                    break;
            }
        }

        var result = new Dictionary<ILocalSymbol, ImmutableArray<ExpressionSyntax>>(SymbolEqualityComparer.Default);
        foreach (var pair in sources)
        {
            if (!unreadable.Contains(pair.Key))
            {
                result[pair.Key] = pair.Value.ToImmutable();
            }
        }

        return result;
    }
}
