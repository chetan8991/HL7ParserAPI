using HL7ParserAPI.Data;
using HL7ParserAPI.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);


// Add Database Context
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));


// Register RabbitMQ Service
builder.Services.AddSingleton<RabbitMQService>();

// Add Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "HL7 API", Version = "v1" });

    // Allow text/plain in Swagger
    c.MapType<string>(() => new Microsoft.OpenApi.Models.OpenApiSchema { Type = "string", Format = "text/plain" });
});

var app = builder.Build();

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.Use(async (context, next) =>
{
    Console.WriteLine($"Incoming Request: {context.Request.Method} {context.Request.Path}");
    Console.WriteLine($"Content-Type: {context.Request.ContentType}");
    await next();
});

app.UseRouting();
app.UseAuthorization();
app.MapControllers();
app.Run();