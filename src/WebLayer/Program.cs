using System.Text;
using Application.DependencyInjection;
using Infrastructure.DependencyInjection;
using Infrastructure.Persistence.Migrations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

using WebLayer.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ========== 1. Логирование ==========
builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
);

// ========== 2. Конфигурация ==========
var configuration = builder.Configuration;

// ========== 3. Регистрация слоёв ==========
builder.Services.AddApplication();       // Application-слой (команды, запросы, валидаторы и т.д.)
builder.Services.AddInfrastructure(configuration); // Infrastructure-слой (БД, RabbitMQ, воркеры)

// ========== 4. Аутентификация JWT ==========
var jwtSettings = configuration.GetSection("Jwt");
var secretKey = jwtSettings["Key"] ?? throw new InvalidOperationException("JWT Key is required.");
var issuer = jwtSettings["Issuer"] ?? "TaskScheduler";
var audience = jwtSettings["Audience"] ?? "TaskScheduler";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
        };
    });
builder.Services.AddAuthorization();

// ========== 5. Контекст запроса ==========
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Application.Interfaces.IRequestContext, RequestContext>();

// ========== 6. Swagger / OpenAPI ==========
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Task Scheduler API",
        Version = "v1",
        Description = "API для отложенного выполнения заданий"
    });

    // Включение XML-комментариев (если генерируются)
    // var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    // options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));

    // Поддержка JWT в Swagger UI
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ========== 7. Health Checks ==========
builder.Services.AddHealthChecks()
    .AddNpgSql(configuration.GetConnectionString("DefaultConnection")!, name: "postgres", tags: new[] { "db" });

// ========== 8. Контроллеры ==========
builder.Services.AddControllers(); // Мы будем добавлять контроллеры позже

// ========== 9. CORS (опционально) ==========
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin() // В production ограничить
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// ========== 10. Выполнение миграций при старте ==========
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")!;
        DatabaseMigrator.RunMigrations(connectionString, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Ошибка при выполнении миграций");
        throw;
    }
}

// ========== 11. Pipeline ==========
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();

app.UseHttpsRedirection();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

// Middleware контекста запроса (должен идти после аутентификации)
app.UseMiddleware<RequestContextMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.MapControllers();

// Health Check эндпоинт
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                component = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description
            })
        });
        await context.Response.WriteAsync(json);
    }
});

app.Run();