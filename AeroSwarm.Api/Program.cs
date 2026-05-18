using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using AeroSwarm.Api.Data;
using AeroSwarm.Api.Hubs;
using AeroSwarm.Api.Options;
using AeroSwarm.Api.Services;
using AeroSwarm.Api.Workers;

var builder = WebApplication.CreateBuilder(args);

// ─── Serilog ────────────────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, sp, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(sp));

// ─── Options ────────────────────────────────────────────────────────────
builder.Services.Configure<SwarmOptions>(
    builder.Configuration.GetSection(SwarmOptions.SectionName));

// ─── EF Core + SQLite ───────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// ─── JWT Authentication ─────────────────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hubs/drone"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ─── CORS ───────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:5174")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// ─── SignalR ────────────────────────────────────────────────────────────
builder.Services.AddSignalR();

// ─── Drone state (singleton, shared across workers & controllers) ───────
builder.Services.AddSingleton<IDroneStateService, DroneStateService>();

// ─── Background workers ─────────────────────────────────────────────────
builder.Services.AddHostedService<MavlinkWorker>();
builder.Services.AddHostedService<HeartbeatSender>();
builder.Services.AddHostedService<FailsafeMonitor>();

// ─── MVC + Swagger ──────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AeroSwarm API",
        Version = "v1",
        Description = "Multi-UAV Command Center — REST + SignalR + MAVLink v2",
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Example: 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
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

// ─── Health checks ──────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("db");

var app = builder.Build();

// ─── DB initialization ──────────────────────────────────────────────────
// NOTE: schema changed in v2 (added Flights + Waypoints tables, new fields on DroneTelemetry).
// On first run after upgrade, delete `aeroswarm.db` to let EnsureCreated() rebuild the schema.
// Alternative: bootstrap EF migrations with `dotnet ef migrations add InitialCreate`
// then switch this to `db.Database.Migrate()`.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseSerilogRequestLogging();
app.UseCors("AllowReactApp");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Local"))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "AeroSwarm API v1");
    });
}

app.MapControllers();
app.MapHub<DroneHub>("/hubs/drone");
app.MapHealthChecks("/health");

app.Run();
