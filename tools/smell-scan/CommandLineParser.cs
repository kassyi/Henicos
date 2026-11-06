using System.Globalization;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>Parses the arguments of the tool: <c>--option value</c> pairs and paths.</summary>
internal static class CommandLineParser
{
    /// <summary>Parses <paramref name="args"/>.</summary>
    /// <exception cref="ArgumentException">An option is unknown or lacks its value.</exception>
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
                    var format = Value(args, ref i);
                    if (!string.Equals(format, "json", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Unsupported format '{format}'; only 'json' is available.");
                    }

                    break;
                case "--parallelism":
                    options.Parallelism = Number(Value(args, ref i));
                    break;
                case "--all":
                    break;
                default:
                    if (argument.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Unknown option '{argument}'.");
                    }

                    options.Paths.Add(argument);
                    break;
            }
        }

        return options;
    }

    private static string Value(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Option '{args[index]}' needs a value.");
        }

        index++;
        return args[index];
    }

    private static int Number(string text)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 1)
        {
            throw new ArgumentException($"'{text}' is not a positive integer.");
        }

        return value;
    }
}
