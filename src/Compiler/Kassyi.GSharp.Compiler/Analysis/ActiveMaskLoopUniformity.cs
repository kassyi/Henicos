using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

// Whether every lane of a warp runs a loop the same number of times: a uniform loop needs no iteration key
internal static class ActiveMaskLoopUniformity
{
    internal static bool IsUniformLoop(
        LoopCandidate loop,
        UniformityAnalysis? uniformity,
        SemanticModel semanticModel)
    {
        if (uniformity == null)
        {
            return false;
        }

        if (loop.StructuredLoop is CommonForEachStatementSyntax topForEach &&
            uniformity.IsThreadVariant(topForEach.Expression))
        {
            return false;
        }

        foreach (var extentRoot in loop.GetExtentRoots())
        {
            if (extentRoot is StatementSyntax rootStatement &&
                ContainsNonUniformityInExtent(rootStatement, uniformity, semanticModel))
            {
                return false;
            }

            if (extentRoot is ExpressionSyntax rootExpression &&
                !RemovedCalls.IsRemovedCall(rootExpression, semanticModel))
            {
                if (((extentRoot.Parent is ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax &&
                      extentRoot == GetLoopCondition(extentRoot.Parent)) ||
                     (extentRoot.Parent is SwitchStatementSyntax switchParent &&
                      extentRoot == switchParent.Expression)) &&
                    uniformity.IsThreadVariant(rootExpression))
                {
                    return false;
                }

                if (IsConditionOrSourceThreadVariant(rootExpression, uniformity) ||
                    IsMethodWithBodyCall(rootExpression, semanticModel) ||
                    ContainsNonUniformityInNode(rootExpression, uniformity, semanticModel))
                {
                    return false;
                }
            }
        }

        return true;
    }

    internal static ExpressionSyntax? GetLoopCondition(SyntaxNode? loop) => loop switch
    {
        ForStatementSyntax f => f.Condition,
        WhileStatementSyntax w => w.Condition,
        DoStatementSyntax d => d.Condition,
        _ => null
    };

    internal static bool ContainsNonUniformityInExtent(
        StatementSyntax rootStatement,
        UniformityAnalysis uniformity,
        SemanticModel semanticModel)
    {
        if (IsConditionOrSourceThreadVariant(rootStatement, uniformity) ||
            IsMethodWithBodyCall(rootStatement, semanticModel))
        {
            return true;
        }

        return ContainsNonUniformityInNode(rootStatement, uniformity, semanticModel);
    }

