using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>
/// Checks the invariants that every IR tree keeps (positions, value categories, arguments, variables, scopes), for
/// tests and for the passes that rewrite the tree.
/// </summary>
/// <remarks>
/// Each violation is one line that starts with the number of the invariant (<c>I7 ...</c>). Whether a node that is not
/// synthesized has the span of a node of its tree (I2) is checked by the tests only, since it searches the tree. The
/// types keep the rest: a constant is a value (or a null pointer) with a constant value (I12), an opaque node has no
/// children (I13).
/// </remarks>
internal sealed class IrInvariants
{
    private readonly IrMethod _method;
    private readonly SyntaxTree[] _trees;
    private readonly ImmutableArray<string>.Builder _violations = ImmutableArray.CreateBuilder<string>();
    private readonly Dictionary<ISymbol, ImmutableArray<IrVariable>> _variablesBySymbol =
        new(SymbolEqualityComparer.Default);
    private readonly HashSet<IrVariable> _declaredByReference = [];

    private IrInvariants(IrMethod method, Compilation compilation)
    {
        _method = method;
        _trees = SemanticModelExtensions.GetSyntaxTrees(compilation);
    }

    /// <summary>The violations of the invariants in <paramref name="method"/>; empty when it keeps them all.</summary>
    public static ImmutableArray<string> Verify(IrMethod method, Compilation compilation)
    {
        var checker = new IrInvariants(method, compilation);
        checker.CheckSource(method.Source, method.Source);
        checker.Node(method.Body, null, ImmutableStack<ImmutableArray<IrVariable>>.Empty);
        return checker._violations.ToImmutable();
    }

    /// <summary>
    /// The violations of a tree after the classify pass: I24, every call is classified; I25, every opaque node says
    /// whether it holds a split point. Empty when it keeps both.
    /// </summary>
    public static ImmutableArray<string> VerifyClassified(IrMethod method)
    {
        var violations = ImmutableArray.CreateBuilder<string>();
        foreach (var node in IrNodeChildren.DescendantsAndSelf(method.Body))
        {
            var (number, text) = node switch
            {
                IrCall { Classification.IsClassified: false } => ("I24", "a call is not classified"),
                IrOpaqueExpression { Split: IrOpaqueSplit.Unclassified } or
                    IrOpaqueStatement { Split: IrOpaqueSplit.Unclassified } =>
                    ("I25", "an opaque node does not say whether it holds a split point"),
                _ => ((string?)null, "")
            };
            if (number != null)
            {
                var start = node.Source.Start;
                violations.Add($"{number} at {start}..{start + node.Source.Length}: {text}");
            }
        }

        return violations.ToImmutable();
    }

    /// <summary>Whether <paramref name="node"/> is or holds an opaque statement or expression anywhere.</summary>
    public static bool ContainsOpaque(IrNode node) =>
        IrNodeChildren.Exists(node, static child => child is IrOpaqueExpression or IrOpaqueStatement);

    // <scopes>: the locals of the blocks and sequences that enclose <node>, the innermost on top
    private void Node(IrNode node, IrNode? parent, ImmutableStack<ImmutableArray<IrVariable>> scopes)
    {
        CheckSource(node.Source, parent?.Source ?? _method.Source);
        if (node is IrExpression expression)
        {
            CheckExpression(expression, parent, scopes);
        }
        else
        {
            CheckStatement(node, scopes);
        }

        var inner = node switch
        {
            IrBlock block => scopes.Push(block.Locals),
            IrSequence sequence => scopes.Push(sequence.Locals),
            _ => scopes
        };
        foreach (var child in IrNodeChildren.Of(node))
        {
            Node(child, node, inner);
        }
    }

    private void Report(IrNode node, string? violation)
    {
        if (violation is not null)
        {
            Report(node.Source, violation);
        }
    }

    // "I7: ..." is reported as "I7 at <start>..<end>: ..."
    private void Report(IrSource source, string violation)
    {
        var colon = violation.IndexOf(':');
        _violations.Add(
            $"{violation[..colon]} at {source.Start}..{source.Start + source.Length}{violation[colon..]}");
    }

    // I1, I23: a position in a tree of the compilation, and a synthesized node inside its parent
    private void CheckSource(IrSource source, IrSource parent)
    {
        if (source.TreeOrdinal < 0 || source.TreeOrdinal >= _trees.Length ||
            source.Start < 0 || source.Length < 0 ||
            source.Start + source.Length > _trees[source.TreeOrdinal].Length)
        {
            Report(source, "I1: the position is outside the trees");
        }

        if (source.Synthesized && (source.TreeOrdinal != parent.TreeOrdinal || !parent.Span.Contains(source.Span)))
        {
            Report(source, "I23: a synthesized node lies outside its parent");
        }
    }

