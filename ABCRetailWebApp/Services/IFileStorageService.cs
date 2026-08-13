namespace ABCRetailWebApp.Services
{
    public interface IFileStorageService
    {
        Task<string> UploadLogFileAsync(string fileName, string content);
        Task<string> DownloadLogFileAsync(string fileName);
        Task<IEnumerable<string>> ListLogFilesAsync();
        Task DeleteLogFileAsync(string fileName);
        Task AppendToLogAsync(string fileName, string content);
    }
}