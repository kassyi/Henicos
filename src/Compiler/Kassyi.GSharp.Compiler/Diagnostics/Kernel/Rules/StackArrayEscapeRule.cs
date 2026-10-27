namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports a use that lets a stack array outlive the C++ scope that holds it (GSH0091). G# keeps a local array
/// (<c>int[] a = new int[4]</c>, an array initializer, a collection expression), the stack array of a span
/// (<c>Span&lt;int&gt; s = stackalloc int[4]</c>) and the storage of a <c>stackalloc</c> in a C++ array of the innermost
/// scope around it, where C# keeps the array on the heap or until the method returns.
/// </summary>
/// <remarks>
/// <para>
/// Each local (and each value parameter of a local function or lambda the method calls) gets the set of storages it
/// may point into, propagated from declarations, assignments, the arguments of those calls and the <c>ref</c> /
/// <c>out</c> arguments of every call until nothing changes (the safe-context of a local in Roslyn's
/// <c>RefSafetyAnalysis</c>, with the scopes of the emitted C++ in place of C#'s). A value refers to a storage only
/// through the positions that carry a reference: a stack array local, a <c>stackalloc</c>, a local that holds one, and,
/// for a value whose type is an array, a span, a pointer or a struct that holds one, the operands, receivers and
/// arguments of such a type. An index, a length or an argument of another type carries none.
/// </para>
/// <para>
/// Reported: reassigning a stack array local or passing it by <c>ref</c> / <c>out</c>; returning a value that refers to a
/// stack array (<c>return ref a[0]</c> included); storing it where it outlives the storage (a local of an outer scope, a
/// parameter, a field or element of something that is not such a local, a <c>ref</c> local, <c>*p</c>); and a call whose
/// <c>ref</c> / <c>out</c> argument outlives a storage that another argument refers to, which the callee may store there
/// (the "method arguments must match" rule of C#).
/// </para>
/// <para>
/// A scope is the innermost block, switch section, or body statement of <c>if</c> / <c>else</c> / <c>for</c> /
/// <c>foreach</c> / <c>while</c> / <c>do</c> / <c>using</c> / <c>fixed</c> / <c>lock</c> (the emitted C++ braces each of
/// them; <c>for</c> also scopes its own declarations), and a lambda or a local function. The emitted C++ flattens a nested
/// block, which only makes a storage live longer, so a check on the C# blocks stays sound.
/// </para>
/// </remarks>
internal sealed class StackArrayEscapeRule : ISyntaxValidationRule<MethodDeclarationSyntax>
{
    public static readonly StackArrayEscapeRule Instance = new();

