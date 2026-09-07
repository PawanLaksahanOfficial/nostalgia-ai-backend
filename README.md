# Nostalgia AI — Backend

ASP.NET Core 8 API for Nostalgia AI. Takes a written memory and an optional photo, generates a
nostalgic narrative, narrates it, renders it into a video with music and captions, and serves
it back over authenticated and public share endpoints.

**Live API:** https://nostalgia-ai-backend.onrender.com
**Frontend:** https://nostalgia-ai-frontend.vercel.app ([frontend repo](../nostalgia-ai-frontend))

---

## What it does

1. A signed-in user submits a title, story text, an optional music mood, and an optional image.
2. Quota is consumed atomically against the user's plan; the memory row is created as `Pending`.
3. A background worker picks it up and runs the pipeline, reporting a human-readable step as
   it goes (surfaced by the client while polling):

   | Step reported | What happens |
   | --- | --- |
   | *Writing your story…* | Narrative generated via OpenRouter, trimmed to a word budget derived from the plan's max duration |
   | *Picking the music…* | A bundled track is selected for the requested mood |
   | *Recording the narration…* | Narration synthesised (Edge TTS) |
   | *Composing your video…* | Captions written as SRT, then FFmpeg composes video, audio, music, captions, and any watermark |
   | *Finishing up…* | Video, thumbnail, and captions uploaded to storage |
4. The client polls status until `Completed` or `Failed`, then streams, downloads, or shares it.
5. Share links are opaque tokens with optional expiry, revocation, and view counts — served
   from unauthenticated endpoints so a recipient needs no account.

---

## Architecture

Four projects, dependencies pointing inward:

```
Domain/            Entities only — User, UserMemory, MemoryShareLink,
                   PasswordResetToken, ProcessedStripeEvent
Application/       Interfaces, DTOs, validators, exceptions. No infrastructure references.
Infrastructure/    EF Core DbContext, migrations, repositories, and all external
                   integrations: AI, TTS, FFmpeg, storage, email, Stripe, share links
nostalgia-ai-backend/   ASP.NET Core host — controllers, middleware, filters, DI, assets
nostalgia-ai-backend.Tests/   xUnit test suite
```

Notable pieces in `Infrastructure/Services/`:

| File | Role |
| --- | --- |
| `VideoProcessingWorker` | Background service that drives the render pipeline |
| `FfmpegVideoComposer`, `FfmpegArgumentBuilder` | Video composition |
| `EdgeTtsService`, `NoOpTextToSpeech` | Narration (swappable via `Tts:Provider`) |
| `CaptionBuilder`, `NarrativeTrimmer` | Subtitle timing and text shaping |
| `ShareLinkService`, `ShareTokenGenerator` | Share tokens, expiry, revocation |
| `SubscriptionService` | Plans, quota, Stripe lifecycle |
| `SesEmailService` | Transactional email (AWS SES) |
| `R2FileStorage`, `LocalFileStorage` | Media storage (`Storage:Provider`) |

---

## Getting started

**Prerequisites:** .NET 8 SDK, PostgreSQL, and — for actual video rendering — FFmpeg/FFprobe on
disk with their paths set in `Video:FfmpegPath` / `Video:FfprobePath`.

```bash
dotnet restore
dotnet ef database update \
  --project Infrastructure/Infrastructure.csproj \
  --startup-project nostalgia-ai-backend/nostalgia-ai-backend.csproj
dotnet run --project nostalgia-ai-backend/nostalgia-ai-backend.csproj
```

### Configuration

`appsettings.json` holds non-secret defaults and ships with every secret blank. **In development
put secrets in user secrets, never in `appsettings.json`:**

```bash
cd nostalgia-ai-backend
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=…;Database=…;Username=…;Password=…"
dotnet user-secrets set "JwtSettings:Key" "<32+ char signing key>"
dotnet user-secrets set "OpenRouter:ApiToken" "<token>"
```

User secrets load **only when `ASPNETCORE_ENVIRONMENT=Development`**. EF Core tooling defaults
to Production, so prefix migration commands when the connection string lives in user secrets:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations list \
  --project Infrastructure/Infrastructure.csproj \
  --startup-project nostalgia-ai-backend/nostalgia-ai-backend.csproj
