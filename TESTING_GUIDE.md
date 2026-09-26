# Azure Functions — Testing Guide

All tests below use the **real Azure storage account** (`abcretail2026st10531590`).
No emulator is needed. The connection string is already in `local.settings.json`.

---

## Prerequisites

### 1. Install Azure Functions Core Tools
```bash
npm install -g azure-functions-core-tools@4 --unsafe-perm true
func --version   # should print 4.x.x
```

### 2. Trust the dotnet binary (macOS only)
If you see "Apple could not verify ABCRetailWebApp is free of malware":
```bash
xattr -d com.apple.quarantine "/Users/xola.dlamini/Documents/ABCRetailWebApp/abc with azure functions/ABCRetailWebApp/ABCRetailWebApp/bin/Debug/net8.0/ABCRetailWebApp"
```
Or run once via Finder: right-click the binary → Open → Open anyway.
After that, `dotnet run` works normally from the terminal.

### 3. Install curl (already on macOS) or use Postman

---

## Starting the Functions Runtime

```bash
cd "ABCRetailWebApp.Functions"
func start
```

Expected output — you should see all 5 functions listed:
```
Functions:
    BlobUploadFunction: [POST] http://localhost:7071/api/BlobUploadFunction
    TableWriteFunction: [POST] http://localhost:7071/api/TableWriteFunction
    OrderProcessingFunction: queueTrigger
    StockAlertsFunction: queueTrigger
    TransactionLogFunction: queueTrigger
```

Leave this terminal open for all tests below.

---

## Test 1 — TableWriteFunction (HTTP)

**What it does:** Receives a product payload, upserts to the `Products` table,
publishes a message to `product-events` queue, appends to `applogs/logs/product-events-{date}.log`.

### 1a — Happy path

```bash
curl -s -X POST http://localhost:7071/api/TableWriteFunction \
  -H "Content-Type: application/json" \
  -d '{
    "partitionKey": "Electronics",
    "rowKey": "TEST-FUNC-001",
    "productName": "Function Test Product",
    "description": "Created via TableWriteFunction test",
    "price": 99.99,
    "stockQuantity": 50,
    "category": "Electronics",
    "brand": "TestBrand",
    "isAvailable": true,
    "createdAt": "2025-01-01T00:00:00Z"
  }'
```

**Expected response:**
```json
{"success":true,"rowKey":"TEST-FUNC-001"}
```

**Verify in Azure Portal (Storage Account → Tables → Products):**
- Row with PartitionKey=`Electronics`, RowKey=`TEST-FUNC-001` exists

**Verify queue (Storage Account → Queues → product-events):**
- 1 new message with `eventType: "ProductCreated"`

**Verify file log (Storage Account → File shares → applogs → logs):**
- File `product-events-{today}.log` exists and contains a `PRODUCT_CREATED` line

**Verify in func start terminal:**
```
[TableWriteFunction] Product upserted. RowKey: TEST-FUNC-001, Name: Function Test Product
[TableWriteFunction] Published to product-events queue
```

### 1b — Duplicate upsert (idempotency)

Run the exact same curl command again. Should return `200` with the same rowKey.
The row in the Products table is updated (not duplicated) — confirm in Azure Portal.

### 1c — Missing required fields

```bash
curl -s -X POST http://localhost:7071/api/TableWriteFunction \
  -H "Content-Type: application/json" \
  -d '{"productName": "No keys"}'
```

**Expected:** `400 Bad Request` — "RowKey and PartitionKey are required"

### 1d — Invalid JSON

```bash
curl -s -X POST http://localhost:7071/api/TableWriteFunction \
  -H "Content-Type: application/json" \
  -d '{bad json'
```

**Expected:** `400 Bad Request` — "Invalid JSON: ..."

---

## Test 2 — BlobUploadFunction (HTTP)