    private const string StackAllocName = "stackalloc";
    private const string CollectionExpressionName = "collection expression";
    private const string Reassigned = "reassigned";
    private const string PassedByReference = "passed by ref or out";
    private const string Returned = "returned";
    private const string StoredOutside = "stored in a field, an element, a parameter or a ref local";
    private const string AssignedToOuterVariable = "assigned to a variable declared outside its scope";
    private const string PassedWithLongerLivedReference = "passed to a call with a ref or out argument that outlives it";

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.StackArrayEscapes);

    private StackArrayEscapeRule()
    {
    }

    public void Analyze(MethodDeclarationSyntax node, ref ValidationContext context)
    {
        if (node.GetBodyNode() is not { } body)
        {
            return;
        }

        var findings = new Analysis(context.SemanticModel).Run(body);
        foreach (var finding in findings)
        {
            context.ReportDiagnostic(GSharpDiagnostics.StackArrayEscapes, finding.Node, finding.Name, finding.Reason);
        }
    }

    private readonly record struct Storage(SyntaxNode Scope, string Name);

    private readonly record struct Finding(SyntaxNode Node, string Name, string Reason);

    private sealed class Analysis(SemanticModel model)
    {
        // The storages that a local or a value parameter may point into
        private readonly Dictionary<ISymbol, List<Storage>> _held = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ITypeSymbol, bool> _carriers = new(SymbolEqualityComparer.Default);
        private readonly List<VariableDeclaratorSyntax> _declarations = [];
        private readonly List<AssignmentExpressionSyntax> _assignments = [];
        private readonly List<ArgumentListSyntax> _callsWithReferences = [];
        private readonly List<InvocationExpressionSyntax> _calls = [];
        private readonly List<ExpressionSyntax> _returns = [];

        public List<Finding> Run(SyntaxNode body)
        {
            Collect(body);
            Propagate();

            var findings = new List<Finding>();
            foreach (var assignment in _assignments)
            {
                CheckAssignment(assignment, findings);
            }

            foreach (var arguments in _callsWithReferences)
            {
                CheckCall(arguments, findings);
            }

            foreach (var value in _returns)
            {
                if (TryFirst(StorageOf(value), out var storage))
                {
                    findings.Add(new Finding(value, storage.Name, Returned));
                }
            }

            findings.Sort(static (x, y) => x.Node.SpanStart.CompareTo(y.Node.SpanStart));
            return findings;
        }

        private void Collect(SyntaxNode body)
        {
            if (body is ArrowExpressionClauseSyntax arrow)
            {
                _returns.Add(arrow.Expression);
            }

            foreach (var node in body.DescendantNodes())
            {
                switch (node)
                {
                    case VariableDeclaratorSyntax { Initializer: not null } declarator:
                        _declarations.Add(declarator);
                        break;
                    // A member of an object initializer is part of the value it initializes (Gather)
                    case AssignmentExpressionSyntax { Parent: not InitializerExpressionSyntax } assignment:
                        _assignments.Add(assignment);
                        break;
                    case ArgumentListSyntax arguments when arguments.Arguments
                        .AsStackEnumerable()
                        .Any(static a => a.RefKindKeyword.IsKind(SyntaxKind.RefKeyword, SyntaxKind.OutKeyword)):
                        _callsWithReferences.Add(arguments);
                        break;
                    case ReturnStatementSyntax { Expression: { } value }:
                        _returns.Add(value);
                        break;
                    case InvocationExpressionSyntax invocation:
                        _calls.Add(invocation);
                        break;
                    case ArrowExpressionClauseSyntax { Parent: LocalFunctionStatementSyntax } localArrow:
                        _returns.Add(localArrow.Expression);
                        break;
                    case AnonymousFunctionExpressionSyntax { ExpressionBody: { } lambdaValue }:
                        _returns.Add(lambdaValue);
                        break;
                }
            }
        }

        // Spreads the storages through the locals until no local gets a new one
        private void Propagate()
        {
            bool changed;
            do
            {
                changed = false;
                foreach (var declarator in _declarations)
                {
                    if (model.GetDeclaredSymbol(declarator) is ILocalSymbol local &&
                        declarator.Initializer is { } initializer)
                    {
                        changed |= Hold(local, StorageOf(initializer.Value));
                    }
                }

                foreach (var assignment in _assignments)
                {
                    foreach (var (target, value) in Pairs(assignment))
                    {
                        if (RootLocal(target) is { } local)
                        {
                            changed |= Hold(local, StorageOf(value));
                        }
                    }
                }

                // The value parameters of a local function or lambda that the method calls hold what the arguments refer
                // to, so that a write to a captured variable in its body is checked like one in the method
                foreach (var invocation in _calls)
                {
                    if (CalleeOfLocalCode(invocation) is not { } callee)
                    {
                        continue;
                    }

                    var arguments = invocation.ArgumentList.Arguments;
                    for (var i = 0; i < arguments.Count; i++)
                    {
                        if (callee.FindParameter(arguments, i) is { RefKind: RefKind.None } parameter)
                        {
                            changed |= Hold(parameter, StorageOf(arguments[i].Expression));
                        }
                    }
                }

                foreach (var arguments in _callsWithReferences)
                {
                    var passed = StoragesOfArguments(arguments, except: null);
                    foreach (var argument in arguments.Arguments)
                    {
                        if (IsWritableReference(argument) && RootLocal(argument.Expression) is { } local)
                        {
                            changed |= Hold(local, passed);
                        }
                    }
                }
            }
            while (changed);
        }

        private bool Hold(ISymbol local, List<Storage> storages)
        {
            if (storages.Count == 0)
            {
                return false;
            }

            if (!_held.TryGetValue(local, out var held))
            {
                held = [];
                _held.Add(local, held);
            }

            var added = false;
            foreach (var storage in storages)
            {
                if (!held
                        .AsStackEnumerable()
                        .Any(storage.Scope, static (h, scope) => h.Scope == scope))
                {
                    held.Add(storage);
                    added = true;
                }
            }

            return added;
        }

        private void CheckAssignment(AssignmentExpressionSyntax assignment, List<Finding> findings)
        {
            foreach (var (target, value) in Pairs(assignment))
            {
                if (LocalOf(Unwrap(target)) is { } targetLocal && IsStackArrayLocal(targetLocal))
                {
                    findings.Add(new Finding(assignment, targetLocal.Name, Reassigned));
                    return;
                }

                if (FindOutlived(StorageOf(value), TargetScope(target), out var storage))
                {
                    var reason = LocalOf(Unwrap(target)) is { IsRef: false }
                        ? AssignedToOuterVariable
                        : StoredOutside;
                    findings.Add(new Finding(assignment, storage.Name, reason));
                    return;
                }
            }
        }

        private void CheckCall(ArgumentListSyntax arguments, List<Finding> findings)
        {
            foreach (var argument in arguments.Arguments)
            {
                if (!IsWritableReference(argument))
                {
                    continue;
                }

                if (model.GetSymbol(argument.Expression) is ILocalSymbol local && IsStackArrayLocal(local))
                {
                    findings.Add(new Finding(argument, local.Name, PassedByReference));
                    continue;
                }

                if (IsCarrier(TypeOf(argument.Expression)) &&
                    FindOutlived(StoragesOfArguments(arguments, except: argument), TargetScope(argument.Expression),
                        out var storage))
                {
                    findings.Add(new Finding(argument, storage.Name, PassedWithLongerLivedReference));
                }
            }
        }

        // What a callee may store into a ref / out argument: any argument that carries a reference
        private List<Storage> StoragesOfArguments(ArgumentListSyntax arguments, ArgumentSyntax? except)
        {
            var storages = new List<Storage>();
            foreach (var argument in arguments.Arguments)
            {
                if (argument != except && !argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
                {
                    Gather(argument.Expression, storages, address: false);
                }
            }

            return storages;
        }

        // The first storage that a target of the given scope outlives; a null scope is outside the method
        private static bool FindOutlived(List<Storage> storages, SyntaxNode? targetScope, out Storage storage)
        {
            foreach (var candidate in storages)
            {
                if (targetScope == null || IsProperAncestor(targetScope, candidate.Scope))
                {
                    storage = candidate;
                    return true;
                }
            }

            storage = default;
            return false;
        }

        private List<Storage> StorageOf(ExpressionSyntax value)
        {
            var storages = new List<Storage>();
            Gather(value, storages, address: false);
            return storages;
        }

        // Adds the storages that value refers to, following only the positions that carry a reference; with address,
        // value is the operand of & or ref, which refers to where the variable or element lives
        private void Gather(ExpressionSyntax value, List<Storage> storages, bool address)
        {
            // A ref expression has the type of the variable, which is often not a carrier (return ref a[0])
            if (!address && value is not RefExpressionSyntax && !IsCarrier(TypeOf(value)))
            {
                return;
            }

            switch (value)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    Gather(parenthesized.Expression, storages, address);
                    break;
                case CastExpressionSyntax cast:
                    Gather(cast.Expression, storages, address);
                    break;
                case CheckedExpressionSyntax checkedExpression:
                    Gather(checkedExpression.Expression, storages, address);
                    break;
                case ConditionalExpressionSyntax conditional:
                    Gather(conditional.WhenTrue, storages, address);
                    Gather(conditional.WhenFalse, storages, address);
                    break;
                case BinaryExpressionSyntax binary:
                    Gather(binary.Left, storages, address: false);
                    Gather(binary.Right, storages, address: false);
                    break;
                case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.AddressOfExpression):
                    Gather(prefix.Operand, storages, address: true);
                    break;
                case PrefixUnaryExpressionSyntax prefix:
                    Gather(prefix.Operand, storages, address: false);
                    break;
                case PostfixUnaryExpressionSyntax postfix:
                    Gather(postfix.Operand, storages, address: false);
                    break;
                case RefExpressionSyntax refExpression:
                    Gather(refExpression.Expression, storages, address: true);
                    break;
                case IdentifierNameSyntax identifier:
                    GatherLocal(identifier, storages, address);
                    break;
                case StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax:
                    storages.Add(new Storage(GetScope(value), StackAllocName));
                    break;
                case CollectionExpressionSyntax when model.GetTypeInfo(value).ConvertedType.IsArrayLikeType() &&
                                                     model.GetTypeInfo(value).ConvertedType is not IArrayTypeSymbol:
                    storages.Add(new Storage(GetScope(value), CollectionExpressionName));
                    break;
                case InvocationExpressionSyntax invocation:
                    if (invocation.Expression is MemberAccessExpressionSyntax method)
                    {
                        Gather(method.Expression, storages, address: false);
                    }

                    foreach (var argument in invocation.ArgumentList.Arguments)
                    {
                        Gather(argument.Expression, storages, address: false);
                    }

                    break;
                case MemberAccessExpressionSyntax member:
                    Gather(member.Expression, storages, address);
                    break;
                case ElementAccessExpressionSyntax element:
                    Gather(element.Expression, storages, address: false);
                    break;
                case ConditionalAccessExpressionSyntax conditionalAccess:
                    Gather(conditionalAccess.Expression, storages, address: false);
                    break;
                case BaseObjectCreationExpressionSyntax creation:
                    if (creation.ArgumentList is { } creationArguments)
                    {
                        foreach (var argument in creationArguments.Arguments)
                        {
                            Gather(argument.Expression, storages, address: false);
                        }
                    }

                    if (creation.Initializer is { } initializer)
                    {
                        foreach (var expression in initializer.Expressions)
                        {
                            Gather(
                                expression is AssignmentExpressionSyntax memberValue ? memberValue.Right : expression,
                                storages,
                                address: false
                            );
                        }
                    }

                    break;
                case TupleExpressionSyntax tuple:
                    foreach (var argument in tuple.Arguments)
                    {
                        Gather(argument.Expression, storages, address: false);
                    }

                    break;
                case SwitchExpressionSyntax switchExpression:
                    foreach (var arm in switchExpression.Arms)
                    {
                        Gather(arm.Expression, storages, address: false);
                    }

                    break;
                case AssignmentExpressionSyntax assignment:
                    Gather(assignment.Right, storages, address: false);
                    break;
            }
        }

        private void GatherLocal(IdentifierNameSyntax identifier, List<Storage> storages, bool address)
        {
            var symbol = model.GetSymbol(identifier);
            if (symbol is ILocalSymbol local && StackArrayLocals.IsStackArray(local, out var declarator))
            {
                storages.Add(new Storage(GetScope(declarator), local.Name));
            }

            // The address of a local that only holds a reference points at the local, not into a storage
            if (!address && symbol is ILocalSymbol or IParameterSymbol { RefKind: RefKind.None } &&
                _held.TryGetValue(symbol, out var held))
            {
                storages.AddRange(held);
            }
        }

        // The local function or lambda that an invocation runs, when the method declares it: a local function, or a
        // delegate local initialized with a lambda
        private IMethodSymbol? CalleeOfLocalCode(InvocationExpressionSyntax invocation)
        {
            switch (model.GetSymbol(invocation))
            {
                case IMethodSymbol { MethodKind: MethodKind.LocalFunction } localFunction:
                    return localFunction;
                case IMethodSymbol { MethodKind: MethodKind.DelegateInvoke }
                    when model.GetSymbol(invocation.Expression) is ILocalSymbol delegateLocal &&
                         delegateLocal.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var declarator) &&
                         declarator.Initializer?.Value is AnonymousFunctionExpressionSyntax lambda:
                    return model.GetSymbol(lambda) as IMethodSymbol;
                default:
                    return null;
            }
        }

        // The variable that a write to target changes: the local or value parameter itself, or the struct local whose
        // field it is
        private ISymbol? RootLocal(SyntaxNode target)
        {
            var current = Unwrap(target);
            while (current is MemberAccessExpressionSyntax member &&
                   TypeOf(member.Expression) is { IsValueType: true })
            {
                current = Unwrap(member.Expression);
            }

            if (LocalOf(current) is { IsRef: false } local)
            {
                return local;
            }

            return model.GetSymbol(current) is IParameterSymbol { RefKind: RefKind.None } parameter ? parameter : null;
        }

        // The scope that a write to target lives in, or null for memory outside the method (a parameter passed by
        // reference, a field of a reference, an element of an array the method did not create, a ref local, *p)
        private SyntaxNode? TargetScope(SyntaxNode target)
        {
            var current = Unwrap(target);
            switch (current)
            {
                case SingleVariableDesignationSyntax designation:
                    return GetScope(designation);
                case MemberAccessExpressionSyntax member when TypeOf(member.Expression) is { IsValueType: true }:
                    return TargetScope(member.Expression);
                case ElementAccessExpressionSyntax element:
                    return TryFirst(StorageOf(element.Expression), out var storage) ? storage.Scope : null;
            }

            return model.GetSymbol(current) switch
            {
                ILocalSymbol { IsRef: false } local when local.TryGetDeclaringSyntax<SyntaxNode>(out var declaration) =>
                    declaration is CommonForEachStatementSyntax ? declaration : GetScope(declaration),
                IParameterSymbol { RefKind: RefKind.None } parameter
                    when parameter.TryGetDeclaringSyntax<SyntaxNode>(out var declaration) => BodyScope(declaration),
                _ => null
            };
        }

        // The (target, value) pairs of an assignment: a tuple or a deconstructing declaration on the left pairs element
        // by element; a target is an expression or the designation of a declared local
        private static List<(SyntaxNode Target, ExpressionSyntax Value)> Pairs(AssignmentExpressionSyntax assignment)
        {
            var pairs = new List<(SyntaxNode, ExpressionSyntax)>();
            AddPairs(assignment.Left, assignment.Right, pairs);
            return pairs;

            static void AddPairs(SyntaxNode target, ExpressionSyntax value, List<(SyntaxNode, ExpressionSyntax)> pairs)
            {
                switch (target)
                {
                    case TupleExpressionSyntax targets:
                        for (var i = 0; i < targets.Arguments.Count; i++)
                        {
                            AddPairs(targets.Arguments[i].Expression, ElementOf(value, i), pairs);
                        }

                        break;
                    case DeclarationExpressionSyntax declaration:
                        AddPairs(declaration.Designation, value, pairs);
                        break;
                    case ParenthesizedVariableDesignationSyntax designations:
                        for (var i = 0; i < designations.Variables.Count; i++)
                        {
                            AddPairs(designations.Variables[i], ElementOf(value, i), pairs);
                        }

                        break;
                    case DiscardDesignationSyntax:
                        break;
                    default:
                        pairs.Add((target, value));
                        break;
                }
            }

            static ExpressionSyntax ElementOf(ExpressionSyntax value, int index) =>
                value is TupleExpressionSyntax values && index < values.Arguments.Count
                    ? values.Arguments[index].Expression
                    : value;
        }

        private static bool IsWritableReference(ArgumentSyntax argument) =>
            argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword, SyntaxKind.OutKeyword);

        // A T[] stack array local is a C++ array, which cannot be assigned or passed by reference
        private static bool IsStackArrayLocal(ILocalSymbol local) =>
            local.Type is IArrayTypeSymbol && StackArrayLocals.IsStackArray(local, out _);

        private static SyntaxNode Unwrap(SyntaxNode node)
        {
            while (true)
            {
                switch (node)
                {
                    case ParenthesizedExpressionSyntax parenthesized:
                        node = parenthesized.Expression;
                        break;
                    case DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax designation }:
                        return designation;
                    default:
                        return node;
                }
            }
        }

        // The local that a name or a designation stands for
        private ILocalSymbol? LocalOf(SyntaxNode node) => node is SingleVariableDesignationSyntax designation
            ? model.GetDeclaredSymbol(designation) as ILocalSymbol
            : model.GetSymbol(node) as ILocalSymbol;

        private ITypeSymbol? TypeOf(ExpressionSyntax expression)
        {
            var info = model.GetTypeInfo(expression);
            return info.ConvertedType ?? info.Type;
        }

        // An array, a span, a pointer, or a struct with a field of such a type (tuples included) can refer to a storage
        private bool IsCarrier(ITypeSymbol? type, int depth = 0)
        {
            switch (type)
            {
                case null:
                    return false;
                case IArrayTypeSymbol or IPointerTypeSymbol:
                    return true;
            }

            if (type.IsArrayLikeType())
            {
                return true;
            }

            if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } || depth > 3)
            {
                return false;
            }

            if (_carriers.TryGetValue(type, out var known))
            {
                return known;
            }

            _carriers[type] = false;
            var carries = false;
            foreach (var member in type.GetMembers())
            {
                if (member is IFieldSymbol { IsStatic: false } field && IsCarrier(field.Type, depth + 1))
                {
                    carries = true;
                    break;
                }
            }

            _carriers[type] = carries;
            return carries;
        }

        private static bool TryFirst(List<Storage> storages, out Storage storage)
        {
            storage = storages.Count > 0 ? storages[0] : default;
            return storages.Count > 0;
        }

        // The innermost node whose end ends the C++ lifetime of what is declared at or allocated by node
        private static SyntaxNode GetScope(SyntaxNode node)
        {
            var current = node;
            for (var parent = node.Parent; parent != null; current = parent, parent = parent.Parent)
            {
                switch (parent)
                {
                    case ForStatementSyntax forStatement:
                        return forStatement.Statement == current ? current : parent;
                    case BlockSyntax or SwitchSectionSyntax:
                        return parent;
                    case IfStatementSyntax ifStatement when ifStatement.Statement == current:
                    case ElseClauseSyntax elseClause when elseClause.Statement == current:
                    case CommonForEachStatementSyntax forEach when forEach.Statement == current:
                    case WhileStatementSyntax whileStatement when whileStatement.Statement == current:
                    case DoStatementSyntax doStatement when doStatement.Statement == current:
                    case UsingStatementSyntax usingStatement when usingStatement.Statement == current:
                    case FixedStatementSyntax fixedStatement when fixedStatement.Statement == current:
                    case LockStatementSyntax lockStatement when lockStatement.Statement == current:
                        return current;
                    case AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax
                        or AccessorDeclarationSyntax:
                        return parent;
                }
            }

            return current;
        }

        // A value parameter lives as long as the body of its function: the scope of the locals declared at its top
        private static SyntaxNode BodyScope(SyntaxNode parameter)
        {
            var function = GetScope(parameter);
            return function switch
            {
                BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody ?? function,
                LocalFunctionStatementSyntax localFunction =>
                    (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody ?? function,
                AnonymousFunctionExpressionSyntax lambda => lambda.Body,
                _ => function
            };
        }

        private static bool IsProperAncestor(SyntaxNode outer, SyntaxNode inner)
        {
            for (var current = inner.Parent; current != null; current = current.Parent)
            {
                if (current == outer)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
