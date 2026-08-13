namespace ABCRetailWebApp.Services
{
    public interface IBlobStorageService
    {
        Task<string> UploadImageAsync(IFormFile file, string customName);
        Task<string?> GetSasUriAsync(string blobName, int expiryMinutes = 10);
        Task DeleteImageAsync(string blobName);
        Task<IEnumerable<string>> ListImagesAsync();
    }
}