**What it does:** Receives a multipart form with an image file and productId,
uploads to `productimages` blob container, returns the blob name,
appends to `applogs/logs/product-events-{date}.log`.

### 2a — Happy path (upload a real image)

```bash
# Use any small image on your machine, or create a test one:
echo "fake image bytes" > /tmp/test-product.jpg

curl -s -X POST http://localhost:7071/api/BlobUploadFunction \
  -F "file=@/tmp/test-product.jpg;type=image/jpeg" \
  -F "productId=TEST-FUNC-001" \
  -F "contentType=image/jpeg"
```

**Expected response:**
```json
{"blobName":"product_TEST-FUNC-001.jpg"}
```

**Verify in Azure Portal (Storage Account → Containers → productimages):**
- Blob `product_TEST-FUNC-001.jpg` exists

**Verify file log:**
- `product-events-{today}.log` has a `BLOB_UPLOADED` line with size info

**Verify in func start terminal:**
```
[BlobUploadFunction] Blob uploaded: product_TEST-FUNC-001.jpg (17 bytes)
```

### 2b — No file sent

```bash
curl -s -X POST http://localhost:7071/api/BlobUploadFunction \
  -F "productId=TEST-FUNC-001"
```

**Expected:** `400 Bad Request` — "No file data received"

### 2c — Missing productId

```bash
curl -s -X POST http://localhost:7071/api/BlobUploadFunction \
  -F "file=@/tmp/test-product.jpg;type=image/jpeg"
```

**Expected:** `400 Bad Request` — "productId field is required"

### 2d — Wrong Content-Type (not multipart)

```bash
curl -s -X POST http://localhost:7071/api/BlobUploadFunction \
  -H "Content-Type: application/json" \
  -d '{}'
```

**Expected:** `400 Bad Request` — "Expected multipart/form-data"

---

## Test 3 — OrderProcessingFunction (Queue)

**What it does:** Triggered by a message on `order-processing-queue`,
writes to `OrderAudit` table, publishes to `transaction-queue`.

### 3a — Send a test message manually

In Azure Portal → Storage Account → Queues → `order-processing-queue` → Add message:

```json
{
  "eventType": "OrderPlaced",
  "orderId": "ORD-TEST-FUNC-001",
  "customerId": "test@example.com",
  "customerName": "Test User",
  "customerEmail": "test@example.com",
  "total": 250.00,
  "timestamp": "2025-01-01T10:00:00Z"
}
```

**Wait ~5 seconds**, then verify:

**func start terminal:**
```
[OrderProcessingFunction] OrderProcessing function triggered.
[OrderProcessingFunction] Order audit saved. OrderId: ORD-TEST-FUNC-001
[OrderProcessingFunction] Transaction event published to queue
```

**Azure Portal → Tables → OrderAudit:**
- Row with PartitionKey=`test@example.com`, RowKey starts with `ORD-TEST-FUNC-001`

**Azure Portal → Queues → transaction-queue:**
- 1 new message with `eventType: "OrderProcessed"`

### 3b — Trigger via web app

Start the web app (`dotnet run` in the `ABCRetailWebApp` folder), log in as a customer,
add a product to cart, and complete checkout. Watch the `func start` terminal for
`OrderProcessingFunction` to fire within a few seconds.

---

## Test 4 — StockAlertsFunction (Queue)

**What it does:** Triggered by `stock-alerts-queue`, writes to `StockAlerts` table,
uploads a CSV to `reports` blob container, publishes to `transaction-queue`.

### 4a — Send a test message manually

Azure Portal → Queues → `stock-alerts-queue` → Add message:

```json
{
  "eventType": "OutOfStock",
  "productId": "TEST-FUNC-001",
  "productName": "Function Test Product",
  "category": "Electronics",
  "remainingStock": 0,
  "timestamp": "2025-01-01T10:00:00Z"
}
```

**Wait ~5 seconds**, then verify:

