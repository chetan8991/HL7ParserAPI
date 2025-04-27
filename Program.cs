using Google.Cloud.SecretManager.V1;
using HL7ParserAPI.Data;
using HL7ParserAPI.Helpers;
using HL7ParserAPI.Models;
using HL7ParserAPI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithProcessId()
    .Enrich.WithThreadId()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog(); // Attach Serilog to .NET logging

// GCP Project ID
string gcpProjectId = "spartan-acrobat-452620-t6";

// Load environment variables
builder.Configuration.AddEnvironmentVariables();

// Pull secrets from Secret Manager
var dbConnectionString = SecretManagerHelper.GetSecret("db-connection-string", gcpProjectId);
builder.Configuration["ConnectionStrings:DefaultConnection"] = dbConnectionString; // Inject into config

// Add Database Context
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register RabbitMQService
builder.Services.AddSingleton<RabbitMQService>();

// Add Controllers and Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "HL7 API", Version = "v1" });
    c.MapType<string>(() => new Microsoft.OpenApi.Models.OpenApiSchema { Type = "string", Format = "text/plain" });
});

var app = builder.Build();

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging(); // Logs HTTP requests

app.Use(async (context, next) =>
{
    Log.Information("Incoming Request: {Method} {Path} - Content-Type: {ContentType}",
        context.Request.Method, context.Request.Path, context.Request.ContentType);
    await next();
});

app.UseRouting();
app.UseAuthorization();
app.MapControllers();
app.Run();
