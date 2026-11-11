namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>What an <see cref="IrVariable"/> stands for.</summary>
internal enum IrVariableKind
{
    /// <summary>A local the user declared.</summary>
    Local,

    /// <summary>A parameter of the method.</summary>
    Parameter,

    /// <summary><see langword="this"/> of an instance member.</summary>
    This,

    /// <summary>A temporary that a pass of the compiler creates.</summary>
    Temporary
}
