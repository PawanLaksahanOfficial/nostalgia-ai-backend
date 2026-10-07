# Nostalgia AI — Backend

ASP.NET Core 8 API for Nostalgia AI. Takes a written memory and an optional photo, has an AI
write a narration and pick matching photos, records a voice-over, renders it all into a
captioned video with music, and serves it back over authenticated and public share endpoints.

**Live API:** https://nostalgia-ai-backend.onrender.com
**Frontend:** https://nostalgia-ai-frontend.vercel.app ([frontend repo](https://github.com/PawanLaksahanOfficial/nostalgia-ai-frontend), with screenshots)

<p align="center">
  <img src="docs/example-frame.jpg" alt="A frame from a generated video: a still lake lined with trees, the AI-written narration as captions, and a small 'Made with Nostalgia AI' watermark" width="720">
  <br>
  <sub>A frame from a real render: the AI-written narration as captions over a stock photo (Bergadder on Pixabay), with the free-plan watermark.</sub>
</p>

---

## What it does

1. A user signs up (email accounts confirm their address first) and submits a title, story text,
   an optional music mood, and an optional image.
2. The request passes the [abuse checks](#abuse-protection), quota is consumed atomically
   against the user's plan, and the memory row is created as `Pending`.
3. A background worker picks it up and runs the pipeline, reporting a human-readable step as
   it goes (surfaced by the client while polling):

   | Step reported | What happens |
   | --- | --- |
   | *Writing your story…* | An AI model (OpenRouter, with a fallback list of free models) writes a JSON script: a first-person narration sized to the plan's maximum length, plus short stock-photo search phrases. If the AI is unavailable, the user's own text is narrated and the video is flagged as such |
   | *Picking the music…* | A bundled public-domain track is chosen for the requested mood |
   | *Recording the narration…* | Edge TTS records the voice-over; its word timings become the captions |
   | *Finding photos for your story…* | The user's photo comes first, then stock photos for the AI's search phrases (Pixabay, or Pexels); photographers are credited |
   | *Composing your video…* | FFmpeg glides slowly across each photo with crossfades between them, burns in captions, mixes the music under the voice, and adds the free-plan watermark |
   | *Finishing up…* | Video, thumbnail, and captions are uploaded to storage |
4. The client polls status until `Completed` or `Failed`, then streams, downloads, or shares it.
5. Share links are opaque tokens with optional expiry, revocation, and view counts — served
   from unauthenticated endpoints so a recipient needs no account.

---

## Abuse protection

Free videos all draw on one free AI quota, so the API makes extra accounts expensive and caps
what any one network can use:

| Layer | What it stops |
| --- | --- |
| Email confirmation | Sign-ups with made-up addresses: email/password accounts must click an emailed link before creating videos. Google and Meta sign-ins, and password resets, count as confirmed |
| One account per inbox | Gmail dot and `+tag` aliases (`j.ane+1@gmail.com`) map to the same canonical address, as do `+tag` aliases elsewhere |
| Throwaway domains | Sign-ups from about 9,000 disposable-email domains ([list](https://github.com/disposable-email-domains/disposable-email-domains), CC0) |
| Sign-ups per network | More than 3 new accounts per network in 24 hours |
| Free videos per network | More than 6 free videos per network in 24 hours, across all its accounts |
| Site-wide free videos | More than 40 free videos in 24 hours, below the free AI tier's 50 requests a day |

Daily counts include deleted videos, so deleting one does not free a slot. Premium users are
exempt from the video caps. Client addresses are stored only as keyed one-way hashes (IPv6 by
its /64 prefix), and when the real client address is unknown the per-network limits are
skipped rather than applied to everyone. All limits are settings under `Abuse` (see below).

---

## Architecture

Four projects, dependencies pointing inward:

```
Domain/            Entities only — User, UserMemory, MemoryShareLink, PasswordResetToken,
                   EmailVerificationToken, VideoRequest, ProcessedStripeEvent
Application/       Interfaces, DTOs, validators, exceptions. No infrastructure references.
Infrastructure/    EF Core DbContext, migrations, repositories, and all external
                   integrations: AI, TTS, FFmpeg, photos, storage, email, Stripe, share links
nostalgia-ai-backend/   ASP.NET Core host — controllers, middleware, filters, DI, assets
nostalgia-ai-backend.Tests/   xUnit test suite
```

Notable pieces in `Infrastructure/Services/`:

| File | Role |
| --- | --- |
| `VideoProcessingWorker` | Background service that drives the render pipeline |
| `StoryScriptParser`, `NarrativeTrimmer` | Builds the AI prompt and reads its JSON script; trims the narration to the plan's word budget |
| `FfmpegVideoComposer`, `FfmpegArgumentBuilder` | Video composition (pan or zoom per photo, crossfades, captions, audio mix, watermark) |
| `EdgeTtsService`, `NoOpTextToSpeech` | Narration (swappable via `Tts:Provider`) |
| `CaptionBuilder` | Caption timing from the voice's word timings |
| `PixabayStockPhotoProvider`, `PexelsStockPhotoProvider`, `PhotoCreditBuilder` | Stock photos (Pixabay searches are cached for 24 hours, as its API terms require) and the stored credit line |
| `BundledMusicProvider` | Picks a bundled track by mood |
| `ShareLinkService`, `ShareTokenGenerator` | Share tokens, expiry, revocation |
| `SubscriptionService` | Plans, quota, Stripe lifecycle |
| `BrevoEmailService`, `SesEmailService`, `EmailTemplates` | Transactional email (email confirmation, password reset): Brevo by default, AWS SES optional |
| `IpAddressHasher`, `EmailCanonicalizer`, `DisposableEmailDomains` | Abuse protection |
| `S3FileStorage`, `LocalFileStorage` | Media storage (`Storage:Provider`): any S3-compatible bucket (Cloudflare R2, Supabase Storage) or local disk |

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

Local development turns off email confirmation (`appsettings.Development.json`), since email
sending is usually not configured there.

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

> **Never define a variable with an empty value on the host.** An empty environment variable
> overrides the default in `appsettings.json` — an empty `OpenRouter__Url`, for example, turns
> the AI off. Delete a variable instead of blanking it.

#### Settings reference

| Section | Keys | Notes |
| --- | --- | --- |
| `ConnectionStrings` | `DefaultConnection` | PostgreSQL (Npgsql) |
| `JwtSettings` | `Key`, `Issuer`, `Audience`, `DurationInMinutes` | `Key` is a secret |
| `AllowedOrigins` | comma-separated origins | Must include the deployed frontend origin |
| `ClientIpHeaders` | comma-separated header names | Where the visitor's address comes from behind a proxy. Production uses `CF-Connecting-IP` (Render sits behind Cloudflare); without it every visitor shares Render's proxy address and one rate-limit bucket. Leave empty when the app is directly exposed. `/health` reports the `clientIp` it resolved |
| `Abuse` | `RequireEmailVerification`, `GlobalDailyVideoLimit`, `PerIpDailyVideoLimit`, `PerIpDailySignupLimit`, `IpHashSecret` | See [Abuse protection](#abuse-protection). Caps are rolling 24-hour windows; `0` disables one. `IpHashSecret` keys the address hashes and defaults to the JWT key |
| `Frontend` | `BaseUrl` | Used to build share, email confirmation, and password-reset links |
| `Storage` | `Provider`, `ServiceUrl`, `Region`, `AccountId`, `AccessKey`, `SecretKey`, `Bucket`, `PublicBaseUrl`, `LocalPath`, `BaseUrl` | `Provider`: `local`, `r2` (set `AccountId`) or `s3` (set `ServiceUrl` + `Region`, e.g. Supabase Storage). Use a bucket in production: Render's disk is wiped on restart |
| `OpenRouter` | `ApiToken`, `Url`, `Model`, `TimeoutSeconds` | Narrative generation. `Model` is a comma-separated fallback list; free models get retired, so check it if narration falls back to the raw story |
| `Tts` | `Provider`, `EdgeTtsPath`, `Voice`, `Rate`, `TimeoutSeconds` | Narration |
| `Pixabay`, `Pexels` | `ApiKey` | Stock photos for the slideshow. Pixabay is used when its key is set (Pexels paused new keys in Oct 2026); with neither, videos without an uploaded photo get a plain background |
| `Video` | `FfmpegPath`, `FfprobePath`, `AssetsPath`, resolutions, `Fps`, `Preset`, `MotionStyle`, `PanPixelsPerSecond`, `MaxImageBytes`, `MaxConcurrentJobs`, `JobTimeoutSeconds`, … | Render pipeline tuning. `appsettings.Production.json` lowers these for Render's free plan. `MotionStyle` is `pan` (a steady glide; the default in `appsettings.json`) or `zoom` (FFmpeg's zoompan, which wobbles unless rendered at a high `Prescale*`). Keep `Preset` at `veryfast` or slower: `ultrafast` makes files 7–9× larger |
| `Email` | `Provider`, `FromAddress`, `FromName` | `Provider` is `brevo` (default) or `ses`. `FromAddress` must be a verified sender with that provider |
| `Brevo` | `ApiKey` | Brevo API key (`xkeysib-…`, not the SMTP key) |
| `AWS` | `AccessKey`, `SecretKey`, `Region` | SES credentials, used only when `Email:Provider` is `ses` |
| `Stripe` | `SecretKey`, `WebhookSecret`, `PremiumPriceId` | All secrets |
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
| POST | `/api/auth/verify-email` | — |
| POST | `/api/auth/resend-verification` | Bearer |
| POST | `/api/auth/forgot-password` | — |
| POST | `/api/auth/reset-password` | — |
| POST | `/api/user/socialLoginValidate` | — |

### Videos — `/api/videos`
| Method | Route | Notes |
| --- | --- | --- |
| POST | `/api/videos` | Multipart: fields + optional `image`. Needs a confirmed email, passes the daily caps, consumes quota |
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
redelivery is safe, and events from a webhook endpoint on a different Stripe API version are
accepted with a logged warning.

Rate limiting is applied per policy — `auth` on sign-in and sign-up, `ai-generation` on video
creation, `share-view` on public share reads — and media streaming is explicitly exempt.

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

xUnit. Covers FFmpeg argument building, story-script parsing, captions, stock-photo parsing and
caching, abuse protection, email, and subscriptions. Repository and service tests run against
a real EF Core context backed by an in-memory **SQLite** connection, so relational behaviour
(constraints, transactions, concurrency) is exercised rather than stubbed.

---

## Deployment

The [`Dockerfile`](Dockerfile) does a multi-stage SDK build to an ASP.NET 8 runtime image and
binds to `$PORT` (default 8080), which suits Render and similar hosts. Supply all secrets as
environment variables, set `AllowedOrigins` to the deployed frontend origin, and point
`Frontend:BaseUrl` at it so emailed and shared links resolve.

The runtime image installs FFmpeg/FFprobe, the DejaVu font (watermark and captions) and the
`edge-tts` CLI. `Tts:Provider` selects narration: `"edge"` wires up `EdgeTtsService`, and any
other value falls back to `NoOpTextToSpeech` (silent narration). FFmpeg is required: while
`/health` reports `"ffmpeg": false`, the worker leaves every job `Pending`. `/health` also
reports `"database"` (and stays HTTP 200 with `"status": "Degraded"` when it is down) and the
caller's resolved `clientIp`.

Four public-domain music loops ship in `nostalgia-ai-backend/assets/music/` (sources in
`CREDITS.md` there). For email, set `Brevo__ApiKey` and a Brevo-verified `Email__FromAddress`,
and in Brevo either allow the host's outbound IP addresses or turn off IP review: Brevo blocks
unknown addresses once a key is 30 days old.

### Running on free tiers

The live deployment runs on free plans, and the defaults are tuned for them:

| Service | Free-tier limit | How the app copes |
| --- | --- | --- |
| Render | 0.1 CPU, 512 MB, sleeps when idle | One video at a time; 540p/24 fps with a cheap pan instead of zoom; a 15-minute job timeout |
| OpenRouter | 50 free-model requests a day | A fallback list of models, and a site-wide cap of 40 free videos a day |
| Supabase | 1 GB storage, 5 GB egress | The `veryfast` encoder keeps a 30-second video around 3–4 MB |
| Pixabay | Searches must be cached for 24 hours | An in-memory search cache |
| Brevo | 300 emails a day | Only confirmation and password-reset emails are sent |
