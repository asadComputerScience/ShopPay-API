# ShopPay API

A small e-commerce backend that lets a customer buy products and pay for them with **Stripe Checkout**.
It is a portfolio project that shows the full payment lifecycle: create an order, send the customer to
Stripe's hosted payment page, and confirm the payment through a **signed webhook**.

> **Test mode only.** The app refuses to start a checkout unless the Stripe key starts with `sk_test_`
> (or `rk_test_`). No real money can ever move.

## What it does

| Method | Route               | Description |
|--------|---------------------|-------------|
| `GET`  | `/products`         | Lists the catalogue (4 sample products are seeded on first run). |
| `POST` | `/checkout`         | Creates an order with status `Pending` and a Stripe Checkout Session. Returns the `paymentUrl`. |
| `GET`  | `/orders/{id}`      | Returns an order and its status (`Pending`, `Paid` or `Cancelled`). |
| `POST` | `/webhooks/stripe`  | Called by Stripe. Verifies the signature, then on `checkout.session.completed` marks the order `Paid`. |

The flow end to end:

```
Client            ShopPay API                       Stripe
  |  POST /checkout    |                               |
  |------------------->|  create Order (Pending)       |
  |                    |  create Checkout Session ---->|
  |  201 + paymentUrl  |<------------------------------|
  |<-------------------|                               |
  |  customer pays on the Stripe page ---------------->|
  |                    |<-- POST /webhooks/stripe -----|  (signed)
  |                    |  verify signature             |
  |                    |  Order -> Paid                |
  |  GET /orders/{id}  |                               |
  |------------------->|  { "status": "Paid" }         |
```

## Tech stack

