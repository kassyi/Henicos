using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Kassyi.GSharp.Compiler;

class Program {
    static void Main() {
        var src = File.ReadAllText("tests/Kassyi.GSharp.Tests.Shared/TestCases/COR-02_SharedMemoryKernels.cu.cs");
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTrees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(src, options: parseOptions) };
        var compilation = CSharpCompilation.Create("Dummy").AddSyntaxTrees(syntaxTrees);

        var generator = new GpuKernelGenerator();
        var driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: new Kassyi.GSharp.Tests.Helpers.TestAnalyzerConfigOptionsProvider("Cpu")
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var runResult = driver.GetRunResult();

        foreach (var t in runResult.GeneratedTrees) {
            Console.WriteLine("Generated: " + t.FilePath);
        }
    }
}
