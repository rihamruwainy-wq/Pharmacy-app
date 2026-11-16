# Medora — pharmacy app

React Native (Expo SDK 58) · ASP.NET Core 10 · SQL Server · Claude (staff-only AI)

The business rules of the Staybnb (Airbnb clone) project, adapted to a pharmacy:

| Airbnb clone | Medora |
|---|---|
| Listings, category bar, search | Product catalogue: categories, search, sort, branch-aware stock |
| 15-min booking hold, conflict check, idempotency key | 15-min **stock reservation** (atomic conditional SQL update + CHECK constraint), idempotent order creation |
| Booking state machine | `pending_payment → awaiting_verification (Rx) → preparing → ready_for_pickup / out_for_delivery → completed`, plus `expired`, `cancelled`, `rejected` |
| Cancellation policy with refunds | Stage-based refunds: full before preparation, minus service fee while preparing, none once handed over; pharmacist rejection = full refund |
| Price recomputed server-side (cents) | Subtotal + delivery (free over $50) + 5% service fee (clamped) + Rx handling fee per prescription line |
| Experiences booked per slot | In-store care services (vaccines, BP/glucose checks, medication reviews) with per-slot capacity, 2-hour free cancellation |
| Wishlists, refresh-token rotation, lockout, outbox email, hold-expiry worker | Same, plus **roles** (customer / pharmacist / admin), per-branch pharmacist scope, and an **audit log** of regulated actions |

## AI (pharmacist and admin only)

- **Pharmacist clinical copilot (RAG).** Questions are answered from the pharmacy's drug-monograph knowledge base (BM25 retrieval, boosted toward the drugs in the order under review). Sources are sent to Claude as citable documents, so every claim comes back linked to the passage it relies on.
  - **Deterministic safety screening.** Drug–drug interactions (curated table) and allergy-class checks are computed by the server, never by the model. The model explains alerts; it can't create or dismiss one.
  - **De-identified context.** The model sees age, allergies and medicines, never names or contact details.
