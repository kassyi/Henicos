namespace Kassyi.GSharp.Compiler.Transpiler.Rules;

using System.Collections.Generic;
using DeclarationRules;
using ExpressionRules;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// Provides an O(1) array-based resolution registry for transpiler rules.
/// </summary>
public sealed class TranspilerRuleRegistry : ITranspilerRuleRegistry
{
    private readonly ISyntaxNodeTranspilerRule?[] _rules = new ISyntaxNodeTranspilerRule?[10000];

    /// <summary>
    /// Registers a transpilation rule for a specific syntax kind.
    /// </summary>
    public void Register(SyntaxKind kind, ISyntaxNodeTranspilerRule rule) => _rules[(int)kind] = rule;

    /// <summary>
    /// Registers a transpilation rule for multiple syntax kinds.
    /// </summary>
    public void Register(IEnumerable<SyntaxKind> kinds, ISyntaxNodeTranspilerRule rule)
    {
        foreach (var kind in kinds)
        {
            _rules[(int)kind] = rule;
        }
    }

    /// <inheritdoc />
    public ISyntaxNodeTranspilerRule? GetRule(SyntaxKind kind)
    {
        var index = (int)kind;
        return index < _rules.Length ? _rules[index] : null;
    }

    /// <summary>
    /// Creates and returns a new registry populated with all default C# syntax transpiler rules.
    /// </summary>
    public static TranspilerRuleRegistry CreateDefault()
    {
        var registry = new TranspilerRuleRegistry();

        // Expressions
        registry.Register(SyntaxKind.Argument, new ArgumentTranspilerRule());
        registry.Register([
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxKind.AddAssignmentExpression,
            SyntaxKind.SubtractAssignmentExpression,
            SyntaxKind.MultiplyAssignmentExpression,
            SyntaxKind.DivideAssignmentExpression,
            SyntaxKind.ModuloAssignmentExpression,
            SyntaxKind.AndAssignmentExpression,
            SyntaxKind.ExclusiveOrAssignmentExpression,
            SyntaxKind.OrAssignmentExpression,
            SyntaxKind.LeftShiftAssignmentExpression,
            SyntaxKind.RightShiftAssignmentExpression,
            SyntaxKind.CoalesceAssignmentExpression
        ], new AssignmentExpressionTranspilerRule());
        registry.Register([
            SyntaxKind.AddExpression,
            SyntaxKind.SubtractExpression,
            SyntaxKind.MultiplyExpression,
            SyntaxKind.DivideExpression,
            SyntaxKind.ModuloExpression,
            SyntaxKind.LeftShiftExpression,
            SyntaxKind.RightShiftExpression,
            SyntaxKind.LogicalOrExpression,
            SyntaxKind.LogicalAndExpression,
            SyntaxKind.BitwiseOrExpression,
            SyntaxKind.BitwiseAndExpression,
            SyntaxKind.ExclusiveOrExpression,
            SyntaxKind.EqualsExpression,
            SyntaxKind.NotEqualsExpression,
            SyntaxKind.LessThanExpression,
            SyntaxKind.LessThanOrEqualExpression,
            SyntaxKind.GreaterThanExpression,
            SyntaxKind.GreaterThanOrEqualExpression,
            SyntaxKind.AsExpression,
            SyntaxKind.IsExpression,
            SyntaxKind.CoalesceExpression
        ], new BinaryExpressionTranspilerRule());
        registry.Register(SyntaxKind.CastExpression, new CastExpressionTranspilerRule());
        registry.Register(SyntaxKind.ConditionalExpression, new ConditionalExpressionTranspilerRule());
        registry.Register(SyntaxKind.ElementAccessExpression, new ElementAccessExpressionTranspilerRule());
        registry.Register(SyntaxKind.InvocationExpression, new InvocationExpressionTranspilerRule());
        registry.Register([
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.AnonymousMethodExpression
        ], new LambdaExpressionTranspilerRule());
        registry.Register([
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxKind.PointerMemberAccessExpression
        ], new MemberAccessExpressionTranspilerRule());
        registry.Register(SyntaxKind.ObjectCreationExpression, new ObjectCreationExpressionTranspilerRule());
        registry.Register(SyntaxKind.ImplicitObjectCreationExpression,
            new ImplicitObjectCreationExpressionTranspilerRule());
        registry.Register(SyntaxKind.ParenthesizedExpression, new ParenthesizedExpressionTranspilerRule());
        registry.Register([
            SyntaxKind.PostIncrementExpression,
            SyntaxKind.PostDecrementExpression,
            SyntaxKind.SuppressNullableWarningExpression
        ], new PostfixUnaryExpressionTranspilerRule());
        registry.Register([
            SyntaxKind.UnaryPlusExpression,
            SyntaxKind.UnaryMinusExpression,
            SyntaxKind.BitwiseNotExpression,
            SyntaxKind.LogicalNotExpression,
            SyntaxKind.PreIncrementExpression,
            SyntaxKind.PreDecrementExpression,
            SyntaxKind.AddressOfExpression,
            SyntaxKind.PointerIndirectionExpression
        ], new PrefixUnaryExpressionTranspilerRule());
        registry.Register(SyntaxKind.SwitchExpression, new SwitchExpressionTranspilerRule());

        // Declarations
        registry.Register(SyntaxKind.LocalDeclarationStatement, new LocalDeclarationStatementTranspilerRule());
        registry.Register(SyntaxKind.VariableDeclaration, new VariableDeclarationTranspilerRule());

        return registry;
    }
}
