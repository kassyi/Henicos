using Henicos.Benchmarks.Repositories;

namespace Henicos.Benchmarks.Tests.Repositories;

public class FileCheckpointRepositoryTests : IDisposable
{
    private readonly string _checkpointFile;
    private readonly string _tempCheckpointFile;
    private readonly FileCheckpointRepository _repository;

    public FileCheckpointRepositoryTests()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        _checkpointFile = Path.Combine(tempDir, "checkpoint.dat");
        _tempCheckpointFile = Path.Combine(tempDir, "checkpoint.tmp");
        _repository = new FileCheckpointRepository(_checkpointFile, _tempCheckpointFile);
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_checkpointFile);
        if (dir != null && Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Exists_ReturnsFalse_WhenFileDoesNotExist()
    {
        Assert.False(_repository.Exists());
    }

    [Fact]
    public void SaveAndLoad_WorksCorrectly()
    {
        int iteration = 42;
        uint[] state = new uint[] { 1, 2, 3, 4, 5 };

        _repository.Save(iteration, state);

        Assert.True(_repository.Exists());
        var (loadedIter, loadedState) = _repository.Load(state.Length);

        Assert.Equal(iteration, loadedIter);
        Assert.Equal(state, loadedState);
    }

    [Fact]
    public void Load_ThrowsFileNotFoundException_WhenFileDoesNotExist()
    {
        Assert.Throws<FileNotFoundException>(() => _repository.Load(5));
    }

    [Fact]
    public void Load_ThrowsInvalidDataException_WhenFileIsCorrupted_Truncated()
    {
        int iteration = 42;
        uint[] state = new uint[] { 1, 2, 3, 4, 5 };
        _repository.Save(iteration, state);

        // Truncate the file slightly
        using (var fs = File.OpenWrite(_checkpointFile))
        {
            fs.SetLength(fs.Length - 5);
        }

        Assert.Throws<InvalidDataException>(() => _repository.Load(state.Length));
    }

    [Fact]
    public void Load_ThrowsInvalidDataException_WhenHashMismatches()
    {
        int iteration = 42;
        uint[] state = new uint[] { 1, 2, 3, 4, 5 };
        _repository.Save(iteration, state);

        // Corrupt the data portion (skip 4 bytes for iteration, then modify the state)
        using (var fs = File.OpenWrite(_checkpointFile))
        {
            fs.Seek(4, SeekOrigin.Begin);
            fs.WriteByte(0xFF);
        }

        Assert.Throws<InvalidDataException>(() => _repository.Load(state.Length));
    }
}
