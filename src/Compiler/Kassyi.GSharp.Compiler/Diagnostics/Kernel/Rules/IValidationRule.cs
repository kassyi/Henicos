namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

internal interface IValidationRule
{
    ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
    {
        get;
    }
}

