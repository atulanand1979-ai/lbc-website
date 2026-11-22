# Leicester Bhangra Classes – website

Static site (plain HTML/CSS/JS, Eleventy for the shared header/footer) + C# Azure Functions API + Azure Table Storage, hosted on Azure Static Web Apps (Free).

```
src/web/                 Eleventy source (src/) -> _site/  (SWA app)
src/api/Lbc.Api/         Azure Functions, .NET 10 isolated (SWA managed API)
src/Lbc.Core/            Domain, services, IPaymentGateway (provider-neutral)
src/Lbc.Infrastructure/  Table Storage repos, SMTP (MailKit), JWT, fake gateway
tests/Lbc.Tests/         xUnit
```

## Run locally

Prereqs: .NET 10 SDK, Node 20+, Azure Functions Core Tools v4, `npm i -g azurite @azure/static-web-apps-cli`.

```powershell
azurite --silent --location .azurite                          # terminal 1: storage emulator
cd src/api/Lbc.Api; func start --port 7071                    # terminal 2: API (uses local.settings.json)
cd src/web; npm install; npm run watch                        # terminal 3: rebuild site on change
swa start src/web/_site --api-devserver-url http://localhost:7071   # terminal 4: http://localhost:4280
```

If Windows Smart App Control blocks the locally built DLLs, use `docker compose up --build` for Azurite + API instead of terminals 1–2.

`local.settings.json` is git-ignored; it holds dev-only secrets. Locally, emails are written to the Functions log (`Smtp__Mode=Log`) and payments use the fake gateway (`Payments__Provider=Fake`), which opens `/dev/fake-pay.html` instead of a real checkout.

### First admin
```powershell
curl -X POST http://localhost:4280/api/manage/bootstrap -H "x-bootstrap-key: <ADMIN_BOOTSTRAP_KEY>" -H "Content-Type: application/json" -d '{"email":"you@example.com","name":"Your Name"}'
```
Follow the set-password link (in the log locally / your inbox in production), then open `/admin/` → **Load the 8 places from the content document**, or add places and weekly classes by hand. Bootstrap stops working once an admin exists.

## Tests
```powershell
dotnet test
```

## Configuration (SWA → Environment variables in production)

| Setting | Purpose |
|---|---|
| `TABLES_CONNECTION_STRING` | Storage account connection string |
| `Jwt__SigningKey` | ≥32 random characters; signs the session cookie |
| `Site__BaseUrl` | e.g. `https://www.leicesterbhangraclasses.co.uk` (links in emails, payment redirects) |
| `Site__AdminEmail` | Inbox for enquiries |
| `Smtp__Mode` = `Smtp`, `Smtp__Host`, `Smtp__Port`, `Smtp__Security` (`StartTls`/`SslOnConnect`), `Smtp__Username`, `Smtp__Password`, `Smtp__FromAddress`, `Smtp__FromName` | Sending mailbox (Gmail: `smtp.gmail.com`, 587, `StartTls`, app password; From must be the Gmail address) |
| `Smtp__RedirectAllTo` | Testing only: every email goes to this address, subject prefixed with the real recipient. Remove before go-live |
| `Payments__Provider` | `Fake` locally. **No real provider yet** – checkouts are refused until one is added |
| `Turnstile__SiteKey`, `Turnstile__SecretKey` | Optional Cloudflare Turnstile anti-spam on public forms |
| `ADMIN_BOOTSTRAP_KEY` | One-off first-admin creation (remove after use) |
| `Pricing__PayAsYouGoPence` / `BronzePence` / `SilverPence` / `GoldPence` | Defaults 750 / 2500 / 3500 / 4500. The prices shown in the page copy (`signup.njk`, `join/index.njk`) are text – keep them in step |

## Adding the real payment gateway
Implement `IPaymentGateway` (`src/Lbc.Core/Payments/Payments.cs`) using the provider's hosted checkout, pass our `Reference` through as metadata, map its webhooks to `PaymentEvent`s in `ParseWebhookAsync` (verify the signature there), and register it in `src/Lbc.Infrastructure/DependencyInjection.cs`. Point the provider's webhook at `/api/payments/webhook`.
