
namespace Kassyi.GSharp.Tests.Integration
{
    public class BannedSymbolsValidationTests
    {
        private static string GetRepositoryRoot()
        {
            var current = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (current != null)
            {
                if (current.GetFiles("Henicos.slnx").Length > 0)
                    return current.FullName;
                current = current.Parent;
            }
            throw new InvalidOperationException("Repository root not found.");
        }

        [Fact]
        public void BannedSymbols_NoDuplicateDefinitions_ToPreventRS0031()
        {
            var root = GetRepositoryRoot();
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

        private Dictionary<string, string> ParseBannedSymbols(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            var dict = new Dictionary<string, string>();
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split(new[] { ';' }, 2);
                var symbol = parts[0].Trim();

                Assert.True(!dict.ContainsKey(symbol), 
                    $"Duplicate symbol '{symbol}' found within the same file {filePath} at line {i + 1}. " +
                    $"This will cause BannedApiAnalyzers to emit RS0031.");
                    
                dict[symbol] = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            }
            
            return dict;
        }
    }
}
