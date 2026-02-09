using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using BistrosoftChallenge.Infrastructure;
using BistrosoftChallenge.Infrastructure.Repositories;
using BistrosoftChallenge.Infrastructure.Schema;
using BistrosoftChallenge.MessageContracts;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using BistrosoftChallenge.Api.Middleware;

namespace BistrosoftChallenge
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            // Require authentication globally by default (AuthController token endpoint will allow anonymous)
            builder.Services.AddControllers(options =>
            {
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
                options.Filters.Add(new AuthorizeFilter(policy));
            });
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            // JWT configuration (use configuration if present, otherwise fallback to defaults)
            var jwtKey = builder.Configuration["Jwt:Key"] ?? "super_secret_key_123!_this_is_a_longer_and_stronger_key_with_random_chars_9876543210!@#$%^&*()";
            var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "BistrosoftChallenge";

            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwtIssuer,
                        ValidAudience = jwtIssuer,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
                    };
                });

            builder.Services.AddAuthorization();
            builder.Services.AddHttpClient();

            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "BistrosoftChallenge API", Version = "v1" });
                // Add JWT Bearer support to Swagger
                var securityScheme = new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Enter only the JWT token (do NOT include the 'Bearer ' prefix).\n\nExample: \"eyJhbGciOi...\"",
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                };
                c.AddSecurityDefinition("Bearer", securityScheme);
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, new string[] { } }
                });
            });

            // DbContext - use configuration or in-memory for now
            builder.Services.AddDbContext<AppDbContext>(options =>
            {
                var conn = builder.Configuration.GetConnectionString("Default");
                if (!string.IsNullOrEmpty(conn))
                {
                    options.UseSqlServer(conn);
                }
                else
                {
                    options.UseInMemoryDatabase("dev");
                }
            });
            builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());

            builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
            builder.Services.AddScoped<IProductRepository, ProductRepository>();
            builder.Services.AddScoped<IOrderRepository, OrderRepository>();
            builder.Services.AddScoped<SchemaCompatibilityValidator>();

            // MassTransit - use RabbitMQ for cross-process messaging (fallback to in-memory if not configured)
            builder.Services.AddMassTransit(x =>
            {
                var rabbitHost = builder.Configuration["RabbitMq:Host"];
                if (!string.IsNullOrEmpty(rabbitHost))
                {
                    x.UsingRabbitMq((context, cfg) =>
                    {
                        cfg.Host(rabbitHost, h =>
                        {
                            h.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
                            h.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
                        });
                        cfg.ConfigureEndpoints(context);
                    });
                }
                else
                {
                    x.UsingInMemory((context, cfg) =>
                    {
                        cfg.ConfigureEndpoints(context);
                    });
                }
            });

            var app = builder.Build();

            app.UseMiddleware<GlobalExceptionMiddleware>();

            // Validate schema compatibility on startup
            using (var scope = app.Services.CreateScope())
            {
                var validator = scope.ServiceProvider.GetRequiredService<SchemaCompatibilityValidator>();
                validator.Validate();
            }

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "BistrosoftChallenge API v1");
                    // keep the default UI path at /swagger
                    c.RoutePrefix = "swagger";
                });
            }

            app.UseHttpsRedirection();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();

            app.Run();
        }
    }
}
