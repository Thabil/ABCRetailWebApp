using Azure;
using Azure.Data.Tables;

namespace ABCRetailWebApp.Services
{
    public class TableStorageService<T> : ITableStorageService<T> where T : class, ITableEntity, new()
    {
        private readonly TableClient _tableClient;

        public TableStorageService(TableServiceClient tableServiceClient, string tableName)
        {
            _tableClient = tableServiceClient.GetTableClient(tableName);
            _tableClient.CreateIfNotExists();
        }

        public async Task<T?> GetEntityAsync(string partitionKey, string rowKey)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<T>(partitionKey, rowKey);
                return response.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task<IEnumerable<T>> GetEntitiesAsync(string? partitionKey = null)
        {
            var results = new List<T>();
            var query = partitionKey != null
                ? _tableClient.QueryAsync<T>(filter: $"PartitionKey eq '{partitionKey}'")
                : _tableClient.QueryAsync<T>();

            await foreach (var entity in query)
            {
                results.Add(entity);
            }
            return results;
        }

        public async Task AddEntityAsync(T entity)
        {
            await _tableClient.AddEntityAsync(entity);
        }

        public async Task UpdateEntityAsync(T entity)
        {
            // Use ETag.All (wildcard) so callers don't need to pre-fetch the current ETag
            var etag = entity.ETag == default ? ETag.All : entity.ETag;
            await _tableClient.UpdateEntityAsync(entity, etag, TableUpdateMode.Replace);
        }

        public async Task DeleteEntityAsync(string partitionKey, string rowKey)
        {
            await _tableClient.DeleteEntityAsync(partitionKey, rowKey);
        }

        public async Task<bool> EntityExistsAsync(string partitionKey, string rowKey)
        {
            return await GetEntityAsync(partitionKey, rowKey) != null;
        }
    }
}