**func start terminal:**
```
[StockAlertsFunction] StockAlerts function triggered.
[StockAlertsFunction] Stock alert saved to table. ProductId: TEST-FUNC-001
[StockAlertsFunction] Stock alert report uploaded to blob: stock-alerts-{date}.csv
[StockAlertsFunction] Transaction event published to queue
```

**Azure Portal → Tables → StockAlerts:**
- Row with PartitionKey=`TEST-FUNC-001`

**Azure Portal → Containers → reports:**
- File `stock-alerts-{today}.csv` exists and is readable

---

## Test 5 — TransactionLogFunction (Queue)

**What it does:** Triggered by `transaction-queue`, writes to `Transactions` table,
appends to `applogs/transactions/transactions-{date}.log`.

This function is triggered automatically by Tests 3 and 4 above (both publish to `transaction-queue`).
After running Test 3 or 4, verify:

**func start terminal:**
```
[TransactionLogFunction] TransactionLog function triggered.
[TransactionLogFunction] Transaction logged to table.
[TransactionLogFunction] Transaction appended to file: transactions-{date}.log
```

**Azure Portal → Tables → Transactions:**
- Row with PartitionKey=`OrderProcessed` or `StockAlertProcessed`

**Azure Portal → File shares → applogs → transactions:**
- File `transactions-{today}.log` with log entries

### 5a — Send a direct test message

Azure Portal → Queues → `transaction-queue` → Add message:

```json
{
  "eventType": "TransactionProcessed",
  "orderId": "ORD-DIRECT-TEST",
  "timestamp": "2025-01-01T10:00:00Z",
  "details": {}
}
```

---

## Test 6 — End-to-End via Web App

This is the full flow test. Run both terminals:

**Terminal 1:**
```bash
cd "ABCRetailWebApp.Functions"
func start
```

**Terminal 2:**
```bash
cd "ABCRetailWebApp/ABCRetailWebApp"
dotnet run
```

Navigate to `http://localhost:5191`, log in as admin, and add a product with an image.

**What to watch in the func start terminal:**
1. `BlobUploadFunction` fires (if image was uploaded and function responded within 3s)
2. `TableWriteFunction` fires shortly after (fire-and-forget, may be 1-2s after redirect)
3. After a customer places an order: `OrderProcessingFunction` fires
4. If stock hits 0: `StockAlertsFunction` fires
5. Both 3 and 4 trigger `TransactionLogFunction` via `transaction-queue`

**What to check in Azure Portal after the full flow:**
| Resource | Expected |
|----------|----------|
| Tables → Products | New product row |
| Tables → OrderAudit | New row per order |
| Tables → StockAlerts | New row if stock depleted |
| Tables → Transactions | Rows for each processed event |
| Containers → productimages | New blob if image uploaded |
| Containers → reports | CSV file if stock alert fired |
| Queues → product-events | Messages for each product created |
| File shares → applogs → logs | `product-events-{date}.log` with entries |
| File shares → applogs → transactions | `transactions-{date}.log` with entries |

---

## Timeout / Fallback Verification

To verify the web app handles a slow/unavailable function gracefully:

1. Stop the `func start` terminal (functions not running)
2. In the web app, add a product with an image
3. **Expected:** Admin is redirected to Inventory immediately (no hang, no error)
4. Product appears in inventory (direct table write succeeded)
5. Image shows as placeholder (BlobUploadFunction was unreachable)
6. Restart `func start` — the TableWriteFunction call will have already timed out and been discarded (this is expected — the product was already written directly)

---

## Cleanup After Testing

Remove test data from Azure Portal:
- Tables → Products → delete row `TEST-FUNC-001`
- Tables → OrderAudit → delete test rows
- Tables → StockAlerts → delete test rows
- Tables → Transactions → delete test rows
- Containers → productimages → delete `product_TEST-FUNC-001.jpg`
- Queues → clear `product-events`, `transaction-queue` if needed
