# ABC Retail Web App — User Manual

## Requirements
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) installed on your machine
- Internet connection (the app connects to Azure Storage — no local database needed)

---

## How to Run

1. Unzip the project folder
2. Open a terminal and navigate into the project folder:
   ```
   cd ABCRetailWebApp/ABCRetailWebApp
   ```
3. Run the app:
   ```
   dotnet run
   ```
4. Open your browser and go to:
   ```
   http://localhost:5191
   ```

That's it. The app will automatically seed all products, customers and orders on first run.

---

## Demo Credentials

| Role | Email | Password |
|------|-------|----------|
| Admin | admin@abcretail.co.za | Admin@1234 |
| Customer | thabo.nkosi@gmail.com | Customer@1234 |

> On the Login page, click either row to auto-fill the form.

---

## Features

### Customer
- Browse 15 products across 5 categories (Clothing, Electronics, Accessories, Footwear, Gifts)
- Filter products by category
- Add items to cart, update quantities, remove items
- Checkout with a simulated card payment
- View order history and track order status under **My Orders**

### Admin (log in as Admin)
- **Dashboard** — live stats: customers, inventory, pending orders, audit log entries
- **Inventory** — view all products, add new products, delete products
- **Customers** — view all registered customers
- **Order Queue** — process pending orders (updates order status to Shipped for the customer)
- **System Logs** — view application logs written to Azure File Storage

---

## Processing an Order (Demo Flow)

1. Log in as **Admin** → go to **Admin Portal → Order Queue**
2. You will see pending orders from Thabo Nkosi
3. Click **Process** on a message — a green toast confirms it was processed
4. Open a new tab, log in as **thabo.nkosi@gmail.com**
5. Go to **My Orders** — the order status has updated to **Shipped**

---

## Notes
- All data is stored in Azure Table Storage — no local database setup required
- Product images are loaded from Unsplash (internet connection required for images)
- The seeder runs on every startup with `overwrite: true` — demo data is always fresh
- Do not expose `appsettings.json` publicly as it contains the Azure Storage connection string
