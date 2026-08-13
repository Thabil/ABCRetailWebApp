using Azure.Data.Tables;

namespace ABCRetailWebApp.Services
{
    public interface ITableStorageService<T> where T : class, ITableEntity
    {
        Task<T?> GetEntityAsync(string partitionKey, string rowKey);
        Task<IEnumerable<T>> GetEntitiesAsync(string? partitionKey = null);
        Task AddEntityAsync(T entity);
        Task UpdateEntityAsync(T entity);
        Task DeleteEntityAsync(string partitionKey, string rowKey);
        Task<bool> EntityExistsAsync(string partitionKey, string rowKey);
    }
}