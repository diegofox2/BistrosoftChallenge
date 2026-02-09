using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using BistrosoftChallenge.Infrastructure;
using BistrosoftChallenge.Infrastructure.Repositories;
using BistrosoftChallenge.Infrastructure.Schema;
using BistrosoftChallenge.MessageContracts;
using MassTransit;

namespace BistrosoftChallenge
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "BistrosoftChallenge API", Version = "v1" });
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
            app.UseAuthorization();
            app.MapControllers();

            app.Run();
        }
    }
}
