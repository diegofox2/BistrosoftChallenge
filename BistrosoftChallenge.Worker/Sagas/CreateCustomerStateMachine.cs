using System;
using MassTransit;
using BistrosoftChallenge.MessageContracts;
using BistrosoftChallenge.Infrastructure;
using BistrosoftChallenge.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BistrosoftChallenge.Worker.Sagas
{
    public class CreateCustomerStateMachine : MassTransitStateMachine<CreateCustomerState>
    {
        public State Created { get; private set; }
        public Event<CreateCustomerCommand> CreateCustomer { get; private set; }

        public CreateCustomerStateMachine()
        {
            InstanceState(x => x.CurrentState);

            Event(() => CreateCustomer, x =>
            {
                x.CorrelateById(m => m.Message.CorrelationId);
                x.SelectId(m => m.Message.CorrelationId);
            });

            Initially(
                When(CreateCustomer)
                    .ThenAsync(async context =>
                    {
                        var msg = context.Message;
                        var consumeContext = context.GetPayload<ConsumeContext>();
                        var db = consumeContext.GetPayload<IServiceProvider>().GetRequiredService<AppDbContext>();

                        var emailTaken = await db.Customers.AnyAsync(c => c.Email == msg.Email, consumeContext.CancellationToken);
                        if (emailTaken)
                        {
                            context.Saga.LastError = "Email already exists";
                            context.Saga.UpdatedAt = DateTime.UtcNow;
                            await context.Publish(new CustomerCreationFailed(msg.CorrelationId, "Email already exists"));
                            return;
                        }

                        var existingCustomer = await db.Customers.FindAsync(new object[] { msg.CustomerId }, consumeContext.CancellationToken);
                        if (existingCustomer != null)
                        {
                            context.Saga.CustomerId = existingCustomer.Id;
                            context.Saga.UpdatedAt = DateTime.UtcNow;
                            return;
                        }

                        var customer = new Customer
                        {
                            Id = msg.CustomerId,
                            Name = msg.Name,
                            Email = msg.Email,
                            PhoneNumber = msg.PhoneNumber
                        };

                        db.Customers.Add(customer);
                        await db.SaveChangesAsync(consumeContext.CancellationToken);

                        context.Saga.CustomerId = customer.Id;
                        context.Saga.CreatedAt = DateTime.UtcNow;
                        context.Saga.UpdatedAt = DateTime.UtcNow;

                        await context.Publish(new CustomerCreated(msg.CorrelationId, customer.Id));
                    })
                    .IfElse(context => string.IsNullOrEmpty(context.Saga.LastError),
                        binder => binder.TransitionTo(Created).Finalize(),
                        binder => binder.Finalize())
            );

            SetCompletedWhenFinalized();
        }
    }
}
