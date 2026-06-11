using System.Collections.Concurrent;

namespace DataChronicles.Api.Services;

/// <summary>
/// Holds generated output workbooks in memory, keyed by batch id, so the UI can
/// first render results (table + chart) and then download the same file on demand.
/// Registered as a singleton.
/// </summary>
public class GeneratedFileStore
{
    private readonly ConcurrentDictionary<string, (byte[] Data, string FileName)> _files = new();

    public void Save(string batchId, byte[] data, string fileName) => _files[batchId] = (data, fileName);

    public (byte[] Data, string FileName)? Get(string batchId) =>
        _files.TryGetValue(batchId, out var v) ? v : null;
}
