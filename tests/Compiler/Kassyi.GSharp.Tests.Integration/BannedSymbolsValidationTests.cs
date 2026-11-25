
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

            foreach (var localFile in FindLocalBannedSymbolFiles(root))
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

        [Fact]
        [Scenario(TestScenario.Boundary)]
        public void FindLocalBannedSymbolFiles_NestedWorktreeTmpAndBuildOutput_AreSkipped()
        {
            var root = Path.Combine(Path.GetTempPath(), "gsharp-banned-symbols-" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteBannedSymbols(root);
                WriteBannedSymbols(Path.Combine(root, "src", "Project"));
                WriteBannedSymbols(Path.Combine(root, "src", "Project", "obj"));
                WriteBannedSymbols(Path.Combine(root, "src", "Project", "bin"));
                WriteBannedSymbols(Path.Combine(root, "tmp", "long-checks", "run"));
                var nestedWorktree = Path.Combine(root, ".claude", "worktrees", "agent");
                WriteBannedSymbols(nestedWorktree);
                File.WriteAllText(Path.Combine(nestedWorktree, ".git"), "gitdir: elsewhere");
                var nestedClone = Path.Combine(root, "docs", "clone");
                WriteBannedSymbols(nestedClone);
                Directory.CreateDirectory(Path.Combine(nestedClone, ".git"));

                var found = FindLocalBannedSymbolFiles(root);

                Assert.Equal(new[] { Path.Combine(root, "src", "Project", "BannedSymbols.txt") }, found);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        private static void WriteBannedSymbols(string directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "BannedSymbols.txt"), "T:System.Object");
        }

        // The BannedSymbols.txt files of this checkout other than the root one. [WHY] A nested git worktree or clone
        // (a directory holding a .git file or folder, such as .claude/worktrees/<name>) and the root tmp/ (scratch
        // worktrees and run output) carry their own copies of the root file; bin/ and obj/ hold build copies.
        private static List<string> FindLocalBannedSymbolFiles(string root)
        {
            var found = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                if (!string.Equals(directory, root, StringComparison.Ordinal))
                {
                    var bannedSymbols = Path.Combine(directory, "BannedSymbols.txt");
                    if (File.Exists(bannedSymbols))
                        found.Add(bannedSymbols);
                }

                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    if (!IsSkippedDirectory(root, child))
                        pending.Push(child);
                }
            }

            found.Sort(StringComparer.Ordinal);
            return found;
        }

        private static bool IsSkippedDirectory(string root, string directory)
        {
            var name = Path.GetFileName(directory);
            return name is "bin" or "obj" or ".git" ||
                   (name == "tmp" && string.Equals(Path.GetDirectoryName(directory), root, StringComparison.Ordinal)) ||
                   Path.Exists(Path.Combine(directory, ".git"));
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
