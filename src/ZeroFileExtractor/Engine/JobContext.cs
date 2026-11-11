namespace ZeroFileExtractor.Engine;

/// <summary>Hedefte aynı adlı dosya varsa ne yapılacağı.</summary>
public enum ConflictMode
{
    Ask = 0,
    Overwrite = 1,
    Skip = 2,
    Rename = 3,
}

/// <summary>Tek bir çakışma için kullanıcının kararı.</summary>
public readonly record struct ConflictDecision(ConflictMode Mode, bool ApplyToAll);

public sealed record ConflictInfo(string TargetPath, long ExistingSize, DateTime ExistingModified, long NewSize, DateTime? NewModified);

/// <summary>İlerleme anlık görüntüsü.</summary>
public readonly record struct JobProgress(long BytesDone, long BytesTotal, int FilesDone, int FilesTotal, string CurrentItem);

/// <summary>
/// Uzun süren bir arşiv işinin (çıkarma, sıkıştırma, test) arayüzle konuştuğu kanal:
/// iptal, duraklatma, ilerleme, çakışma ve parola soruları.
/// </summary>
public sealed class JobContext
{
    private readonly ManualResetEventSlim _gate = new(true);
    private long _bytesDone;
    private int _filesDone;
    private string _current = string.Empty;

    public CancellationToken Cancellation { get; init; }

    public long BytesTotal { get; set; }
    public int FilesTotal { get; set; }

    /// <summary>Atlanan (çakışma, bağlantı vb.) öğe sayısı.</summary>
    public int Skipped { get; set; }

    /// <summary>Ölümcül olmayan sorunlar (tek tek dosya hataları).</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>İş parçacığından çağrılır, cevap gelene kadar bekler.</summary>
    public Func<ConflictInfo, ConflictDecision>? ResolveConflict { get; init; }

    /// <summary>Parola gerektiğinde sorulur; null dönerse iş iptal edilir. Parametre: önceki deneme yanlış mıydı.</summary>
    public Func<bool, string?>? RequestPassword { get; init; }

    public bool IsPaused => !_gate.IsSet;

    public void Pause() => _gate.Reset();

    public void Resume() => _gate.Set();

    /// <summary>Kopyalama döngülerinde çağrılır: duraklatılmışsa bekler, iptal edilmişse fırlatır.</summary>
    public void Checkpoint()
    {
        if (!_gate.IsSet) _gate.Wait(Cancellation);
        Cancellation.ThrowIfCancellationRequested();
    }

    public void SetCurrent(string item) => Volatile.Write(ref _current, item);

    public void AddBytes(long count) => Interlocked.Add(ref _bytesDone, count);

    public void SetBytes(long value) => Interlocked.Exchange(ref _bytesDone, value);

    public void FileDone() => Interlocked.Increment(ref _filesDone);

    public JobProgress Snapshot() =>
        new(Interlocked.Read(ref _bytesDone), BytesTotal, Volatile.Read(ref _filesDone), FilesTotal, Volatile.Read(ref _current));

    /// <summary>Akışı duraklatma/iptal ve ilerleme sayımıyla kopyalar.</summary>
    public void Copy(Stream source, Stream target, bool countBytes = true)
    {
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1 << 17);
        try
        {
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                Checkpoint();
                target.Write(buffer, 0, read);
                if (countBytes) AddBytes(read);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

/// <summary>Okunan baytları ilerlemeye sayan, duraklatma/iptali uygulayan sarmalayıcı (sıkıştırmada kaynak dosyalar için).</summary>
public sealed class CountingReadStream(Stream inner, JobContext context) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }

    public override int Read(byte[] buffer, int offset, int count)
    {
        context.Checkpoint();
        int read = inner.Read(buffer, offset, count);
        if (read > 0) context.AddBytes(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        context.Checkpoint();
        int read = inner.Read(buffer);
        if (read > 0) context.AddBytes(read);
        return read;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Parola gerekli ya da yanlış.</summary>
public sealed class PasswordRequiredException(bool wrong) : Exception(wrong ? "Wrong password" : "Password required")
{
    public bool Wrong { get; } = wrong;
}
