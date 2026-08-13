using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace ABCRetailWebApp.Services
{
    public class BlobStorageService : IBlobStorageService
    {
        private readonly BlobContainerClient _containerClient;

        public BlobStorageService(BlobServiceClient blobServiceClient)
        {
            _containerClient = blobServiceClient.GetBlobContainerClient("productimages");
            _containerClient.CreateIfNotExists(PublicAccessType.None);
        }

        public async Task<string> UploadImageAsync(IFormFile file, string customName)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("Please select a file.");

            var extension = Path.GetExtension(file.FileName);
            var blobName = $"{customName}{extension}";
            var blobClient = _containerClient.GetBlobClient(blobName);

            using var stream = file.OpenReadStream();
            await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = file.ContentType });

            return blobName;
        }

        public async Task<string?> GetSasUriAsync(string blobName, int expiryMinutes = 10)
        {
            if (string.IsNullOrEmpty(blobName)) return null;

            var blobClient = _containerClient.GetBlobClient(blobName);
            if (!await blobClient.ExistsAsync()) return null;

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _containerClient.Name,
                BlobName = blobName,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            return blobClient.GenerateSasUri(sasBuilder).ToString();
        }

        public async Task DeleteImageAsync(string blobName)
        {
            if (string.IsNullOrEmpty(blobName)) return;
            var blobClient = _containerClient.GetBlobClient(blobName);
            await blobClient.DeleteIfExistsAsync();
        }

        public async Task<IEnumerable<string>> ListImagesAsync()
        {
            var images = new List<string>();
            await foreach (var blob in _containerClient.GetBlobsAsync())
            {
                images.Add(blob.Name);
            }
            return images;
        }
    }
}