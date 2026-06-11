using Azure.Storage.Blobs;

namespace DataChronicles.Api.Services;

/// <summary>
/// Optional Azure Blob Storage archival of generated output files.
/// If no (real) connection string is configured the service becomes a no-op so the
/// app runs locally without Azurite or an Azure account.
/// </summary>
public class BlobStorageService
{
    private readonly BlobContainerClient? _container;
    private readonly ILogger<BlobStorageService> _log;

    public bool Enabled => _container != null;

    public BlobStorageService(IConfiguration config, ILogger<BlobStorageService> log)
    {
        _log = log;
        var conn = config["AzureStorage:BlobConnectionString"];
        var containerName = config["AzureStorage:ContainerName"] ?? "datachronicles";

        if (string.IsNullOrWhiteSpace(conn) || conn.StartsWith("YOUR_"))
        {
            _log.LogInformation("Blob storage not configured — archival disabled.");
            return;
        }

        try
        {
            _container = new BlobContainerClient(conn, containerName);
            _container.CreateIfNotExists();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not connect to Blob storage — archival disabled.");
            _container = null;
        }
    }

    public async Task UploadAsync(string name, byte[] data)
    {
        if (_container == null) return;
        try
        {
            var blob = _container.GetBlobClient(name);
            await blob.UploadAsync(new MemoryStream(data), overwrite: true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Blob upload failed for {Name}.", name);
        }
    }
}
