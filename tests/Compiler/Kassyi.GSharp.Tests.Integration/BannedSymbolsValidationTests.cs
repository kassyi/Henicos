
namespace Kassyi.GSharp.Tests.Integration
{
    [Category(TestCategory.Compiler)]
    public class BannedSymbolsValidationTests
    {
        [Fact]
        public void BannedSymbols_NoDuplicateDefinitions_ToPreventRS0031()
        {
            var root = RepositoryPaths.Root;
            var rootBannedSymbolsPath = Path.Combine(root, "BannedSymbols.txt");
            
            Assert.True(File.Exists(rootBannedSymbolsPath), "Root BannedSymbols.txt must exist.");
            var rootSymbols = ParseBannedSymbols(rootBannedSymbolsPath);

            var allFiles = Directory.GetFiles(root, "BannedSymbols.txt", SearchOption.AllDirectories);
            var localBannedSymbolFiles = new List<string>();
            foreach (var f in allFiles)
            {
                if (string.Equals(f, rootBannedSymbolsPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                    f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                    continue;
                localBannedSymbolFiles.Add(f);
            }

            foreach (var localFile in localBannedSymbolFiles)
            {
                var localSymbols = ParseBannedSymbols(localFile);
                var duplicates = new List<string>();
                
                foreach (var key in localSymbols.Keys)
                {
                    if (rootSymbols.ContainsKey(key))
                    {
                        duplicates.Add(key);
                    }
                }

                Assert.True(duplicates.Count == 0, 
                    $"Found duplicate BannedSymbols definitions between {rootBannedSymbolsPath} and {localFile}. " +
                    $"This will cause BannedApiAnalyzers to emit RS0031 and IGNORE the rule! " +
                    $"Duplicates: {string.Join(", ", duplicates)}");
            }
        }

        private static readonly string[] ValidDocIdPrefixes = ["T:", "M:", "P:", "F:", "E:", "N:"];

        private Dictionary<string, string> ParseBannedSymbols(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            var dict = new Dictionary<string, string>();
            
            for (int i = 0; i < lines.Length; i++)
            {
                var lineSpan = lines[i].AsSpan().Trim();
                if (lineSpan.IsEmpty || lineSpan.StartsWith("#"))
                    continue;

                var semicolonIndex = lineSpan.IndexOf(';');
                var symbolSpan = semicolonIndex >= 0 ? lineSpan[..semicolonIndex].Trim() : lineSpan;
                var descriptionSpan = semicolonIndex >= 0 ? lineSpan[(semicolonIndex + 1)..].Trim() : default;
                var symbol = symbolSpan.ToString();

                bool hasValidPrefix = false;
                foreach (var prefix in ValidDocIdPrefixes)
                {
                    if (symbolSpan.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        hasValidPrefix = true;
                        break;
                    }
                }

                Assert.True(hasValidPrefix,
                    $"Invalid Roslyn Documentation Comment ID prefix for symbol '{symbol}' in {filePath} at line {i + 1}. " +
                    $"Expected one of: {string.Join(", ", ValidDocIdPrefixes)}");

                Assert.True(!dict.ContainsKey(symbol), 
                    $"Duplicate symbol '{symbol}' found within the same file {filePath} at line {i + 1}. " +
                    $"This will cause BannedApiAnalyzers to emit RS0031.");
                    
                dict[symbol] = descriptionSpan.IsEmpty ? string.Empty : descriptionSpan.ToString();
            }
            
            return dict;
        }
    }
}
