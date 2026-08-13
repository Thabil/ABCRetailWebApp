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
        public List<OrderEntity> AllOrders { get; set; } = new();
    }
}
