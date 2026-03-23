using Microsoft.EntityFrameworkCore;
using Infrastructure.Data;
using System;
using Application.Interfaces;
using Infrastructure.AI;
using Infrastructure.Repositories;
using Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

//CORS Policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", p => p.WithOrigins("http://localhost:3000").AllowAnyMethod().AllowAnyHeader().AllowCredentials());
});
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database
builder.Services.AddDbContext<EFDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Dependency Injection for Application Services
builder.Services.AddScoped<IAIService, AIService>();
builder.Services.AddScoped<IUserRepository, EfUserRepository>();
builder.Services.AddHttpClient<IAuthenticationService, AuthenticationService>();

var app = builder.Build();

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.UseCors("AllowReactApp");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();