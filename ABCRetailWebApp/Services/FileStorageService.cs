using Azure;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;

namespace ABCRetailWebApp.Services
{
    public class FileStorageService : IFileStorageService
    {
        private readonly ShareClient _shareClient;
        private readonly ShareDirectoryClient _directoryClient;

        public FileStorageService(ShareServiceClient shareServiceClient)
        {
            _shareClient = shareServiceClient.GetShareClient("applogs");
            _shareClient.CreateIfNotExists();

            _directoryClient = _shareClient.GetDirectoryClient("logs");
            _directoryClient.CreateIfNotExists();
        }

        public async Task<string> UploadLogFileAsync(string fileName, string content)
        {
            try
            {
                var fileClient = _directoryClient.GetFileClient(fileName);
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                await fileClient.CreateAsync(stream.Length);
                await fileClient.UploadRangeAsync(new HttpRange(0, stream.Length), stream);
                return $"{fileName} uploaded successfully";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        public async Task<string> DownloadLogFileAsync(string fileName)
        {
            try
            {
                var fileClient = _directoryClient.GetFileClient(fileName);
                if (!await fileClient.ExistsAsync()) return "File not found";

                var downloadInfo = await fileClient.DownloadAsync();
                using var reader = new StreamReader(downloadInfo.Value.Content);
                return await reader.ReadToEndAsync();
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        public async Task<IEnumerable<string>> ListLogFilesAsync()
        {
            var files = new List<string>();
            await foreach (var item in _directoryClient.GetFilesAndDirectoriesAsync())
            {
                files.Add(item.Name);
            }
            return files;
        }

        public async Task DeleteLogFileAsync(string fileName)
        {
            var fileClient = _directoryClient.GetFileClient(fileName);
            await fileClient.DeleteIfExistsAsync();
        }

        public async Task AppendToLogAsync(string fileName, string content)
        {
            var fileClient = _directoryClient.GetFileClient(fileName);
            var bytes = System.Text.Encoding.UTF8.GetBytes(content + Environment.NewLine);

            if (await fileClient.ExistsAsync())
            {
                var properties = await fileClient.GetPropertiesAsync();
                long fileSize = properties.Value.ContentLength;
                long newSize  = fileSize + bytes.Length;

                // Azure File Storage requires the file to be resized before writing beyond current allocation
                await fileClient.SetHttpHeadersAsync(new ShareFileSetHttpHeadersOptions { NewSize = newSize });

                using var stream = new MemoryStream(bytes);
                await fileClient.UploadRangeAsync(new HttpRange(fileSize, bytes.Length), stream);
            }
            else
            {
                await UploadLogFileAsync(fileName, content + Environment.NewLine);
            }
        }
    }
}