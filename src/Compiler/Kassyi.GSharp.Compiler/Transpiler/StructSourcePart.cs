namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>One struct definition or <c>Dispose</c> definition, with the includes that writing it required.</summary>
/// <param name="Type">The struct.</param>
/// <param name="Text">The text.</param>
/// <param name="Includes">The <c>#include</c> targets that the text needs.</param>
/// <param name="Failed">
/// Whether writing it met a construct that could not be emitted; only this struct's record fails for it, not the
/// function or the struct whose transpilation wrote it.
/// </param>
/// <param name="InternalError">The message of the exception that stopped writing it, or an empty string.</param>
internal readonly record struct StructSourcePart(
    INamedTypeSymbol Type,
    string Text,
    EquatableArray<string> Includes,
    bool Failed,
    string InternalError);
