using System.Globalization;
using System.Text;
using static Collector;

/// <summary>Formats the report as Markdown.</summary>
internal static class MarkdownReport
{
    public static string ToMarkdown(ReportModel m)
    {
        var sb = new StringBuilder();
        AppendHardware(sb, m);
        AppendGpus(sb, m);
        AppendToolchain(sb, m);
        AppendGSharp(sb, m);
        return sb.ToString();
    }

    private static void AppendHardware(StringBuilder sb, ReportModel m)
    {
        sb.Append("- OS: ").Append(m.Os).Append('\n');
        sb.Append("- CPU / RAM: ").Append(m.Cpu).Append(", ").Append(m.Memory).Append('\n');
        sb.Append("- Motherboard: ").Append(m.Motherboard).Append('\n');
    }

    private static void AppendGpus(StringBuilder sb, ReportModel m)
    {
        if (m.Gpus.Count == 0)
        {
            sb.Append("- GPU: ").Append(NotFound).Append('\n');
            return;
        }

        for (int i = 0; i < m.Gpus.Count; i++)
        {
            sb.Append("- GPU ")
                .Append(i.ToString(CultureInfo.InvariantCulture))
                .Append(": ")
                .Append(m.Gpus[i])
                .Append('\n');
        }
    }

    private static void AppendToolchain(StringBuilder sb, ReportModel m)
    {
        sb.Append("- CUDA Toolkit: ").Append(m.CudaToolkit).Append(" (CUDA_PATH ").Append(m.CudaPath).Append(")\n");
        sb.Append("- Host C++: ").Append(m.HostCpp).Append('\n');
        sb.Append("- .NET: SDK ").Append(m.DotnetSdk).Append(" (global.json: ").Append(m.GlobalJson).Append(")\n");
        sb.Append("  - SDKs: ").Append(Join(m.DotnetSdks)).Append('\n');
        sb.Append("  - Runtimes: ").Append(Join(m.DotnetRuntimes)).Append('\n');
    }

    private static void AppendGSharp(StringBuilder sb, ReportModel m)
    {
        if (m.GSharp is not null)
        {
            sb.Append("- G#: ").Append(m.GSharp).Append('\n');
        }

        sb.Append("- G# environment variables: ");
        bool first = true;
        foreach (KeyValuePair<string, string> pair in m.EnvironmentVariables)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(pair.Key).Append('=').Append(pair.Value);
        }

        sb.Append('\n');
    }

    private static string Join(List<string> items) => items.Count == 0 ? NotFound : string.Join(", ", items);
}
