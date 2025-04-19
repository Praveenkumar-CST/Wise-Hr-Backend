using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Supabase;
using System.Text;
using WiseHRServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add controllers
builder.Services.AddControllers();

// ✅ CORS: Allow your frontend origin
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5039") // <-- Frontend origin
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Required for cookies/auth from frontend
    });
});

// ✅ MongoDB
builder.Services.AddSingleton<MongoDbService>();

// ✅ Encryption
builder.Services.AddSingleton<EncryptionService>(provider =>
{
    var key = builder.Configuration["Encryption:Aes256Key"];
    return new EncryptionService(key);
});

// ✅ JWT Auth using Supabase JWT secret
var jwtSecret = builder.Configuration["Supabase:JwtSecret"];
if (string.IsNullOrEmpty(jwtSecret) || jwtSecret.Length < 16)
    throw new Exception("JWT Secret is missing or too short in appsettings.json");

var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Supabase:Url"] + "/auth/v1",
            ValidateAudience = true,
            ValidAudience = "authenticated",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            NameClaimType = "sub",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromMinutes(5)
        };
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine("Authentication failed: " + context.Exception.Message);
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                Console.WriteLine("Token validated: " + context.SecurityToken);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpClient();

// ✅ Supabase client
var supabaseUrl = builder.Configuration["Supabase:Url"];
var serviceRoleKey = builder.Configuration["Supabase:ServiceRoleKey"];
if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(serviceRoleKey))
    throw new Exception("Supabase configuration (Url or ServiceRoleKey) is missing");

builder.Services.AddScoped(_ => new Supabase.Client(
    supabaseUrl,
    serviceRoleKey,
    new SupabaseOptions { AutoConnectRealtime = true }));

// ✅ SQL Server connection
var connectionString = builder.Configuration.GetConnectionString("SqlConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// ✅ Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "WiseHR API",
        Version = "v1",
        Description = "API for WiseHR application",
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer' followed by a space and your JWT token."
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ✅ Middleware order
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// 🧠 CORS must be placed before authentication
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "WiseHR API v1");
    });
}

// ✅ Map controllers
app.MapControllers();
app.Run();
