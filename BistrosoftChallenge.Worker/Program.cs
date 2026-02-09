using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BistrosoftChallenge.Infrastructure;
using BistrosoftChallenge.Worker.Sagas;

namespace BistrosoftChallenge.Worker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);

            builder.Services.AddHostedService<Worker>();

            builder.Services.AddDbContext<AppDbContext>(options =>
            {
                var conn = builder.Configuration.GetConnectionString("Default");
                if (!string.IsNullOrEmpty(conn))
                {
                    options.UseSqlServer(conn);
                }
                else
                {
                    options.UseInMemoryDatabase("worker");
                }
            });

            builder.Services.AddMassTransit(cfg =>
            {
                cfg.AddSagaStateMachine<CreateCustomerStateMachine, CreateCustomerState>()
                    .InMemoryRepository();

                cfg.AddSagaStateMachine<CreateOrderStateMachine, CreateOrderState>()
                    .InMemoryRepository();

                cfg.AddSagaStateMachine<ChangeOrderStatusStateMachine, ChangeOrderStatusState>()
                    .InMemoryRepository();

                var rabbitHost = builder.Configuration["RabbitMq:Host"];
                if (!string.IsNullOrEmpty(rabbitHost))
                {
                    cfg.UsingRabbitMq((context, rc) =>
                    {
                        rc.Host(rabbitHost, h =>
                        {
                            h.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
                            h.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
                        });
                        rc.ConfigureEndpoints(context);
                    });
                }
                else
                {
                    cfg.UsingInMemory((context, rc) =>
                    {
                        rc.ConfigureEndpoints(context);
                    });
                }
            });

            var host = builder.Build();
            host.Run();
        }
    }
}