using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports a <c>Math.Clamp</c> whose <c>min</c> and <c>max</c> are constants with <c>min &gt; max</c> (GSH0122): .NET
/// throws <c>ArgumentException</c> then, so the call fails whenever it runs. Bounds known only at run time are checked
/// when the call runs (#718).
/// </summary>
internal sealed class MathClampBoundsRule : ISyntaxValidationRule<InvocationExpressionSyntax>
{
    public static readonly MathClampBoundsRule Instance = new();

    private const string ClampMethod = "Clamp";
    private const string MinParameter = "min";
    private const string MaxParameter = "max";

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.MathClampMinGreaterThanMax);

    private MathClampBoundsRule()
    {
    }

    public void Analyze(InvocationExpressionSyntax invocation, ref ValidationContext context)
    {
        // The call fails whenever it runs with both bounds constant (charter: certain at compile time)
        if (context.SemanticModel.GetSafeOperation(invocation) is not IInvocationOperation
            {
                TargetMethod: { Name: ClampMethod, ContainingType: { } owner }
            } call ||
            !owner.IsSystemMathType() ||
            FindConstant(call, MinParameter) is not { } min ||
            FindConstant(call, MaxParameter) is not { } max ||
            !IsGreater(min, max))
        {
            return;
        }

        context.ReportDiagnostic(
            GSharpDiagnostics.MathClampMinGreaterThanMax,
            invocation,
            Format(min),
            Format(max)
        );
    }

    private static object? FindConstant(IInvocationOperation call, string name)
    {
        foreach (var argument in call.Arguments)
        {
            if (argument.Parameter?.Name == name)
            {
                return argument.Value.ConstantValue is { HasValue: true, Value: { } value } ? value : null;
            }
        }

        return null;
    }

    // .NET compares with > : NaN on either side and -0.0 against 0.0 do not throw. A bound of another boxed type is not
    // reported
    private static bool IsGreater(object min, object max) => (min, max) switch
    {
        (double a, double b) => a > b,
        (float a, float b) => a > b,
        (ulong a, ulong b) => a > b,
        (uint a, uint b) => a > b,
        (long a, long b) => a > b,
        (int a, int b) => a > b,
        (short a, short b) => a > b,
        (sbyte a, sbyte b) => a > b,
        (ushort a, ushort b) => a > b,
        (byte a, byte b) => a > b,
        _ => false
    };

    private static string Format(object value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
}
