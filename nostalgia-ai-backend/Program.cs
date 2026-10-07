using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Application.DTOs;
using Application.Interfaces;
using Application.Validators;
using FluentValidation;
using Infrastructure.AI;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using nostalgia_ai_backend.Filters;
using nostalgia_ai_backend.Middleware;

var builder = WebApplication.CreateBuilder(args);

// CORS Policy
var allowedOrigins = (builder.Configuration["AllowedOrigins"] ?? "http://localhost:3000,http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", p => p
        .WithOrigins(allowedOrigins)
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials());
});

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ValidationFilter>();
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Centralized exception handling
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            ApiResponse.Fail("Too many requests. Please try again later."),
            cancellationToken);
    };

    // Global: 100 requests/minute per client IP, applied to every endpoint.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientIp(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: GetClientIp(httpContext),
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 4,
                QueueLimit = 0
            }));

    options.AddPolicy("ai-generation", httpContext =>
        RateLimitPartition.GetTokenBucketLimiter(
            partitionKey: GetUserOrIp(httpContext),
            factory: _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 5,
                TokensPerPeriod = 2,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("share-view", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientIp(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

static string GetClientIp(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static string GetUserOrIp(HttpContext context) =>
    context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? GetClientIp(context);

// Database
builder.Services.AddDbContext<EFDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// JWT Authentication
var jwtKey = builder.Configuration["JwtSettings:Key"];
if (string.IsNullOrEmpty(jwtKey))
{
    throw new InvalidOperationException(
        "JwtSettings:Key is not configured. Set it via user-secrets locally or the JwtSettings__Key environment variable in production.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
        ValidAudience = builder.Configuration["JwtSettings:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

var trustedProxies = (builder.Configuration["TrustedProxies"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(candidate => IPAddress.TryParse(candidate, out var parsed) ? parsed : null)
    .Where(address => address is not null)
    .ToList();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var proxy in trustedProxies)
    {
        options.KnownProxies.Add(proxy!);
    }
});

// HTTP Clients
builder.Services.AddHttpClient("OpenRouter", client =>
{
    client.Timeout = TimeSpan.FromSeconds(
        builder.Configuration.GetValue("OpenRouter:TimeoutSeconds", 60));
});

// Dependency Injection for Application Services
builder.Services.AddScoped<IAIService, AIService>();
builder.Services.AddScoped<IUserRepository, EfUserRepository>();
builder.Services.AddScoped<IMemoryRepository, EfMemoryRepository>();
builder.Services.AddScoped<IPasswordHasher, PasswordHasherService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

// Email: Brevo's free HTTP API by default; SES stays available for later.
if (string.Equals(builder.Configuration.GetSection("Email")["Provider"], "ses", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IEmailService, SesEmailService>();
}
else
{
    builder.Services.AddHttpClient(BrevoEmailService.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
    builder.Services.AddScoped<IEmailService, BrevoEmailService>();
}

// Video management and sharing
builder.Services.AddScoped<IShareLinkRepository, EfShareLinkRepository>();
builder.Services.AddScoped<IShareLinkService, ShareLinkService>();
builder.Services.AddScoped<IVideoComposer, FfmpegVideoComposer>();
builder.Services.AddScoped<IMusicProvider, BundledMusicProvider>();

// Free stock photos give every video visuals. Pixabay is used when its key is set (Pexels paused new
// API keys in October 2026); otherwise Pexels, which is a no-op without Pexels:ApiKey.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Pixabay:ApiKey"]))
{
    // Pixabay takes the key in the query string, which HttpClient's request logging would write out.
    builder.Services.AddHttpClient(PixabayStockPhotoProvider.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(20))
        .RemoveAllLoggers();
    // A singleton so its 24-hour search cache, required by Pixabay's API terms, outlives each job.
    builder.Services.AddSingleton<IStockPhotoProvider, PixabayStockPhotoProvider>();
}
else
{
    builder.Services.AddHttpClient(PexelsStockPhotoProvider.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(20));
    builder.Services.AddScoped<IStockPhotoProvider, PexelsStockPhotoProvider>();
}

var storageProvider = builder.Configuration.GetSection("Storage")["Provider"];
if (string.Equals(storageProvider, "r2", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(storageProvider, "s3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IFileStorage, S3FileStorage>();
}
else
{
    builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
}

// Voiceover is optional: without a provider, videos still get music and captions.
if (string.Equals(builder.Configuration.GetSection("Tts")["Provider"], "edge", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<ITextToSpeech, EdgeTtsService>();
}
else
{
    builder.Services.AddScoped<ITextToSpeech, NoOpTextToSpeech>();
}

// Background Worker for Video Processing
builder.Services.AddHostedService<VideoProcessingWorker>();

var app = builder.Build();

// Middleware
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
if (trustedProxies.Count > 0)
{
    app.UseForwardedHeaders();
}
app.UseHttpsRedirection();

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", async (IVideoComposer composer, ITextToSpeech tts, EFDbContext db) =>
{
    bool database;
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        database = await db.Database.CanConnectAsync(timeout.Token);
    }
    catch
    {
        database = false;
    }
    var ffmpeg = await composer.IsAvailableAsync();
    return Results.Ok(new
    {
        status = database ? "Healthy" : "Degraded",
        database,
        ffmpeg,
        tts = tts.IsEnabled
    });
});

app.Run();