    internal static bool ContainsNonUniformityInNode(
        SyntaxNode root,
        UniformityAnalysis uniformity,
        SemanticModel semanticModel)
    {
        foreach (var node in RemovedCalls.DescendantNodesThatRun(
                     root,
                     semanticModel,
                     static (n, model) => ActiveMaskIterationPlanner.IsNestedCallable(n) ||
                                          (n is InvocationExpressionSyntax inv && NameofOperator.Is(inv, model))))
        {
            if (IsConditionOrSourceThreadVariant(node, uniformity) ||
                IsMethodWithBodyCall(node, semanticModel))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsConditionOrSourceThreadVariant(SyntaxNode node, UniformityAnalysis uniformity) =>
        node switch
        {
            IfStatementSyntax ifStatement => uniformity.IsThreadVariant(ifStatement.Condition),
            WhileStatementSyntax whileStatement => uniformity.IsThreadVariant(whileStatement.Condition),
            DoStatementSyntax doStatement => uniformity.IsThreadVariant(doStatement.Condition),
            ForStatementSyntax { Condition: { } condition } => uniformity.IsThreadVariant(condition),
            CommonForEachStatementSyntax forEach => uniformity.IsThreadVariant(forEach.Expression),
            SwitchStatementSyntax switchStatement => uniformity.IsThreadVariant(switchStatement.Expression),
            SwitchExpressionSyntax switchExpression => uniformity.IsThreadVariant(switchExpression.GoverningExpression),
            ConditionalExpressionSyntax conditional => uniformity.IsThreadVariant(conditional.Condition),
            ConditionalAccessExpressionSyntax conditionalAccess => uniformity.IsThreadVariant(conditionalAccess.Expression),
            BinaryExpressionSyntax binary when binary.IsKind(
                SyntaxKind.LogicalAndExpression,
                SyntaxKind.LogicalOrExpression,
                SyntaxKind.CoalesceExpression) => uniformity.IsThreadVariant(binary.Left),
            AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) =>
                uniformity.IsThreadVariant(assignment.Left),
            IsPatternExpressionSyntax isPattern => uniformity.IsThreadVariant(isPattern.Expression),
            CasePatternSwitchLabelSyntax { WhenClause: { } whenClause } =>
                uniformity.IsThreadVariant(whenClause.Condition),
            SwitchExpressionArmSyntax { WhenClause: { } whenClause } =>
                uniformity.IsThreadVariant(whenClause.Condition),
            _ => false
        };

    internal static bool IsMethodWithBodyCall(SyntaxNode node, SemanticModel semanticModel)
    {
        var compilation = semanticModel.Compilation;
        if (node is InvocationExpressionSyntax invocation)
        {
            if (NameofOperator.Is(invocation, semanticModel))
            {
                return false;
            }

            if (semanticModel.GetSymbol(invocation) is not IMethodSymbol callee)
            {
                return true;
            }

            if (HasCallableBody(callee, compilation))
            {
                return true;
            }
        }

        if (node is ExpressionSyntax expression && expression is not ParenthesizedExpressionSyntax)
        {
            if (NameofOperator.Encloses(expression, semanticModel))
            {
                return false;
            }

            var conversion = semanticModel.GetSafeConversion(expression);
            if (conversion is { IsUserDefined: true, MethodSymbol: { } convMethod } &&
                HasCallableBody(convMethod, compilation))
            {
                return true;
            }
        }

        if (node is UsingStatementSyntax usingStatement)
        {
            var typeNode = (SyntaxNode?)usingStatement.Declaration?.Type ?? usingStatement.Expression;
            if (typeNode != null && UsingHasDisposableBody(typeNode, semanticModel))
            {
                return true;
            }
        }

        if (node is LocalDeclarationStatementSyntax { UsingKeyword.RawKind: (int)SyntaxKind.UsingKeyword } localUsing &&
            UsingHasDisposableBody(localUsing.Declaration.Type, semanticModel))
        {
            return true;
        }

        if (node is VariableDeclaratorSyntax
            {
                Parent.Parent: FixedStatementSyntax, Initializer.Value: { } fixedInit
            } &&
            PinnableReferenceLookup.Find(fixedInit, semanticModel) is { } pinnable &&
            HasCallableBody(pinnable.GetConstructedReducedFrom() ?? pinnable, compilation))
        {
            return true;
        }

        return semanticModel.GetSafeOperation(node) switch
        {
            IPropertyReferenceOperation { Property: var prop } =>
                (EmittedUserTypes.IsEmittedUserType(prop.ContainingType) &&
                 StructAbiMembers.HasAccessorFunctions(prop)) ||
                (prop.GetMethod is { } getter && HasCallableBody(getter, compilation)) ||
                (prop.SetMethod is { } setter && HasCallableBody(setter, compilation)),
            IObjectCreationOperation { Constructor: { IsImplicitlyDeclared: false } ctor } =>
                HasCallableBody(ctor, compilation),
            IBinaryOperation { OperatorMethod: { } opMethod } =>
                HasCallableBody(opMethod, compilation),
            IUnaryOperation { OperatorMethod: { } opMethod } =>
                HasCallableBody(opMethod, compilation),
            IIncrementOrDecrementOperation { OperatorMethod: { } opMethod } =>
                HasCallableBody(opMethod, compilation),
            ICompoundAssignmentOperation { OperatorMethod: { } opMethod } =>
                HasCallableBody(opMethod, compilation),
            IConversionOperation { OperatorMethod: { } opMethod } =>
                HasCallableBody(opMethod, compilation),
            _ => false
        };
    }

    internal static bool UsingHasDisposableBody(SyntaxNode typeOrExpression, SemanticModel semanticModel)
    {
        var type = semanticModel.GetTypeSymbol(typeOrExpression);
        return type is INamedTypeSymbol named &&
               EmittedUserTypes.IsEmittedUserType(named) &&
               UsingDisposeLookup.Find(named, semanticModel.Compilation) is { } dispose &&
               HasCallableBody(dispose, semanticModel.Compilation);
    }

    internal static bool HasCallableBody(IMethodSymbol callee, Compilation compilation)
    {
        if (callee.MethodKind is MethodKind.LocalFunction or MethodKind.DelegateInvoke or
            MethodKind.AnonymousFunction)
        {
            return true;
        }

        if (GpuMacroClassifier.Classify(callee).HasBody)
        {
            return true;
        }

        if (callee.HasAttribute(KnownAttributes.GpuDeviceFunction) ||
            DeviceExportTable.TryFind(callee, out _))
        {
            return true;
        }

        return DeviceFunctionBodies.TryGet(callee, compilation, out var bodies) && bodies.Length > 0;
    }
}
