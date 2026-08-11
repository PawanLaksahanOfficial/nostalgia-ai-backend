using Application.Interfaces;
using Google;
using Infrastructure.AI;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsProduction())
{
    builder.Configuration.AddSystemsManager(
        path: "/nostalgia/production",
        optional: false,
        reloadAfter: TimeSpan.FromMinutes(30)
    );
}

// CORS Policy - origins are read from configuration (appsettings.json / appsettings.Production.json)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", p =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? Array.Empty<string>();

        if (allowedOrigins.Length > 0)
        {
            p.WithOrigins(allowedOrigins)
             .AllowAnyMethod()
             .AllowAnyHeader()
             .AllowCredentials();
        }
        else
        {
            // Same-origin deployment behind an ALB/nginx reverse proxy: allow any origin. No credentials header needed.
            p.AllowAnyOrigin()
             .AllowAnyMethod()
             .AllowAnyHeader();
        }
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database
builder.Services.AddDbContext<EFDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// JWT Authentication
var jwtKey = builder.Configuration["JwtSettings:Key"];
if (!string.IsNullOrEmpty(jwtKey))
{
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
}

// HTTP Clients
builder.Services.AddHttpClient("OpenRouter", client =>
{
    client.BaseAddress = new Uri(builder.Configuration.GetSection("OpenRouter")["Url"] ?? "https://openrouter.ai/api/v1");
    client.Timeout = TimeSpan.FromSeconds(60);
});

// Dependency Injection for Application Services
builder.Services.AddScoped<IAIService, AIService>();
builder.Services.AddScoped<IUserRepository, EfUserRepository>();
builder.Services.AddScoped<IMemoryRepository, EfMemoryRepository>();
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddHttpClient<IAuthenticationService, AuthenticationService>();

// Background Worker for Video Processing
builder.Services.AddHostedService<VideoProcessingWorker>();

var app = builder.Build();

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseHttpsRedirection();
}

// Ensure custom local storage directory exists and serve files from /storage route
var storagePath = Path.Combine(Directory.GetCurrentDirectory(), "storage");
if (!Directory.Exists(storagePath))
{
    Directory.CreateDirectory(storagePath);
}
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(storagePath),
    RequestPath = "/storage"
});

// 1. Look for index.html when hitting root "/"
app.UseDefaultFiles();

// 2. Serve static React files (JS, CSS, static assets) from wwwroot
app.UseStaticFiles();

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// 3. Fallback route to serve React's index.html for client-side React Router navigation
app.MapFallbackToFile("index.html");

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var dbContext = services.GetRequiredService<EFDbContext>();
        dbContext.Database.Migrate();
        Console.WriteLine("--> Database migration completed successfully!");
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database.");
        throw;
    }
}

app.Run();