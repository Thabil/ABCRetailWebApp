using System.Collections.Generic;

namespace ABCRetailWebApp.Models
{
    public class AdminDashboardViewModel
    {
        public int TotalOrders { get; set; }
        public int TotalCustomers { get; set; }
        public int InventoryItems { get; set; }
        public int PendingOrders { get; set; }
        public int AuditLogEntries { get; set; }
        public double TotalRevenue { get; set; }
        public int StockAlertCount { get; set; }
        public List<OrderEntity> AllOrders { get; set; } = new();
        public List<ProductEntity> OutOfStockProducts { get; set; } = new();
    }

    public class FunctionActivityViewModel
    {
        public List<OrderAuditEntity>     OrderAudit   { get; set; } = new();
        public List<StockAlertEntity>     StockAlerts  { get; set; } = new();
        public List<TransactionLogEntity> Transactions { get; set; } = new();
    }
}