- **ASP.NET Core Web API** on **.NET 10** (the current LTS release), controllers
- **Entity Framework Core 10** with **SQLite** (the database file is created automatically)
- **Stripe.net** (Stripe Checkout + webhook signature verification), test mode only
- **Swagger UI** (Swashbuckle) for interactive API docs
- **xUnit** for unit tests

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`dotnet --version` should print `10.x`)
- A free [Stripe account](https://dashboard.stripe.com/register). Stay in **Test mode** (toggle in the dashboard)
- The [Stripe CLI](https://docs.stripe.com/stripe-cli) to receive webhooks on your own machine

## Run it

### 1. Restore and build

```bash
dotnet restore
dotnet build
```

### 2. Add your Stripe test keys (stored outside the repository)

Keys are read from configuration. The committed `appsettings.json` only contains `<placeholders>`;
your real keys go into [.NET user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets),
which live in your user profile and can never be committed by accident.

```bash
cd src/ShopPay.Api
dotnet user-secrets set "Stripe:SecretKey" "sk_test_..."     # Dashboard > Developers > API keys
dotnet user-secrets set "Stripe:WebhookSecret" "whsec_..."   # printed by `stripe listen`, see below
cd ../..
```

Prefer environment variables? `Stripe__SecretKey` and `Stripe__WebhookSecret` work too.

Until the keys are set the API still starts: `GET /products` works, while `POST /checkout` and the
webhook answer `503 Service Unavailable` with an explanation.

### 3. Start the API

```bash
dotnet run --project src/ShopPay.Api
```

Open **http://localhost:5062/swagger** for the interactive docs. The SQLite database (`shoppay.db`) is created
and seeded on first start. It is git-ignored.

### 4. Try it

In Swagger UI (or any HTTP client):

1. `GET /products` and pick some product ids.
2. `POST /checkout` with a body like:

   ```json
   { "items": [ { "productId": 1, "quantity": 2 }, { "productId": 3, "quantity": 1 } ] }
   ```

   The response contains `orderId` and `paymentUrl`.
3. Open `paymentUrl` in a browser and pay with the Stripe test card **4242 4242 4242 4242**
   (any future expiry date, any 3-digit CVC, any postcode).
4. `GET /orders/{orderId}`. Once the webhook has arrived (next section) the status is `Paid`.

Example with curl:

```bash
curl -X POST http://localhost:5062/checkout \
  -H "Content-Type: application/json" \
  -d '{"items":[{"productId":1,"quantity":2}]}'
```

Example with PowerShell:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5062/checkout `
  -ContentType 'application/json' `
  -Body '{"items":[{"productId":1,"quantity":2}]}'
```

## Testing the webhook with the Stripe CLI

Stripe cannot reach `localhost`, so the Stripe CLI acts as a tunnel: it receives events from Stripe and
forwards them to your machine, signed with a secret it prints for you.

1. **Log in once** (opens a browser to pair the CLI with your test-mode account):

   ```bash
   stripe login
   ```

2. **Start forwarding** in a second terminal and leave it running:

   ```bash
   stripe listen --forward-to localhost:5062/webhooks/stripe
   ```

   It prints something like `Ready! Your webhook signing secret is whsec_1234...`. Copy that value.

3. **Give the secret to the API** and restart it, so it can verify signatures:

   ```bash
   cd src/ShopPay.Api
   dotnet user-secrets set "Stripe:WebhookSecret" "whsec_1234..."
   cd ../..
   dotnet run --project src/ShopPay.Api
   ```

4. **Pay for an order** as described in "Try it" above. In the `stripe listen` terminal you will see:

   ```
   --> checkout.session.completed [evt_...]
   <-- [200] POST http://localhost:5062/webhooks/stripe [evt_...]
   ```

   The API log shows `Order <id> marked as Paid`, and `GET /orders/{id}` now returns `"status": "Paid"`.

Useful extras:

- **Signature check.** A request that does not come from Stripe is rejected:

  ```bash
  curl -i -X POST http://localhost:5062/webhooks/stripe -H "Stripe-Signature: t=1,v1=fake" -d "{}"
  # HTTP/1.1 400  (Invalid webhook signature)
  ```

- **`stripe trigger checkout.session.completed`** sends a *sample* event. It proves signature verification and
  routing work (you get a `200`), but the sample session does not belong to one of your orders, so no order changes.
  To see an order flip to `Paid`, pay for a real checkout as in step 4.
- **Replay an event** (webhooks are delivered at least once, so the handler is idempotent):
  `stripe events resend evt_...`. The order stays `Paid` and the timestamp does not change.

## Configuration reference

| Key                     | Where to set it   | Purpose |
|-------------------------|-------------------|---------|
| `Stripe:SecretKey`      | user-secrets / env | Your `sk_test_...` key. |
| `Stripe:WebhookSecret`  | user-secrets / env | The `whsec_...` signing secret from `stripe listen`. |
| `Stripe:Currency`       | `appsettings.json` | Currency for all orders (default `usd`). |
| `Stripe:PublicBaseUrl`  | `appsettings.json` | Base URL Stripe redirects the customer back to after paying (default `http://localhost:5062`). |
| `ConnectionStrings:ShopPay` | `appsettings.json` | SQLite connection string (default `Data Source=shoppay.db`). |

## Run the tests

```bash
dotnet test
```

The tests never call Stripe and need no keys:

- `OrderTests`: the order rules on their own: total calculation, `Pending -> Paid`, double-payment safety,
  wrong amount / currency / session rejected, cancelled and paid orders protected.
- `OrderServiceTests`: the checkout and payment flow against an in-memory SQLite database with a fake payment
  gateway: order + session creation, unknown products, merged duplicate lines, cancelling the order when Stripe
  fails, and marking an order paid (twice, to prove it is idempotent).

## Project layout

```
src/ShopPay.Api
  Controllers/      Thin HTTP layer: Products, Checkout, Orders, StripeWebhook
  Contracts/        Request / response shapes (with validation rules) exposed by the API
  Domain/           Product, Order, OrderItem and the business rules of an order's life cycle
  Data/             EF Core DbContext + seed data
  Services/         OrderService (checkout & payment flow) and StripeWebhookHandler
  Payments/         IPaymentGateway + the Stripe implementation
  Configuration/    Strongly-typed Stripe settings
  Infrastructure/   Global exception handler (consistent JSON errors)
tests/ShopPay.Tests xUnit tests
```

## Design notes

- **Money is stored as integer cents** (`long`), never as floating point.
- **The webhook is the source of truth for payment.** Redirecting the customer back from Stripe proves nothing
  (anyone can open that URL); only the signature-verified webhook marks an order `Paid`.
- **The webhook is idempotent.** Stripe may deliver an event more than once. Paying an already-paid order is a no-op.
- **Amount, currency and session are re-checked** against the order before it is marked paid.
- **The raw request body** is used for signature verification, because the signature covers the exact bytes Stripe sent.
- **Stripe is behind `IPaymentGateway`**, so the order logic is testable without the network.
- **No orphan orders.** If Stripe fails after the order was saved, the order is set to `Cancelled`.
- **Errors are consistent** (RFC 7807 problem details): `400` validation, `404` not found, `409` state conflict,
  `502` Stripe failure, `503` Stripe not configured, `500` unexpected (details are logged, not leaked).
- `EnsureCreated()` builds the database on start-up to keep the demo simple. A production system would use
  EF Core migrations.

## Possible next steps

- Authentication and customer accounts
- EF Core migrations and a production database (PostgreSQL / SQL Server)
- Handle `checkout.session.async_payment_succeeded` for delayed payment methods
- Refunds and a `GET /orders` listing
- Integration tests with `WebApplicationFactory`