```

In production, supply the same keys as environment variables using `__` for nesting —
`ConnectionStrings__DefaultConnection`, `JwtSettings__Key`, `Stripe__SecretKey`, and so on.

#### Settings reference

| Section | Keys | Notes |
| --- | --- | --- |
| `ConnectionStrings` | `DefaultConnection` | PostgreSQL (Npgsql) |
| `JwtSettings` | `Key`, `Issuer`, `Audience`, `DurationInMinutes` | `Key` is a secret |
| `AllowedOrigins` | comma-separated origins | Must include the deployed frontend origin |
| `Frontend` | `BaseUrl` | Used to build share and password-reset links |
| `Storage` | `Provider`, `AccountId`, `AccessKey`, `SecretKey`, `Bucket`, `PublicBaseUrl`, `LocalPath`, `BaseUrl` | `Provider` selects R2 or local disk |
| `OpenRouter` | `ApiToken`, `Url`, `Model`, `TimeoutSeconds` | Narrative generation |
| `Tts` | `Provider`, `EdgeTtsPath`, `Voice`, `Rate`, `TimeoutSeconds` | Narration |
| `Video` | `FfmpegPath`, `FfprobePath`, `AssetsPath`, resolutions, `Fps`, `MaxImageBytes`, `MaxConcurrentJobs`, `JobTimeoutSeconds`, … | Render pipeline tuning |
| `Stripe` | `SecretKey`, `WebhookSecret`, `PremiumPriceId` | All secrets |
| `AWS` | `AccessKey`, `SecretKey`, `Region` | SES credentials |
| `Email` | `FromAddress` | Must be SES-verified |
| `GoogleClientId`, `Meta` | `AppId`, `AppSecret` | Social sign-in |
| `TierLimits` | `Free`/`Premium` → `MonthlyMemories`, `MaxVideoDuration`, `Quality` | Plan entitlements |

---

## API

All responses use a consistent envelope:

```json
{ "success": true, "message": "Success", "data": { }, "errors": null }
```

Authenticated routes expect `Authorization: Bearer <jwt>`.

### Auth — `/api/auth`, `/api/user`
| Method | Route | Auth |
| --- | --- | --- |
| POST | `/api/auth/register` | — |
| POST | `/api/auth/login` | — |
| POST | `/api/auth/forgot-password` | — |
| POST | `/api/auth/reset-password` | — |
| POST | `/api/user/socialLoginValidate` | — |

### Videos — `/api/videos`
| Method | Route | Notes |
| --- | --- | --- |
| POST | `/api/videos` | Multipart: fields + optional `image`. Consumes quota. |
| GET | `/api/videos` | List the caller's videos |
| GET | `/api/videos/{id}` | Detail |
| GET | `/api/videos/{id}/status` | Poll while rendering |
| PUT / DELETE | `/api/videos/{id}` | Rename / delete |
| GET | `/api/videos/{id}/stream` \| `/download` \| `/thumbnail` | Range-enabled media |
| POST / GET | `/api/videos/{id}/share` | Create / list share links |
| DELETE | `/api/videos/{id}/share/{shareId}` | Revoke a link |

### Public share — `/api/share` (no auth)
`GET /api/share/{token}`, plus `/stream`, `/download`, `/thumbnail`. Unknown, expired, or
revoked tokens all return the same 404 message so links can't be probed.

### Profile, subscription, webhooks
`/api/profile/{myProfile,change-password,memories}`,
`/api/subscription/{plans,quota,status,checkout,portal,cancel,resume}`,
and `/api/webhook/stripe`. Stripe events are de-duplicated via `ProcessedStripeEvent`, so
redelivery is safe.

Rate limiting is applied per policy — `ai-generation` on create/generate, `share-view` on
public share reads — and media streaming is explicitly exempt.

---

## Database

EF Core 9 with Npgsql. Migrations live in `Infrastructure/Migrations/`.

```bash
# add a migration
dotnet ef migrations add <Name> \
  --project Infrastructure/Infrastructure.csproj \
  --startup-project nostalgia-ai-backend/nostalgia-ai-backend.csproj

# apply
dotnet ef database update \
  --project Infrastructure/Infrastructure.csproj \
  --startup-project nostalgia-ai-backend/nostalgia-ai-backend.csproj
```

> **Migrations are not applied automatically.** There is no `Database.Migrate()` at startup and
> no migration step in the Dockerfile, so every environment must be updated deliberately before
> deploying code that depends on new schema.

When rebasing a branch that adds a migration, confirm its timestamp still sorts after every
migration on the target branch, and that it does not recreate an index or table another
migration already owns — neither problem is caught by `has-pending-model-changes`, only by
actually running the migration.

---

## Testing

```bash
dotnet test
```

xUnit. Service tests run against a real EF Core context backed by an in-memory **SQLite**
connection, so relational behaviour (constraints, transactions, concurrency) is exercised
rather than stubbed.

---

## Deployment

The [`Dockerfile`](Dockerfile) does a multi-stage SDK build to an ASP.NET 8 runtime image and
binds to `$PORT` (default 8080), which suits Render and similar hosts. Supply all secrets as
environment variables, set `AllowedOrigins` to the deployed frontend origin, and point
`Frontend:BaseUrl` at it so emailed and shared links resolve.

The runtime image does **not** include FFmpeg or Edge TTS. `Tts:Provider` selects narration:
`"edge"` wires up `EdgeTtsService`, and any other value falls back to `NoOpTextToSpeech`
(silent narration). FFmpeg has no such fallback — without the binaries at `Video:FfmpegPath`
and `Video:FfprobePath`, composition fails and jobs end up `Failed`.
