namespace Kassyi.GSharp.Compiler.Generators;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args is ["demangle", ..])
        {
            return LogDemangler.Run(args[1..]);
        }

        if (args is ["diagnostics", _, _, ..])
        {
            DiagnosticGenerator.Run(args[1], args[2]);
            return 0;
        }

        if (args is ["dialects", _, _, ..])
        {
            YamlDialectGenerator.Run(args[1], args[2]);
            return 0;
        }

        if (args is ["native-api", _, _, ..])
        {
            YamlApiGenerator.Run(args[1], args[2]);
            return 0;
        }

        if (args is ["prelude", _, _, ..])
        {
            PreludeGenerator.Run(args[1], args[2]);
            return 0;
        }

        if (args is ["internal-names", _, _, _, _, ..])
        {
            InternalNamesGenerator.Run(args[1], args[2], args[3], args[4]);
            return 0;
        }

        // Fallback for backwards compatibility with existing build tasks
        if (args.Length >= 2)
        {
            YamlDialectGenerator.Run(args[0], args[1]);
        }
        else
        {
            Console.WriteLine("Usage: Kassyi.GSharp.Compiler.Generators <mode> <input> <output>");
            Console.WriteLine("Modes: dialects, diagnostics, native-api, prelude, internal-names, demangle");
        }

        return 0;
    }
}
