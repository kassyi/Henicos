namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>Where the value of an <see cref="IrArgument"/> comes from.</summary>
internal enum IrArgumentKind
{
    /// <summary>The caller wrote the argument.</summary>
    Explicit,

    /// <summary>The caller left out an optional parameter; the value is its default.</summary>
    DefaultValue,

    /// <summary>The arguments of a <see langword="params"/> parameter, collected into one array.</summary>
    ParamArray
}
