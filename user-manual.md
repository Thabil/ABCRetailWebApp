# ABC Retail Web App — User Manual

## Requirements
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- Internet connection (app connects to Azure Storage — no local database needed)

---

## How to Run

1. Unzip the project folder
2. Open a terminal and navigate into the project:
   ```
   cd ABCRetailWebApp/ABCRetailWebApp
   ```
3. Run the app:
   ```
   dotnet run
   ```
4. Open your browser at:
   ```
   http://localhost:5191
   ```

Products, customers and orders are seeded automatically on first run.

---

## Demo Credentials

| Role     | Email                      | Password       |
|----------|----------------------------|----------------|
| Admin    | admin@abcretail.co.za      | Admin@1234     |
| Customer | thabo.nkosi@gmail.com      | Customer@1234  |

> On the Login page, click either row to auto-fill the form.

---

## Features

### As a Customer
- Browse 15 products across 5 categories (Clothing, Electronics, Accessories, Footwear, Gifts)
- Filter by category using the pills at the top of the Shop page
- Add items to cart, adjust quantities, remove items
- Checkout with a simulated card payment
- View order history and status under **My Orders**

### As an Admin
- **Dashboard** — live stats: customers, inventory, pending orders, audit log entries
- **Inventory** — view, add, and delete products
- **Customers** — view all registered customers
- **Order Queue** — process pending orders (status updates to Shipped for the customer)
- **System Logs** — view app logs written to Azure File Storage

---

## Demo Flow: Processing an Order

1. Log in as **Admin** → **Admin Portal → Order Queue**
2. Pending orders from Thabo Nkosi will be listed
3. Click **Process** — a green toast confirms success
4. In a new tab, log in as **thabo.nkosi@gmail.com**
5. Go to **My Orders** — status now shows **Shipped**

---

## Notes
- All data lives in Azure Table Storage — no local DB setup needed
- Product images load from Unsplash — internet required for images to show
- Demo data is refreshed on every startup
- Keep `appsettings.json` private — it contains the Azure Storage connection string
