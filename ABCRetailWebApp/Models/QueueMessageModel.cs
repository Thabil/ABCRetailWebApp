namespace ABCRetailWebApp.Models
{
    public class QueueMessageModel
    {
        public string? MessageId { get; set; }
        public string? Content { get; set; }
        public DateTime? Timestamp { get; set; }
        public string? PopReceipt { get; set; }
        public bool IsPoisoned { get; set; }
    }
}