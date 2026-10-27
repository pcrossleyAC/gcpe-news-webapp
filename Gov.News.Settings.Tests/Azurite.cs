using System.Net.Sockets;
using Azure.Storage.Blobs;

namespace Gov.News.Settings.Tests;

/// <summary>
/// Runs a test against the Azurite storage emulator, and skips it when Azurite isn't running.
/// Start Azurite with:
/// <code>docker run -d -p 10000:10000 mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0</code>
/// </summary>
public sealed class AzuriteFactAttribute : FactAttribute
{
    public AzuriteFactAttribute()
    {
        if (!Azurite.IsRunning)
            Skip = "Azurite is not listening on localhost:10000.";
    }
}

public static class Azurite
{
    public const string ConnectionString = "UseDevelopmentStorage=true";

    public static readonly bool IsRunning = Probe();

    /// <summary>A new, empty container per test so tests can't see each other's blobs.</summary>
    public static BlobContainerClient NewContainer()
    {
        var container = new BlobContainerClient(ConnectionString, "settings-" + Guid.NewGuid().ToString("N"));
        container.Create();
        return container;
    }

    private static bool Probe()
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync("127.0.0.1", 10000).Wait(TimeSpan.FromSeconds(1));
        }
        catch (Exception)
        {
            return false;
        }
    }
}
