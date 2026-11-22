# CLAUDE.md — Leicester Bhangra Classes (LBC) website

Context for anyone (human or Claude) picking this project up. How-to-run and the full settings table live in `README.md`; this file records **what was agreed, why, and where things stand**.

## Working rules (always follow — main session or the `lbc` agent)
1. **Live data needs explicit approval.** Before any add, update or delete against the live Azure tables (storage account `stlbc2d8cxf9r` — via scripts, `az storage`, the live `/api/manage/*` endpoints, or anything else), state exactly which rows/places/classes/members will change and get the developer's "yes" for *that* change. Approval for one change does not cover the next. Reading live data is fine without asking. Local Azurite data is not live.
2. **Live settings and deploys need approval too:** changing SWA app settings, Azure resources, or running the deploy workflow.
3. **Keep this file current — but only for changes that are kept.** The developer often tries changes and discards them, so:
   - **Code/content changes:** don't touch this file while working. Update it only when the developer asks to commit and push, and include the update in that same commit. Discarded changes never reach this file.
   - **Live data / settings / Azure changes** (no push involved; they take effect immediately): update this file once the approved change has been applied successfully.
   - What to update: the relevant sections (rules, decisions, infrastructure, status, content to confirm), plus a dated line in the change log at the bottom. Remove anything no longer true. Never record secrets, tokens or connection strings.
4. Commit/push only when asked. Commits use the developer's personal email (already in local git config).
5. Never print secrets in chat; read them from `az staticwebapp appsettings list -n swa-lbc -g rg-lbc` into variables.
6. `.claude/settings.json` has `ask` permission rules (az storage, appsettings set/delete, resource deletes, `gh workflow run`, `dotnet run`, anything naming the storage account, `TABLES_CONNECTION_STRING`/`LBC_TABLES`, or the live `/api/manage` URL) so these always prompt. Don't work around them; add a rule if a new live-write path appears.
7. One-off data scripts go in the session scratchpad, not the repo. Pattern that works: a .NET file-based app (`dotnet run x.cs`) with `#:project ...\Lbc.Infrastructure.csproj`, `#:package Microsoft.Extensions.*@10.0.15`, `#:property PublishAot=false`, connection string from env var, calling `ScheduleAdminService`/repositories so rows match the app's shape.

## The project
Website for Leicester Bhangra Classes ("Britain's largest Bhangra school": weekly classes in 8 English cities, workshops, event performances). Built by a senior .NET developer for the client. Inputs from the client:
- Design template: Claude Design canvas `https://claude.ai/artifact/L9Ei5yU96vLyYANu5Youkp` (9 boards: Home, Workshops, Locations, Sign Up, Book Us, Charity, Reviews, Shop, Contact). Yellow `#FFD400` / black `#0B0B0B`, Anton (display) + Inter (body). Recreated as classes in `src/web/src/css/site.css`.
- Content document `wesbite content page.docx` (page copy, venues, prices, artists, "trusted by", charities, press links). Copy has been transcribed into the `.njk` pages.

