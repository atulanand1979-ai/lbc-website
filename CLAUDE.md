# CLAUDE.md — Leicester Bhangra Classes (LBC) website

Context for anyone (human or Claude) picking this project up. How-to-run and the full settings table live in `README.md`; this file records **what was agreed, why, and where things stand**.

## The project
Website for Leicester Bhangra Classes ("Britain's largest Bhangra school": weekly classes in 8 English cities, workshops, event performances). Built by a senior .NET developer for the client. Inputs from the client:
- Design template: Claude Design canvas `https://claude.ai/artifact/L9Ei5yU96vLyYANu5Youkp` (9 boards: Home, Workshops, Locations, Sign Up, Book Us, Charity, Reviews, Shop, Contact). Yellow `#FFD400` / black `#0B0B0B`, Anton (display) + Inter (body). Recreated as classes in `src/web/src/css/site.css`.
- Content document `wesbite content page.docx` (page copy, venues, prices, artists, "trusted by", charities, press links). Copy has been transcribed into the `.njk` pages.

## Agreed business rules (from the client via the developer)
- **Pay As You Go £7.50:** customer chooses place → weekly class → date, pays online. Booking for a specific class; **no capacity limits, no cancellations/refunds**. Guest checkout (name, email, phone); confirmation email includes a "create your account" link.
- **Memberships (monthly):** Bronze £25 = unlimited Beginners in **one chosen city** (home venue); Silver £35 = unlimited Beginners, all cities; Gold £45 = Beginners + advanced, all cities, plus perks.
- **Members never book.** They turn up; the teacher checks names. The system does **not** enforce tier/city eligibility.
- **Admin** manages places and weekly schedules ("every Monday 7pm"), skip dates (bank holidays), sees the class register (paid PAYG + eligible members), changes a Bronze member's home city, cancels memberships, reads enquiries. Admin does all member operations.
- Member account area: current membership + upcoming/past PAYG bookings. **No self-service cancel/upgrade** (maybe later; cancellation rules still undecided).
- Not needed: SMS/reminders, under-18 handling, migrating existing members, teacher logins.
- Shop: "coming soon" page only for now (phase 2).

## Technical decisions (and why)
- **Hosting: Azure Static Web Apps Free + managed Functions API + Azure Table Storage** — cheapest option; developer previously used Fly.io for APIs but SWA managed functions cover it at £0.
- **.NET 10 isolated** Functions (`apiRuntime: dotnet-isolated:10.0`) — .NET 8/9 support on SWA ended 10 Nov 2026.
- **Payment gateway is provider-neutral** (`IPaymentGateway`, `src/Lbc.Core/Payments/Payments.cs`). The client may not want Stripe. Gateway only collects money via hosted checkout; **our tables are the source of truth**. Our reference (`BK|classId|yyyyMMdd|bookingId` or `MS|membershipId`) is passed to the gateway and must come back on every webhook. `FakePaymentGateway` + `/dev/fake-pay.html` simulate it locally. With no provider configured, `UnconfiguredPaymentGateway` refuses checkouts.
- **Own auth**: HMAC JWT in an httpOnly/Secure/SameSite=Strict cookie (`lbc_session`), PBKDF2 passwords via ASP.NET Identity's `PasswordHasher`. **No Entra ID** (developer's explicit choice). State-changing endpoints require `application/json` (CSRF defence).
- **Own email**: MailKit over the client's SMTP mailbox. **No Azure Communication Services** (developer's explicit choice).
- **Frontend: plain HTML/CSS + vanilla JS modules, Eleventy only for the shared header/footer layout.** No SPA framework (SEO, simplicity). Developer chose this over Blazor/Astro.
- Admin API routes are under `/api/manage/*` because Azure Functions reserves `/admin`.
- Venues/classes live in Table Storage; `_data/venues.json` is only a build-time fallback for SEO on the Locations page and is replaced at runtime by `/api/venues`.
- Prices charged come from config (`Pricing__*`); prices shown in page copy are text and must be kept in step manually.
- First admin: `POST /api/manage/bootstrap` with header `x-bootstrap-key` = `ADMIN_BOOTSTRAP_KEY`; returns `setPasswordUrl` (needed because email is log-only at first). Refuses once an admin exists. Then `/admin/` → "Load the 8 places" seeds venues from `SeedService`.

