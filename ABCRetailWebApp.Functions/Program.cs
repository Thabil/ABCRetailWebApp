using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Storage.Files.Shares;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") 
                               ?? throw new InvalidOperationException("AzureWebJobsStorage connection string is required");

        services.AddAzureClients(clientBuilder =>
        {
            clientBuilder.AddTableServiceClient(connectionString);
            clientBuilder.AddBlobServiceClient(connectionString);
            // Disable automatic base64 encoding so JSON queue messages are not double-encoded
            clientBuilder.AddQueueServiceClient(connectionString);
            clientBuilder.AddClient<ShareServiceClient, ShareClientOptions>((options) =>
            {
                return new ShareServiceClient(connectionString);
            });
        });
    })
    .Build();

host.Run();