- **Admin analytics assistant (tool use).** A bounded agent loop over read-only, range-limited server functions: sales summary, order status breakdown, top products, low stock, expiring stock, branches. It has no SQL access and no write access.
- **Swappable provider.** The model is configuration, not code. `Ai:Provider` takes one of these values:

  | `Ai:Provider` | Use for | Settings |
  |---|---|---|
  | `openai` | Any OpenAI-compatible endpoint. **Free and local:** Ollama (the development default is `qwen2.5:3b` on this PC's CPU), LM Studio, vLLM. **Hosted:** OpenAI, Azure OpenAI, Groq, OpenRouter, Together. | `Ai:BaseUrl`, `Ai:Model`, `Ai:ApiKey` (not needed locally) |
  | `claude` | Anthropic, through the official C# SDK: native citations, server-side refusal fallbacks, prompt caching. Uses `claude-opus-5-5` by default. | `Claude:ApiKey` (or `ANTHROPIC_API_KEY`), `Claude:Model` |
  | `none` | AI turned off. The production default until a provider is chosen. | |

  OpenAI-style APIs have no native citations, so that adapter numbers the sources `[S1]`… and asks the model to cite them. It then **verifies** each one: citations pointing at sources that don't exist are dropped, and each kept citation is paired with the sentence from its source that best matches the claim.

  To switch to a customer's subscription, set it in user-secrets, environment variables or Key Vault, not in `appsettings`. For example: `Ai__Provider=openai`, `Ai__BaseUrl=https://api.openai.com/v1`, `Ai__Model=<model>`, `Ai__ApiKey=<key>`.
- **Auditing and limits.** Each AI call is written to the audit log with its token usage, and is rate-limited per user (10 per minute).
- **If the provider is off or unreachable.** The copilot still returns its alerts and the relevant sources, and the API answers `503 AI_UNAVAILABLE` instead of crashing.
- **Demo content.** The knowledge base in `backend/src/Medora.Infrastructure/Persistence/Seed/knowledge.json` and `interactions.json` is **illustrative demo content**. It must be replaced with a licensed drug database or checked against official product information before any clinical use.

## Performance, caching and request handling

**Server**
- **Responses:** ETags with `304 Not Modified`, Brotli/Gzip compression, and source-validated JSON with null fields omitted.
- **Caching:** output cache plus HybridCache (L1 in-process, L2 optional) with tag invalidation. Repricing evicts both layers.
- **Database:** pooled DbContexts, projection-only no-tracking reads, filtered and covering indexes, rowversion concurrency, a retrying execution strategy, and set-based stock updates taken in a fixed order to avoid deadlocks.
- **Rate limits:** global, auth, writes and AI.
- **Errors and tracing:** problem+json errors with machine codes, correlation IDs, and OpenTelemetry.

**App**
- **Query cache:** TanStack Query with `offlineFirst`, freshness tuned per resource, and retries only on transient errors (honouring `Retry-After`). Polling runs only while an order is in progress, and queries refetch when the app returns to the foreground.
- **Persisted cache:** written to disk for **public catalogue data only**; health data is never written to AsyncStorage.
- **HTTP client:** timeouts, ETag revalidation, a single shared token refresh, idempotency keys with jittered backoff, and Zod-validated responses.
- **Rendering:** FlashList virtualisation, memoised rows, `expo-image` memory and disk caching, prefetch on press-in, deferred search with request cancellation, optimistic wishlist hearts, and tokens kept in SecureStore.

## Design system, languages and accessibility

- **Look: "heritage apothecary, modern finish".**
  - **Colours:** a cream ground, ink text, bottle-green actions and antique-gold accents, in the tradition of houses such as D.R. Harris and Granado.
  - **Type:** Playfair Display and Amiri for headings, IBM Plex Sans Arabic for body text.
  - **Finish:** rounded surfaces, soft elevation, and thin gold ornamental rules.
  - **Re-skinning** for a client means editing one file: `mobile/src/theme/tokens.ts`.
- **Light and dark.** Both schemes follow the device by default and can be overridden in Settings. Every text/background pair meets WCAG AA (body text is 14–16:1, buttons 8:1).
- **English and Arabic.**
  - Strings live in `mobile/src/i18n/en.ts` and `ar.ts`, including Arabic plural forms. TypeScript fails the build if an Arabic key is missing.
  - Arabic switches the whole layout to right-to-left, mirrors arrows, and uses the Amiri typeface.
  - Error messages are translated by error code.
- **Search.**
  - The server understands brand names (Tylenol), generic names (paracetamol) and Arabic names (باراسيتامول).
  - It maps symptoms in either language (headache / صداع) to the right products.
  - It normalises Arabic spelling and corrects typos ("Showing results for…").
  - The app adds live suggestions, recent searches, one-tap common needs, and filters for type, price, in stock nearby, category and sort.
- **Accessibility.**
  - 48 dp touch targets, and real screen-reader roles: headers, buttons, radios, selected states, and an adjustable quantity stepper.
  - Errors are announced to screen readers, and state is never shown by colour alone.
  - An in-app text size setting (up to 130%) works on top of the device's own font scaling.
- **First run.** Onboarding starts with a bilingual language choice, then four short steps. Everything is skippable, and it can be replayed from Settings.

## Run locally

Prerequisites: .NET SDK 10, Node 20+, SQL Server LocalDB (`MSSQLLocalDB`).

```bash
cd backend
dotnet tool restore
dotnet run --project src/Medora.Api --launch-profile http
```

The API listens on `http://localhost:5280`. In Development it applies migrations and seeds demo data automatically. API docs are at `/scalar/v1`.

```bash
cd mobile
npm install
npm run web
```

Use `npm run web` for the browser, or `npx expo start` and then press `a` (Android emulator) or `i` (iOS simulator). For a physical phone, set `EXPO_PUBLIC_API_URL=http://<your-PC-IP>:5280`.

### Free hosted AI (current development setup: Groq free tier)

User-secrets on this PC already set `Ai:Provider=openai`, `Ai:BaseUrl=https://api.groq.com/openai/v1` and `Ai:Model=llama-3.3-70b-versatile`. Add your own free key, then restart the API:

```bash
dotnet user-secrets set "Ai:ApiKey" "<your-groq-key>" --project backend/src/Medora.Api
```

Free tiers are rate-limited and are for development only. Before production, move to the customer's paid provider and agree data-processing terms (the copilot sends de-identified case data). Moving is just these same four settings.

### Free local AI (Ollama, offline)

Ollama is unzipped at `D:\My Files\_tools\ollama`, and its models are stored on D:. Start it before the API:

```powershell
$env:OLLAMA_MODELS = "D:\My Files\_tools\ollama\models"; $env:OLLAMA_CONTEXT_LENGTH = "8192"; & "D:\My Files\_tools\ollama\ollama.exe" serve
```

The first time only, download the model:

```powershell
& "D:\My Files\_tools\ollama\ollama.exe" pull qwen2.5:3b
```

The Development settings already point the API at `http://localhost:11434/v1` with `qwen2.5:3b`.

### Demo accounts (development seed only, password `Password123!`)

| Email | Role |
|---|---|
| `demo@medora.local` | Customer: penicillin allergy, on warfarin and simvastatin, with a clarithromycin + ibuprofen order awaiting verification |
| `pharmacist@medora.local` | Pharmacist, Downtown branch |
| `admin@medora.local` | Administrator |

Test cards are picked on the payment screen and map to Stripe test payment methods: `pm_card_visa` succeeds, `pm_card_chargeDeclined` is declined, and `pm_card_chargeDeclinedInsufficientFunds` fails for insufficient funds.

## Tests

```bash
cd backend
dotnet test
```

That runs 35 domain tests covering the state machine, pricing, refunds, interaction and allergy screens, BM25, slots, and password and lockout rules.

```bash
cd mobile
npx tsc --noEmit
npx eslint src
```

## Before production

- **Payments:** replace `MockPaymentGateway` with Stripe (PaymentIntents, PaymentSheet on the device, signed webhooks).
- **Prescription images:** upload directly to blob storage with short-lived SAS URLs. The API only stores a reference.
- **Branch time zones:** use real branch time zones; slots are UTC wall-clock today.
- **Secrets:** move the JWT signing key, connection string and Claude key to a secret store.
- **Database:** move to Azure SQL or SQL Server, add Redis as the HybridCache L2, and run migrations from CI instead of at startup.
- **Account recovery:** add password reset (the Airbnb clone's flow ports directly) and email verification.
- **Clinical content:** use a licensed drug and interaction database, and have pharmacists review the copilot prompts and outputs.
"# Pharmacy-app" 