## Agreed business rules (from the client via the developer)
- **Pay As You Go £7.50:** customer chooses place → weekly class → date and is **booked straight away — no online payment; they pay at the class** (status `PayAtClass`; developer's change 2026-11-22, client to confirm and say how the teacher takes payment). Booking for a specific class; **no capacity limits**; one booking per email per class date. Guest booking (name, email, phone); confirmation email (sent immediately) says "pay at the class" and includes a "create your account" link. The old pay-online PAYG path (`Pending`→`Paid` via `OneOffPaid` webhook) is still in `PaymentEventProcessor` but nothing starts it.
- **Memberships (monthly):** Bronze £25 = unlimited Beginners in **one chosen city** (home venue); Silver £35 = unlimited Beginners, all cities; Gold £45 = Beginners + advanced, all cities, plus perks.
- **Members never book.** They turn up; the teacher checks names. The system does **not** enforce tier/city eligibility.
- **Admin** manages places and weekly schedules ("every Monday 7pm"), skip dates (bank holidays), sees the class register (confirmed PAYG with "Collect £7.50" for pay-at-class + eligible members), changes a Bronze member's home city, cancels memberships, reads enquiries. Admin does all member operations.
- Member account area: current membership + upcoming/past PAYG bookings. **No self-service cancel/upgrade** (maybe later; cancellation rules still undecided).
- Not needed: SMS/reminders, under-18 handling, migrating existing members, teacher logins.
- Shop: "coming soon" page only for now (phase 2).

## Technical decisions (and why)
- **Hosting: Azure Static Web Apps Free + managed Functions API + Azure Table Storage** — cheapest option; developer previously used Fly.io for APIs but SWA managed functions cover it at £0.
- **.NET 10 isolated** Functions (`apiRuntime: dotnet-isolated:10.0`) — .NET 8/9 support on SWA ended 10 Nov 2026.
- **Payment gateway is provider-neutral** (now only used for memberships) (`IPaymentGateway`, `src/Lbc.Core/Payments/Payments.cs`). Gateway only collects money via hosted checkout; **our tables are the source of truth**. Our reference (`BK|classId|yyyyMMdd|bookingId` or `MS|membershipId`) is passed to the gateway and must come back on every webhook (`ParseWebhookAsync` returns a list — providers batch events). With no provider configured, `UnconfiguredPaymentGateway` refuses checkouts.
- **Memberships: GoCardless Bacs Direct Debit** (developer's choice 2026-11-22 — cheapest, ~1% + 20p, free recurring). `GoCardlessPaymentGateway` (raw REST, no SDK): Billing Request (mandate only, ref + amount in metadata) + hosted Billing Request Flow → webhook `billing_requests.fulfilled` creates the monthly Subscription (idempotency key = mandate id) → `RecurringStarted`. Payments confirmed/failed → renewed/failed; subscription/mandate cancelled → cancelled. **Membership is Active as soon as the Direct Debit is set up** (not after the first payment clears — client to confirm). Collection day = GoCardless default. If set-up completes after admin cancelled, the processor cancels the subscription. Coded against GoCardless docs and stubbed tests; **not yet run against the real sandbox**.
- `Payments__Provider=Fake`: memberships go to `/dev/fake-direct-debit.html` (neutral demo of the Direct Debit page for showing the client; bank details are never sent) and PAYG legacy to `/dev/fake-pay.html`.
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
- GitHub: private repo `https://github.com/atulanand1979-ai/lbc-website`, branch `main`. Workflow `.github/workflows/azure-static-web-apps.yml` tests, builds site + API itself (`skip_app_build`/`skip_api_build`) and deploys. **Deploys on every push to `main`** (temporary, developer's choice while testing — switch back to manual-only `workflow_dispatch` later) and can also be run from Actions → "Build and deploy" → Run workflow. No PR preview environments.
- Azure subscription "Azure subscription 1", resource group **`rg-lbc`**:
  - Static Web App **`swa-lbc`** (Free, West Europe) — `https://kind-mud-081113903.3.azurestaticapps.net`. Created via CLI with **no linked repo** (portal "GitHub" source caused a repositoryUrl template error).
  - Storage account **`stlbc2d8cxf9r`** (Standard LRS, UK South).
  - SWA app settings set: `TABLES_CONNECTION_STRING`, `Jwt__SigningKey`, `ADMIN_BOOTSTRAP_KEY`, `Site__BaseUrl`, `Site__AdminEmail` (developer's gmail for now), `Smtp__*` (Gmail), `Payments__Provider=Fake`, `Payments__FakeWebhookSecret`. Read values with `az staticwebapp appsettings list -n swa-lbc -g rg-lbc` — never commit them.
  - **Email: dedicated Gmail `classesleicesterbhangra@gmail.com`** via `smtp.gmail.com:587` StartTls with an app password (set by the developer; never handle it in chat). Gmail forces From = that address.
  - ⚠️ **`Smtp__RedirectAllTo` = developer's gmail** while testing: every system email (customer ones too) goes to the developer, subject prefixed `[to <real recipient>]`. Remove before go-live.
  - ⚠️ **Fake payments are ON in production** for testing: anyone with the URL can mark a booking/membership paid. Remove `Payments__Provider` before the client shares the URL.
- Repo commits use the developer's personal email (set in local git config), not the work email.

## Status / next steps
1. ✅ Deployed (site + API + Table Storage working). Admin account created (developer's gmail) via bootstrap; `ADMIN_BOOTSTRAP_KEY` can now be deleted.
2. ✅ Live places: **Leicester, Bedford, Kettering only** (developer's choice — the other 5 from `SeedService` are not loaded; add them via Admin → Places when wanted. The "Load the 8 places" button is a no-op once any place exists). Next: smoke-test PAYG (pay at class) + membership with the fake gateway.
3. Payments: GoCardless integrated in code. Client is applying for a GoCardless account (in the client's name; developer to be added as a team member). Next: test against a sandbox account, then set `Payments__Provider=GoCardless`, `Payments__GoCardless__*` and register `<base>/api/payments/webhook` in GoCardless (live-settings approval needed). Blocks go-live.
4. Email: sending from a dedicated Gmail for testing, all redirected to the developer. Before go-live: decide the final mailbox (client's own or the Gmail), remove `Smtp__RedirectAllTo`, set `Site__AdminEmail` to the client's inbox. No way yet to add more admins (bootstrap only works once) — needs a script or an admin-area feature.
5. Custom domain (client currently uses leicesterbhangraclasses.co.uk) → update `Site__BaseUrl`.
6. Privacy policy, cookie notice, terms (UK GDPR); confirm VAT status.
7. Cloudflare Turnstile keys for form anti-spam — more important now PAYG bookings need no payment.

## Copy style
- **No em dashes (—) in anything visible** (pages, page descriptions, JS messages, emails, API errors): developer's request 2026-11-22. Use a full stop, comma, colon or brackets; `-` for empty table cells. Quote front-matter `description:` values that contain `: `. Save source files as UTF-8 (the email templates once had garbled `â€”` from a bad encoding).

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

## Change log
- 2026-11-22 — First deploy confirmed. Admin bootstrapped. Fake payments enabled on live. Loaded Leicester, Bedford, Kettering (with their weekly classes) into live tables. Added working rules + `lbc` agent (`.claude/agents/lbc.md`).
- 2026-11-22 — Real email on: Gmail `classesleicesterbhangra@gmail.com` SMTP settings, new `Smtp__RedirectAllTo` option (all mail → developer while testing), `Site__AdminEmail` → developer's gmail. Deployed, then `Smtp__Mode=Smtp`; test forgot-password email arrived (in spam at first — new sender).
- 2026-11-22 — PAYG changed to "book now, pay at the class": no gateway page, confirmation email sent immediately, register shows who to collect from, duplicate bookings refused. Workflow now deploys on every push to `main` (temporary).
- 2026-11-22 — Fixed Sign out on live: the logout Set-Cookie never reached the browser when the cookie was already expired (`Cookies.Delete`, and `Max-Age=0` didn't help either — suspected Functions-host behaviour). Logout now overwrites `lbc_session` with an invalid value and no expiry; the account page checks `/api/me` afterwards and warns if still signed in.
- 2026-11-22 — Memberships via GoCardless Direct Debit: `GoCardlessPaymentGateway`, webhooks parsed as a batch, demo Direct Debit page `/dev/fake-direct-debit.html` for the fake provider, join page copy says Direct Debit. Not yet tested against the GoCardless sandbox.
- 2026-11-22 — Fixed garbled `â€”` in every email (mis-encoded em dashes in `EmailTemplates.cs`). Removed all visible em dashes from the site, emails and messages (new Copy style rule).
