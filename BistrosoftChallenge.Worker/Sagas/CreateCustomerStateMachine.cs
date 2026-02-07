using System;
using Automatonymous;
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
                        var msg = context.Data;
                        var consumeContext = context.GetPayload<ConsumeContext>();
                        var db = consumeContext.GetRequiredService<AppDbContext>();

                        var emailTaken = await db.Customers.AnyAsync(c => c.Email == msg.Email, consumeContext.CancellationToken);
                        if (emailTaken)
                        {
                            context.Instance.LastError = "Email already exists";
                            context.Instance.UpdatedAt = DateTime.UtcNow;
                            await MassTransit.BehaviorContextExtensions.Publish(context, new CustomerCreationFailed(msg.CorrelationId, "Email already exists"));
                            return;
                        }

                        var existingCustomer = await db.Customers.FindAsync(new object[] { msg.CustomerId }, consumeContext.CancellationToken);
                        if (existingCustomer != null)
                        {
                            context.Instance.CustomerId = existingCustomer.Id;
                            context.Instance.UpdatedAt = DateTime.UtcNow;
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

                        context.Instance.CustomerId = customer.Id;
                        context.Instance.CreatedAt = DateTime.UtcNow;
                        context.Instance.UpdatedAt = DateTime.UtcNow;

                        await MassTransit.BehaviorContextExtensions.Publish(context, new CustomerCreated(msg.CorrelationId, customer.Id));
                    })
                    .IfElse(context => string.IsNullOrEmpty(context.Instance.LastError),
                        binder => binder.TransitionTo(Created).Finalize(),
                        binder => binder.Finalize())
            );

            SetCompletedWhenFinalized();
        }
    }
}
