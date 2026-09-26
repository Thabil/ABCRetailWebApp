using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

namespace ABCRetailWebApp.Services
{
    public class QueueStorageService : IQueueStorageService
    {
        private readonly QueueServiceClient _queueServiceClient;

        public QueueStorageService(QueueServiceClient queueServiceClient)
        {
            _queueServiceClient = queueServiceClient;
        }

        public async Task SendMessageAsync(string queueName, string message)
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.CreateIfNotExistsAsync();
            await queueClient.SendMessageAsync(message);
        }

        public async Task<(string Text, string MessageId, string PopReceipt)?> ReceiveMessageAsync(string queueName)
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.CreateIfNotExistsAsync();

            var response = await queueClient.ReceiveMessageAsync();
            if (response.Value == null) return null;

            var msg = response.Value;

            // Delete immediately after receiving
            await queueClient.DeleteMessageAsync(msg.MessageId, msg.PopReceipt);

            return (msg.MessageText, msg.MessageId, msg.PopReceipt);
        }

        public async Task DeleteMessageAsync(string queueName, string messageId, string popReceipt)
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.DeleteMessageAsync(messageId, popReceipt);
        }

        public async Task<IEnumerable<string>> PeekMessagesAsync(string queueName, int maxMessages = 5)
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.CreateIfNotExistsAsync();

            var messages = await queueClient.PeekMessagesAsync(maxMessages);
            return messages.Value.Select(m => m.MessageText);
        }

        public async Task ClearQueueAsync(string queueName)
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.ClearMessagesAsync();
        }
    }
}