## Code map
- `src/Lbc.Core` — entities, services (`CheckoutService`, `PaymentEventProcessor`, `AccountService`, `ScheduleAdminService`, `MemberAdminService`, `CatalogService`, `ScheduleCalculator`), interfaces.
- `src/Lbc.Infrastructure` — Table Storage repos (`Tables/TableRepositories.cs`), JWT/passwords (`Security.cs`), SMTP (`Email.cs`), fake gateway, Turnstile, DI (`DependencyInjection.cs`).
- `src/api/Lbc.Api` — thin HTTP functions; `Http/ApiHost.cs` does JSON parsing, error mapping and cookie auth.
- `src/web` — Eleventy: `src/*.njk` pages, `src/js/*.js`, `src/css/site.css`, `src/staticwebapp.config.json` (CSP, headers, apiRuntime).
- `tests/Lbc.Tests` — xUnit with in-memory fakes (`Fakes.cs`); run `dotnet test`.
- Pinned versions: Microsoft.Extensions.* **10.0.x** (not 11 — must match the ASP.NET Core 10 shared framework in the Functions worker); MailKit 5 (`SendAsync(new SendRequest(msg))`); Eleventy 3.1.8.

## Infrastructure (as of 2026-11-22)
- GitHub: private repo `https://github.com/atulanand1979-ai/lbc-website`, branch `main`. Workflow `.github/workflows/azure-static-web-apps.yml` tests, builds site + API itself (`skip_app_build`/`skip_api_build`) and deploys. **Manual only** (`workflow_dispatch`, developer's choice): pushes don't deploy and there are no PR preview environments — run it from Actions → "Build and deploy" → Run workflow.
- Azure subscription "Azure subscription 1", resource group **`rg-lbc`**:
  - Static Web App **`swa-lbc`** (Free, West Europe) — `https://kind-mud-081113903.3.azurestaticapps.net`. Created via CLI with **no linked repo** (portal "GitHub" source caused a repositoryUrl template error).
  - Storage account **`stlbc2d8cxf9r`** (Standard LRS, UK South).
  - SWA app settings set: `TABLES_CONNECTION_STRING`, `Jwt__SigningKey`, `ADMIN_BOOTSTRAP_KEY`, `Site__BaseUrl`, `Site__AdminEmail`, `Smtp__Mode=Log`. Read values with `az staticwebapp appsettings list -n swa-lbc -g rg-lbc` — never commit them.
- Repo commits use the developer's personal email (set in local git config), not the work email.

## Status / next steps
1. **Pending (developer):** add GitHub secret `AZURE_STATIC_WEB_APPS_API_TOKEN` (from swa-lbc → Manage deployment token) and run the workflow manually. First deploy not yet confirmed.
2. After deploy: bootstrap the admin, seed the 8 places, smoke-test the live site.
3. Choose and implement the real payment provider (blocks go-live).
4. Get the client's SMTP mailbox details → set `Smtp__*`, switch `Smtp__Mode=Smtp`.
5. Custom domain (client currently uses leicesterbhangraclasses.co.uk) → update `Site__BaseUrl`.
6. Privacy policy, cookie notice, terms (UK GDPR); confirm VAT status.
7. Optional: Cloudflare Turnstile keys for form anti-spam.

## Content the client must confirm
- Hike amount for Khalsa Aid (doc says "AMAN ADD THE AMOUNT HERE") — placeholder on Charity page.
- Derby start date assumed **4 Feb 2027** (doc: "starting in February") — editable in admin.
- Gold copy says "advanced" but timetable says "Intermediate".
- Names tidied from the doc: "Coppa Feel"/"Copper Feel" → "CoppaFeel!", "Leicester council" → "Leicester City Council", DWP name, Coventry neonatal ward → "UHCW Neonatal Ward".
- Placeholders: photos, brand logos, student reviews, social links, shop products, YouTube titles.

## Local development notes
- Run steps are in `README.md` (Azurite + `func start` + `npm run watch` + `swa start`).
- This Windows machine had **Smart App Control** blocking locally built DLLs (`0x800711C7`); the developer turned it off. `docker-compose.yml` runs Azurite + API in Linux containers as a fallback.
- The machine can run low on memory; background dev servers may be stopped by Claude Code. Docker Desktop isn't needed now.
- GitHub CLI device login timed out from the `!` shell; pushing works through Git Credential Manager instead.