    private void CheckStatement(IrNode statement, ImmutableStack<ImmutableArray<IrVariable>> scopes)
    {
        switch (statement)
        {
            case IrLocalDeclaration declaration:
                CheckDeclaration(declaration, scopes);
                break;
            case IrReturn { Value: { } value } @return:
                Report(@return, IrNodeChecks.Return(_method.Symbol, value));
                break;
        }
    }

    private void CheckDeclaration(IrLocalDeclaration declaration, ImmutableStack<ImmutableArray<IrVariable>> scopes)
    {
        if (declaration.Variable.Kind != IrVariableKind.Local)
        {
            Report(declaration, "I10: a declaration declares a variable that is not a local");
        }

        CheckVariable(declaration.Variable, declaration, scopes);
    }

    private void CheckExpression(IrExpression expression, IrNode? parent,
        ImmutableStack<ImmutableArray<IrVariable>> scopes)
    {
        Report(expression, IrNodeChecks.Category(expression, IrNodeChildren.IsWriteTarget(parent, expression)));
        Report(expression, IrNodeChecks.Node(expression));
        switch (expression)
        {
            case IrVariableReference reference:
                CheckReference(reference, IrNodeChildren.IsOutArgument(parent, reference), scopes);
                break;
            case IrCall call:
                CheckArguments(call, call.Arguments, call.Method.Parameters);
                break;
            case IrPropertyReference property:
                CheckArguments(property, property.Arguments, property.Property.Parameters);
                break;
            case IrStructCreation creation:
                CheckArguments(creation, creation.Arguments,
                    creation.Constructor?.Parameters ?? ImmutableArray<IParameterSymbol>.Empty);
                break;
        }
    }

    // I7, I8, I22: one argument per parameter, in parameter order, each of the category its parameter takes
    private void CheckArguments(IrExpression holder, ImmutableArray<IrArgument> arguments,
        ImmutableArray<IParameterSymbol> parameters)
    {
        if (arguments.Length != parameters.Length)
        {
            Report(holder, "I8: the number of arguments differs from the number of parameters");
        }

        var seen = new bool[arguments.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (index < parameters.Length && !argument.Parameter.IsEqualTo(parameters[index]))
            {
                Report(holder, "I8: an argument is not in the position of its parameter");
            }

            var position = argument.EvaluationIndex;
            if (position < 0 || position >= seen.Length || seen[position])
            {
                Report(holder, "I8: the evaluation positions of the arguments are not a permutation");
            }
            else
            {
                seen[position] = true;
            }

            Report(holder, IrNodeChecks.Argument(argument));
        }
    }

    // I17: a local is declared by at most one reference, the value of an out argument
    private void CheckReference(IrVariableReference reference, bool isOutArgument,
        ImmutableStack<ImmutableArray<IrVariable>> scopes)
    {
        CheckVariable(reference.Variable, reference, scopes);
        if (!reference.IsDeclaration)
        {
            return;
        }

        if (!isOutArgument)
        {
            Report(reference, "I17: a declaring reference is not the value of an out argument");
        }

        if (!_declaredByReference.Add(reference.Variable))
        {
            Report(reference, "I17: a local is declared by more than one reference");
        }
    }

    // I9, I19, I20: one instance per symbol and region, and every variable in scope
    private void CheckVariable(IrVariable variable, IrNode node, ImmutableStack<ImmutableArray<IrVariable>> scopes)
    {
        if (variable.Symbol is { } symbol)
        {
            CheckIdentity(symbol, variable, node);
        }

        var inScope = variable.Kind switch
        {
            IrVariableKind.Parameter => _method.Parameters.Contains(variable),
            IrVariableKind.This => variable == _method.This,
            _ => IsDeclaredIn(scopes, variable)
        };
        if (!inScope)
        {
            var number = variable.Kind == IrVariableKind.Temporary ? "I19" : "I20";
            Report(node, $"{number}: {variable.Name} is not declared in an enclosing scope of the method");
        }
    }

    private static bool IsDeclaredIn(ImmutableStack<ImmutableArray<IrVariable>> scopes, IrVariable variable)
    {
        foreach (var scope in scopes)
        {
            if (scope.Contains(variable))
            {
                return true;
            }
        }

        return false;
    }

    private void CheckIdentity(ISymbol symbol, IrVariable variable, IrNode node)
    {
        var instances = _variablesBySymbol.TryGetValue(symbol, out var known)
            ? known
            : ImmutableArray<IrVariable>.Empty;
        if (instances
            .AsStackEnumerable()
            .Any(variable, static (other, current) => other.Region == current.Region && other != current))
        {
            Report(node, "I9: two instances stand for the same variable");
        }
        else if (!instances.Contains(variable))
        {
            _variablesBySymbol[symbol] = instances.Add(variable);
        }
    }
}
