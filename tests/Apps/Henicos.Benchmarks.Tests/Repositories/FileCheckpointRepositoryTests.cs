using Henicos.Benchmarks.Repositories;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

namespace Henicos.Benchmarks.Tests.Repositories;

[Category(TestCategory.Unit)]
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
    public void SaveAndLoad_RoundTripsEveryField()
    {
        var checkpoint = CreateCheckpoint();

        _repository.Save(checkpoint);

        Assert.True(_repository.Exists());
        var loaded = _repository.Load();
        Assert.Equal(checkpoint.E, loaded.E);
        Assert.Equal(checkpoint.Base, loaded.Base);
        Assert.Equal(checkpoint.BlockSize, loaded.BlockSize);
        Assert.Equal(checkpoint.Iteration, loaded.Iteration);
        Assert.Equal(checkpoint.ChecksPassed, loaded.ChecksPassed);
        Assert.Equal(checkpoint.Rollbacks, loaded.Rollbacks);
        Assert.Equal(checkpoint.State.ToArray(), loaded.State.ToArray());
        Assert.Equal(checkpoint.Check.ToArray(), loaded.Check.ToArray());
        Assert.Equal(checkpoint.SnapshotE.ToArray(), loaded.SnapshotE.ToArray());
        Assert.Equal(checkpoint.FinalK.ToArray(), loaded.FinalK.ToArray());
    }

    [Fact]
    public void SaveAndLoad_EmptySnapshot_RoundTrips()
    {
        var checkpoint = new FermatPrpCheckpoint(61, 3, 7, 49, new uint[] { 1, 2 }, new uint[] { 3, 4 },
            ReadOnlyMemory<uint>.Empty, ReadOnlyMemory<uint>.Empty, 1, 0);

        _repository.Save(checkpoint);

        var loaded = _repository.Load();
        Assert.True(loaded.SnapshotE.IsEmpty);
        Assert.True(loaded.FinalK.IsEmpty);
    }

    [Fact]
    public void Load_ThrowsFileNotFoundException_WhenFileDoesNotExist()
    {
        Assert.Throws<FileNotFoundException>(() => _repository.Load());
    }

    [Fact]
    public void Load_ThrowsInvalidDataException_WhenFileIsCorrupted_Truncated()
    {
        _repository.Save(CreateCheckpoint());

        // Truncate the file slightly
        using (var fs = File.OpenWrite(_checkpointFile))
        {
            fs.SetLength(fs.Length - 5);
        }

        Assert.Throws<InvalidDataException>(() => _repository.Load());
    }

    [Fact]
    public void Load_ThrowsInvalidDataException_WhenHashMismatches()
    {
        _repository.Save(CreateCheckpoint());

        // Corrupt one byte of the payload after the magic.
        using (var fs = File.OpenWrite(_checkpointFile))
        {
            fs.Seek(8, SeekOrigin.Begin);
            fs.WriteByte(0xFF);
        }

        Assert.Throws<InvalidDataException>(() => _repository.Load());
    }

    [Fact]
    public void Load_ThrowsInvalidDataException_ForTheEarlierFormat()
    {
        // The earlier format: an int iteration, the raw state words and a SHA-256, without the magic. Its state was a
        // redundant 2L + 16-word value that the current algorithm cannot resume from.
        using (var fs = File.Create(_checkpointFile))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(100000);
            bw.Write(new byte[4 * 18]);
            bw.Write(new byte[32]);
        }

        var exception = Assert.Throws<InvalidDataException>(() => _repository.Load());
        Assert.Contains("earlier format", exception.Message);
    }

    [Fact]
    public void Load_LastPointOfARun_ResumesToTheSameVerdictAndIsRejectedByAnotherTest()
    {
        // The benchmark saves every verified point; after a finished run the file holds the last one (k = K'), which
        // must carry u_K for the resumed run to decide again. E = 13 runs on the host: K = 27, B = 5, K' = 30.
        FermatPrpResult expected;
        using (var source = new FermatAlgorithm(13, 3))
        {
            expected = source.Run();
            _repository.Save(source.CreateCheckpoint());
        }

        var loaded = _repository.Load();
        Assert.False(loaded.FinalK.IsEmpty);

        using var resumed = new FermatAlgorithm(13, 3);
        resumed.Restore(loaded);
        var result = resumed.Run();
        Assert.Equal(expected.Residue64, result.Residue64);
        Assert.Equal(expected.IsProbablePrime, result.IsProbablePrime);

        // A file of another test is refused when it is restored.
        using var other = new FermatAlgorithm(29, 3);
        Assert.Throws<ArgumentException>(() => other.Restore(loaded));
    }

    private static FermatPrpCheckpoint CreateCheckpoint() =>
        new(61, 3, 7, 126, new uint[] { 1, 2, 3, 4 }, new uint[] { 5, 6, 7, 8 }, new uint[] { 9, 10, 11, 12 },
            new uint[] { 13, 14, 15, 16 }, 3, 1);
}
