using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Supabase;
using System.Text;
using WiseHRServer.Services; 
using CalendarApi.Data; 

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();

// CORS: Allow specific frontend origin (more secure than AllowAll)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5039") // Adjust to your frontend URL
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Required for cookies/auth
    });
});

// SQL Server connection 
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new Exception("SQL Server connection string 'DefaultConnection' is missing");
builder.Services.AddDbContext<CalendarDbContext>(options =>
    options.UseSqlServer(connectionString));

//  MongoDB 
builder.Services.AddSingleton<MongoDbService>();

//  Encryption
builder.Services.AddSingleton<EncryptionService>(provider =>
{
    var key = builder.Configuration["Encryption:Aes256Key"] 
        ?? throw new Exception("Encryption key 'Aes256Key' is missing in appsettings.json");
    return new EncryptionService(key);
});

//  JWT Authentication using Supabase JWT secret 
var jwtSecret = builder.Configuration["Supabase:JwtSecret"] 
    ?? throw new Exception("JWT Secret is missing or too short in appsettings.json");
if (jwtSecret.Length < 16)
    throw new Exception("JWT Secret is too short in appsettings.json");

var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false; // Set to true in production
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

//  Supabase client 
var supabaseUrl = builder.Configuration["Supabase:Url"] 
    ?? throw new Exception("Supabase URL is missing in appsettings.json");
var serviceRoleKey = builder.Configuration["Supabase:ServiceRoleKey"] 
    ?? throw new Exception("Supabase ServiceRoleKey is missing in appsettings.json");

builder.Services.AddScoped(_ => new Supabase.Client(
    supabaseUrl,
    serviceRoleKey,
    new SupabaseOptions { AutoConnectRealtime = true }));

//  Swagger 
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "WiseHR Calendar API",
        Version = "v1",
        Description = "API for WiseHR and Calendar application",
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

//  Middleware order
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "WiseHR Calendar API v1");
    });
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();


app.MapControllers();

app.Run();