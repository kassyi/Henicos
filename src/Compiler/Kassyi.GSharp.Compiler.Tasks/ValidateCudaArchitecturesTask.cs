namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>
/// MSBuild task that checks the <c>GSharpTargetArchitectures</c> entries without running nvcc, so that a malformed
/// entry fails the build also where the CUDA compilation is skipped. It logs nothing when every entry is valid.
/// </summary>
public class ValidateCudaArchitecturesTask : Microsoft.Build.Utilities.Task
{
    /// <summary>Gets or sets the value of <c>GSharpTargetArchitectures</c> (entries separated by <c>;</c>).</summary>
    public string TargetArchitectures { get; set; } = "";

    /// <inheritdoc />
    public override bool Execute() =>
        NvccArchitectures.ReportInvalidEntries(Log, NvccArchitectures.Parse(TargetArchitectures));
}
