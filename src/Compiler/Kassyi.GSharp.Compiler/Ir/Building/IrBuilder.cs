using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Building;

/// <summary>Builds the intermediate representation of a method body from Roslyn's <see cref="IOperation"/>.</summary>
/// <remarks>
/// The builder reads the operations of the shared semantic model (<see
/// cref="SemanticModelExtensions.GetCachedSemanticModel"/>),
/// which the generator and the analyzers have usually bound already. It makes the decisions that C# defines: the
/// argument of each parameter and the order of evaluation, the default values, the conversions, the value category of
/// each expression, one statement per declared local. What G# decides on top of C# is left to later passes.
/// </remarks>
internal static class IrBuilder
{
    /// <summary>
    /// Builds the body of <paramref name="method"/>, or returns <see langword="null"/> for a method without a body in
    /// source (extern, abstract, a partial method without an implementation, metadata).
    /// </summary>
    public static IrMethod? TryBuild(IMethodSymbol method, Compilation compilation, CancellationToken cancellationToken)
    {
        if (!method.TryGetDeclaringSyntax<SyntaxNode>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
        {
            return null;
        }

        var model = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        var body = model.GetOperation(declaration, cancellationToken) switch
        {
            IMethodBodyBaseOperation methodBody => methodBody.BlockBody ?? methodBody.ExpressionBody,
            ILocalFunctionOperation localFunction => localFunction.Body,
            IBlockOperation block => block,
            _ => null
        };
        if (body is null)
        {
            return null;
        }

        // The body of a partial method refers to the parameters of its implementing declaration
        method = method.PartialImplementationPart ?? method;
        var context = new IrBuildContext(compilation, method, declaration, cancellationToken);
        var parameters = ImmutableArray.CreateBuilder<IrVariable>(method.Parameters.Length);
        foreach (var parameter in method.Parameters)
        {
            parameters.Add(context.Variables.GetParameter(parameter));
        }

        var builtBody = IrStatementBuilder.BuildBlock(body, context);
        return new IrMethod(method, parameters.MoveToImmutable(), context.This, builtBody, context.DeclarationSource);
    }

    /// <summary>Builds one statement of a body, with the state of the build it belongs to.</summary>
    public static IrStatement BuildStatement(IOperation statement, IrBuildContext context) =>
        IrStatementBuilder.Build(statement, context);

    /// <summary>Builds one expression, used by its holder as <paramref name="category"/>.</summary>
    public static IrExpression BuildExpression(IOperation expression, IrCategory category, IrBuildContext context) =>
        IrExpressionBuilder.Build(expression, category, context);
}
