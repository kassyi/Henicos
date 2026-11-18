namespace Kassyi.GSharp.Compiler.Ir.Frames;

/// <summary>How a tracked variable is declared in the state-machine IR.</summary>
internal enum IrDeclarationForm
{
    /// <summary>A top-level <see cref="Statements.IrLocalDeclaration"/> in a state.</summary>
    Statement,

    /// <summary>An <see cref="Statements.IrLocalDeclaration"/> nested inside a structured statement.</summary>
    Nested,

    /// <summary>
    /// A declaring <see cref="Expressions.IrVariableReference"/> such as an <see langword="out"/> variable.
    /// </summary>
    Reference,

    /// <summary>The <see cref="Linear.IrResumeCall.ResultTarget"/> of a resuming call.</summary>
    ResumeResult
}
