namespace ABCRetailWebApp.Services
{
    public interface IQueueStorageService
    {
        Task SendMessageAsync(string queueName, string message);
        Task<(string Text, string MessageId, string PopReceipt)?> ReceiveMessageAsync(string queueName);
        Task DeleteMessageAsync(string queueName, string messageId, string popReceipt);
        Task<IEnumerable<string>> PeekMessagesAsync(string queueName, int maxMessages = 5);
        Task ClearQueueAsync(string queueName);
    }
}