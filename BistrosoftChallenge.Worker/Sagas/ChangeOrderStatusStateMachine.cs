using BistrosoftChallenge.Domain.Entities;
using BistrosoftChallenge.Infrastructure.SagaStates;
using BistrosoftChallenge.Infrastructure;
using BistrosoftChallenge.MessageContracts;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace BistrosoftChallenge.Worker.Sagas
{
    public class ChangeOrderStatusStateMachine : MassTransitStateMachine<ChangeOrderStatusState>
    {
        public State Completed { get; private set; }
        public Event<ChangeOrderStatusCommand> ChangeOrderStatus { get; private set; }

        public ChangeOrderStatusStateMachine()
        {
            InstanceState(x => x.CurrentState);

            Event(() => ChangeOrderStatus, x =>
            {
                x.CorrelateById(m => m.Message.CorrelationId);
                x.SelectId(m => m.Message.CorrelationId);
            });

            Initially(
                When(ChangeOrderStatus)
                    .ThenAsync(async context =>
                    {
                        var msg = context.Message;
                        var consumeContext = context.GetPayload<ConsumeContext>();
                        var db = consumeContext.GetPayload<IServiceProvider>().GetRequiredService<AppDbContext>();

                        var order = await db.Orders.FindAsync(new object[] { msg.OrderId }, consumeContext.CancellationToken);
                        if (order == null)
                        {
                            context.Saga.LastError = "Order not found";
                            context.Saga.UpdatedAt = DateTime.UtcNow;
                            await context.Publish(new OrderStatusChangeFailed(msg.CorrelationId, msg.OrderId, "Order not found"));
                            return;
                        }

                        if (order.Status == msg.NewStatus)
                        {
                            await context.Publish(new OrderStatusChanged(msg.CorrelationId, order.Id, order.Status));
                            return;
                        }

                        if (!IsValidTransition(order.Status, msg.NewStatus))
                        {
                            var reason = $"Invalid transition from {order.Status} to {msg.NewStatus}";
                            context.Saga.LastError = reason;
                            context.Saga.UpdatedAt = DateTime.UtcNow;
                            await context.Publish(new OrderStatusChangeFailed(msg.CorrelationId, order.Id, reason));
                            return;
                        }

                        order.Status = msg.NewStatus;
                        await db.SaveChangesAsync(consumeContext.CancellationToken);
                        context.Saga.OrderId = order.Id;
                        context.Saga.UpdatedAt = DateTime.UtcNow;
                        await context.Publish(new OrderStatusChanged(msg.CorrelationId, order.Id, msg.NewStatus));
                    })
                    .IfElse(context => string.IsNullOrEmpty(context.Saga.LastError),
                        binder => binder.TransitionTo(Completed).Finalize(),
                        binder => binder.Finalize())
            );

            SetCompletedWhenFinalized();
        }

        private static bool IsValidTransition(OrderStatus current, OrderStatus next)
        {
            return current switch
            {
                OrderStatus.Pending => next == OrderStatus.Paid || next == OrderStatus.Cancelled,
                OrderStatus.Paid => next == OrderStatus.Shipped || next == OrderStatus.Cancelled,
                OrderStatus.Shipped => next == OrderStatus.Delivered,
                _ => false
            };
        }
    }
}
