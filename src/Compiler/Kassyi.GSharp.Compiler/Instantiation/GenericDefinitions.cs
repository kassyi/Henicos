namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// The generic declarations of device functions, which G# does not compile as they are written: a type parameter has
/// no C++ type, and the lowering of a function that reaches a barrier cannot type the fields of its frame. Each instance
/// is to be compiled from its own closed copy instead (<see cref="GenericCopies"/>).
/// </summary>
/// <remarks>
/// A method that declares no type parameter of its own but is a member of a generic type is not one of them: such a
/// member that never names a type parameter of its type is emitted once today, with a managed version, and keeps that.
/// </remarks>
internal static class GenericDefinitions
{
    /// <summary>The reason that GSH0121 gives for the generic declaration of a device function.</summary>
    public const string NotSupportedReason = "generic device functions are not supported yet";

    /// <summary>Whether <paramref name="method"/> declares type parameters of its own.</summary>
    public static bool DeclaresTypeParameters(IMethodSymbol method) => method.OriginalDefinition.IsGenericMethod;

    /// <summary>
    /// Whether the generator leaves out the device function <paramref name="method"/>, whose body is in
    /// <paramref name="declaration"/>, and the analyzer reports GSH0121 for it: it declares type parameters
    /// (<see cref="DeclaresTypeParameters"/>) and is not an iterator, whose generic form GSH0070 already reports.
    /// </summary>
    public static bool IsLeftOut(IMethodSymbol method, MethodDeclarationSyntax declaration) =>
        DeclaresTypeParameters(method) && !IteratorTypeResolver.ContainsDirectYield(declaration.Body);
}
