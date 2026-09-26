using System.Globalization;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Storage.Files.Shares;
using Microsoft.Extensions.Azure;
using ABCRetailWebApp.Models;
using ABCRetailWebApp.Services;

var builder = WebApplication.CreateBuilder(args);

// ✅ Force dot (.) as decimal separator
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

builder.Services.AddControllersWithViews();

// ✅ Required for IHttpContextAccessor
builder.Services.AddHttpContextAccessor();

// ✅ Add session support
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var connectionString = builder.Configuration.GetConnectionString("AzureStorage");

builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.AddTableServiceClient(connectionString);
    clientBuilder.AddBlobServiceClient(connectionString);

    clientBuilder.AddClient<QueueServiceClient, QueueClientOptions>((options) =>
    {
        return new QueueServiceClient(connectionString);
    });

    clientBuilder.AddClient<ShareServiceClient, ShareClientOptions>((options) =>
    {
        return new ShareServiceClient(connectionString);
    });
});

// ============================================================
// REGISTER ALL SERVICES
// ============================================================

// Table Storage - Customers (will also be used for authentication)
builder.Services.AddScoped<ITableStorageService<CustomerEntity>>(provider =>
{
    var tableClient = provider.GetRequiredService<TableServiceClient>();
    return new TableStorageService<CustomerEntity>(tableClient, "Customers");
});

// Table Storage - Products
builder.Services.AddScoped<ITableStorageService<ProductEntity>>(provider =>
{
    var tableClient = provider.GetRequiredService<TableServiceClient>();
    return new TableStorageService<ProductEntity>(tableClient, "Products");
});

// Table Storage - Orders (NEW)
builder.Services.AddScoped<ITableStorageService<ABCRetailWebApp.Models.OrderEntity>>(provider =>
{
    var tableClient = provider.GetRequiredService<TableServiceClient>();
    return new TableStorageService<ABCRetailWebApp.Models.OrderEntity>(tableClient, "Orders");
});

// Table Storage - Function audit tables (read-only from webapp side)
builder.Services.AddScoped<ITableStorageService<ABCRetailWebApp.Models.OrderAuditEntity>>(provider =>
{
    var tableClient = provider.GetRequiredService<TableServiceClient>();
    return new TableStorageService<ABCRetailWebApp.Models.OrderAuditEntity>(tableClient, "OrderAudit");
});

builder.Services.AddScoped<ITableStorageService<ABCRetailWebApp.Models.StockAlertEntity>>(provider =>
{
    var tableClient = provider.GetRequiredService<TableServiceClient>();
    return new TableStorageService<ABCRetailWebApp.Models.StockAlertEntity>(tableClient, "StockAlerts");
});

builder.Services.AddScoped<ITableStorageService<ABCRetailWebApp.Models.TransactionLogEntity>>(provider =>
{
    var tableClient = provider.GetRequiredService<TableServiceClient>();
    return new TableStorageService<ABCRetailWebApp.Models.TransactionLogEntity>(tableClient, "Transactions");
});

// Blob Storage
builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();

// Queue Storage
builder.Services.AddScoped<IQueueStorageService>(provider =>
{
    var queueClient = provider.GetRequiredService<QueueServiceClient>();
    return new QueueStorageService(queueClient);
});

// File Storage
builder.Services.AddScoped<IFileStorageService>(provider =>
{
    var fileClient = provider.GetRequiredService<ShareServiceClient>();
    return new FileStorageService(fileClient);
});

// ✅ Add AuthService
builder.Services.AddScoped<AuthService>();

// Azure Functions HTTP client (fire-and-forget with timeout + retry)
builder.Services.AddHttpClient<FunctionHttpClient>(client =>
{
    var baseUrl = builder.Configuration["FunctionsBaseUrl"] ?? "http://localhost:7071";
    client.BaseAddress = new Uri(baseUrl);
});

var app = builder.Build();

// ============================================================
// RUN SEEDER ON STARTUP (overwrite = true to fix stale data)
// ============================================================
using (var scope = app.Services.CreateScope())
{
    var tableClient = scope.ServiceProvider.GetRequiredService<TableServiceClient>();
    var queueClient = scope.ServiceProvider.GetRequiredService<QueueServiceClient>();
    var seeder      = new DataSeeder(tableClient, queueClient);
    var seedFile    = Path.Combine(app.Environment.ContentRootPath, "seed-data.json");
    await seeder.RunAsync(seedFile, overwrite: true);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// ✅ Use session middleware
app.UseSession();

app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();