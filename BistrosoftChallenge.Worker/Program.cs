using BistrosoftChallenge.Infrastructure;
using BistrosoftChallenge.Infrastructure.SagaStates;
using BistrosoftChallenge.Worker.Sagas;
using MassTransit;
using Microsoft.EntityFrameworkCore;

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
                cfg.AddEntityFrameworkOutbox<AppDbContext>(o =>
                {
                    o.UseSqlServer();
                    o.UseBusOutbox();
                });

                cfg.AddSagaStateMachine<CreateCustomerStateMachine, CreateCustomerState>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ExistingDbContext<AppDbContext>();
                        r.UseSqlServer();
                    });

                cfg.AddSagaStateMachine<CreateOrderStateMachine, CreateOrderState>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ExistingDbContext<AppDbContext>();
                        r.UseSqlServer();
                    });

                cfg.AddSagaStateMachine<ChangeOrderStatusStateMachine, ChangeOrderStatusState>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ExistingDbContext<AppDbContext>();
                        r.UseSqlServer();
                    });

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