namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// How <c>new T(args)</c> of a struct reaches the device (<see cref="StructCreationClassifier.Classify"/>).
/// </summary>
internal enum StructCreationKind
{
    /// <summary>
    /// A user constructor with a body, or a primary constructor: <c>__gsharp_ctor_...(args)</c>
    /// (<see cref="StructConstructorEmitter"/>).
    /// </summary>
    Factory,

    /// <summary>
    /// A constructor that the dialect YAML maps to a function: <c>&lt;mapped&gt;(args)</c> (Vector3(float), ...).
    /// </summary>
    Mapped,

    /// <summary>
    /// Aggregate initialization <c>T{args}</c>: no arguments, or a constructor whose parameters are the members in
    /// order.
    /// </summary>
    Braces,

    /// <summary>
    /// Neither: reported (GSH0003), since <c>T{args}</c> would store the arguments in the members in order (#499).
    /// </summary>
    Unsupported
}
