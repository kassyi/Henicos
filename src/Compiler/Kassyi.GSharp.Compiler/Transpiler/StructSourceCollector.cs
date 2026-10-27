namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Receives, struct by struct, the struct definitions and <c>Dispose</c> definitions that a method transpilation writes
/// in front of the method, so that a device code export can record each struct once instead of inside every function.
/// </summary>
/// <remarks>
/// The text the method returns does not change: each part in <see cref="Definitions"/> and <see cref="Disposes"/> is
/// also written where it always was. <see cref="Discovered"/> holds the structs that the definitions name without the
/// method's text defining them (a constructor parameter, an operator's result), which only the export needs.
/// </remarks>
internal sealed class StructSourceCollector
{
    /// <summary>The definitions (<c>StructTranspiler.WriteStructDefinition</c>) written in front of the method, in order.</summary>
    public ImmutableArray<StructSourcePart>.Builder Definitions { get; } = ImmutableArray.CreateBuilder<StructSourcePart>();

    /// <summary>The <c>Dispose</c> definitions (<c>UsingTranspiler.WriteDisposeDefinitions</c>) written in front of the method.</summary>
    public ImmutableArray<StructSourcePart>.Builder Disposes { get; } = ImmutableArray.CreateBuilder<StructSourcePart>();

    /// <summary>The definitions of the structs that the definitions name and the method's text does not define.</summary>
    public ImmutableArray<StructSourcePart>.Builder Discovered { get; } = ImmutableArray.CreateBuilder<StructSourcePart>();

    /// <summary>Whether a definition of <paramref name="type"/> was collected.</summary>
    public bool HasDefinition(INamedTypeSymbol type) =>
        Definitions
            .AsStackEnumerable()
            .Any(type, static (part, t) => part.Type.IsEqualTo(t)) ||
        Discovered
            .AsStackEnumerable()
            .Any(type, static (part, t) => part.Type.IsEqualTo(t));
}

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

/// <summary>Whether a part met a construct that could not be emitted, set by an intercepting reporter.</summary>
internal sealed class PartFailure
{
    /// <summary>Whether a failure was reported.</summary>
    public bool Failed { get; private set; }

    /// <summary>Records a failure.</summary>
    public void Mark() => Failed = true;

    /// <summary>The message of an exception that a record keeps (its type and message, without the stack).</summary>
    public static string Describe(Exception exception) => exception.GetType().Name + ": " + exception.Message;
}
