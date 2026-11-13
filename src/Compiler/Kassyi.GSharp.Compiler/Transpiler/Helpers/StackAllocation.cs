namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// A stack array that a <c>stackalloc</c> or a collection expression creates outside the declaration of a stack array
/// local: in an argument, in a pointer or <c>for</c> declaration, or nested in another expression.
/// </summary>
/// <param name="ElementType">The element type of the storage.</param>
/// <param name="Length">The constant element count.</param>
/// <param name="Elements">The initializer (<c>InitializerExpressionSyntax</c> or <c>CollectionExpressionSyntax</c>), or null.</param>
/// <param name="IsPointer">Whether the value is converted to a pointer (<c>int* p = stackalloc int[4]</c>).</param>
internal readonly record struct StackAllocation(
    ITypeSymbol ElementType,
    int Length,
    SyntaxNode? Elements,
    bool IsPointer);
