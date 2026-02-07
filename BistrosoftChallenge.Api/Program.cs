using Microsoft.EntityFrameworkCore;
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
            builder.Services.AddSwaggerGen();

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

            builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
            builder.Services.AddScoped<IProductRepository, ProductRepository>();
            builder.Services.AddScoped<IOrderRepository, OrderRepository>();
            builder.Services.AddScoped<SchemaCompatibilityValidator>();

            // MassTransit - minimal setup for sending commands
            builder.Services.AddMassTransit(x =>
            {
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.ConfigureEndpoints(context);
                });
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
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseAuthorization();
            app.MapControllers();

            app.Run();
        }
    }
}
