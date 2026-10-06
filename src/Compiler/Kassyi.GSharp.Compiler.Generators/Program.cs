namespace Kassyi.GSharp.Compiler.Generators;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args is ["diagnostics", _, _, ..])
        {
            DiagnosticGenerator.Run(args[1], args[2]);
            return;
        }

        if (args is ["dialects", _, _, ..])
        {
            YamlDialectGenerator.Run(args[1], args[2]);
            return;
        }

        if (args is ["native-api", _, _, ..])
        {
            YamlApiGenerator.Run(args[1], args[2]);
            return;
        }

        // Fallback for backwards compatibility with existing build tasks
        if (args.Length >= 2)
        {
            YamlDialectGenerator.Run(args[0], args[1]);
        }
        else
        {
            Console.WriteLine("Usage: Kassyi.GSharp.Compiler.Generators <mode> <input> <output>");
            Console.WriteLine("Modes: dialects, diagnostics, native-api");
        }
    }
}
