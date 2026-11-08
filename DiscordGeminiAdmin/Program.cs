using DiscordGeminiAdmin.Application;
using DiscordGeminiAdmin.Infrastructure;
using DiscordGeminiAdmin.Infrastructure.Persistence;
using DiscordGeminiAdmin.Presentation.Discord;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((_, logger) => logger.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddDiscord();

var host = builder.Build();

await host.Services.ApplyMigrationsAsync();
host.UseDiscord();

await host.RunAsync();
