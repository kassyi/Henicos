namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls;

/// <summary>
/// What a <c>fixed</c> declarator that pins through <c>GetPinnableReference</c> writes
/// (<see cref="PinnableReferenceMethod.Plan"/>).
/// </summary>
/// <param name="Method">The method, an extension method in its reduced form.</param>
/// <param name="Receiver">The expression of the initializer, which is the receiver of the call.</param>
/// <param name="PointerType">The pointer type of the declaration.</param>
/// <param name="CastsToPointer">
/// Whether the result is cast to the pointer type: a <c>ref readonly</c> result is a const reference in C++, which C#
/// pins as a pointer that may be written.
/// </param>
/// <param name="Form">How the receiver is passed.</param>
/// <param name="ReceiverType">The type of the extension's first parameter, which the local of a spill has.</param>
internal readonly record struct PinnablePlan(
    IMethodSymbol Method,
    ExpressionSyntax Receiver,
    ITypeSymbol? PointerType,
    bool CastsToPointer,
    PinnableReceiverForm Form,
    ITypeSymbol ReceiverType);
