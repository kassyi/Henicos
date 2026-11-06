using System.Globalization;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>Parses the arguments of the tool: <c>--option value</c> pairs and paths.</summary>
internal static class CommandLineParser
{
    /// <summary>Parses <paramref name="args"/>.</summary>
    /// <exception cref="UsageException">An option is unknown or lacks its value.</exception>
    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            switch (argument)
            {
                case "--help" or "-h":
                    options.Help = true;
                    break;
                case "--root":
                    options.Root = Value(args, ref i);
                    break;
                case "--config":
                    options.Config = Value(args, ref i);
                    break;
                case "--files-from":
                    options.FilesFrom = Value(args, ref i);
                    break;
                case "--out":
                    options.Out = Value(args, ref i);
                    break;
                case "--format":
                    AddFormats(options, Value(args, ref i));
                    break;
                case "--baseline":
                    options.Baseline = Value(args, ref i);
                    break;
                case "--changed":
                    options.Changed = Value(args, ref i);
                    break;
                case "--report":
                    options.Report = Report(Value(args, ref i));
                    break;
                case "--top":
                    options.Top = Number(Value(args, ref i));
                    break;
                case "--timing":
                    options.Timing = true;
                    break;
                case "--parallelism":
                    options.Parallelism = Number(Value(args, ref i));
                    break;
                case "--all":
                    break;
                default:
                    if (argument.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new UsageException($"Unknown option '{argument}'.");
                    }

                    options.Paths.Add(argument);
                    break;
            }
        }

        Validate(options);
        return options;
    }

    private static void Validate(CommandLineOptions options)
    {
        var sources = (options.FilesFrom is null ? 0 : 1) + (options.Changed is null ? 0 : 1) + (options.Paths.Count > 0 ? 1 : 0);
        if (sources > 1)
        {
            throw new UsageException("Give only one of paths, --files-from and --changed.");
        }

        if (options.Report == ReportMode.Metrics && options.Formats.Exists(static format => format != ReportFormat.Text))
        {
            throw new UsageException("A metrics listing is written as text only.");
        }

        if (options.Report == ReportMode.Metrics && options.Baseline is not null)
        {
            throw new UsageException("--baseline applies to --report findings.");
        }

        if (options.Formats.Count > 1 && options.Out is null)
        {
            throw new UsageException("Several formats need --out <directory> to write their files into.");
        }

        if (options.Formats.Count > 1 && options.Formats.Contains(ReportFormat.Github))
        {
            throw new UsageException("The github format is written alone, to standard output.");
        }

        if (options.Top is not null && options.Report != ReportMode.Metrics)
        {
            throw new UsageException("--top applies to --report metrics.");
        }
    }

    private static void AddFormats(CommandLineOptions options, string names)
    {
        foreach (var range in names.AsSpan().Split(','))
        {
            var name = names[range];
            var format = Format(name);
            if (options.Formats.Contains(format))
            {
                throw new UsageException($"The format '{name}' is given twice.");
            }

            options.Formats.Add(format);
        }
    }

    private static ReportFormat Format(string name) => name switch
    {
        "json" => ReportFormat.Json,
        "sarif" => ReportFormat.Sarif,
        "text" => ReportFormat.Text,
        "markdown" => ReportFormat.Markdown,
        "github" => ReportFormat.Github,
        _ => throw new UsageException($"Unsupported format '{name}'; use json, sarif, text, markdown or github."),
    };

    private static ReportMode Report(string name) => name switch
    {
        "findings" => ReportMode.Findings,
        "metrics" => ReportMode.Metrics,
        _ => throw new UsageException($"Unsupported report '{name}'; use findings or metrics."),
    };

    private static string Value(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new UsageException($"Option '{args[index]}' needs a value.");
        }

        index++;
        return args[index];
    }

    private static int Number(string text)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 1)
        {
            throw new UsageException($"'{text}' is not a positive integer.");
        }

        return value;
    }
}
