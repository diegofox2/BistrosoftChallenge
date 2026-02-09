using BistrosoftChallenge.Domain.Entities;
using BistrosoftChallenge.Infrastructure;
using BistrosoftChallenge.MessageContracts;
using MassTransit;

namespace BistrosoftChallenge.Worker.Sagas
{
    public class CreateOrderStateMachine : MassTransitStateMachine<CreateOrderState>
    {
        public State Created { get; private set; }
        public Event<CreateOrderCommand> CreateOrder { get; private set; }

        public CreateOrderStateMachine()
        {
            InstanceState(x => x.CurrentState);

            Event(() => CreateOrder, x =>
            {
                x.CorrelateById(m => m.Message.CorrelationId);
                x.SelectId(m => m.Message.CorrelationId);
            });

            Initially(
                When(CreateOrder)
                    .ThenAsync(async context =>
                    {
                        var msg = context.Message;
                        var consumeContext = context.GetPayload<ConsumeContext>();
                        var db = consumeContext.GetPayload<IServiceProvider>().GetRequiredService<AppDbContext>();

                        var customer = await db.Customers.FindAsync(new object[] { msg.CustomerId }, consumeContext.CancellationToken);
                        if (customer == null)
                        {
                            context.Saga.LastError = "Customer not found";
                            context.Saga.UpdatedAt = DateTime.UtcNow;
                            await context.Publish(new OrderCreationFailed(msg.CorrelationId, msg.OrderId, "Customer not found"));
                            return;
                        }

                        var productInfos = new List<(Product product, OrderItemDto item)>();
                        var totalAmount = 0m;

                        foreach (var item in msg.Items)
                        {
                            if (item.Quantity <= 0)
                            {
                                context.Saga.LastError = "Product quantities must be greater than zero";
                                context.Saga.UpdatedAt = DateTime.UtcNow;
                                await context.Publish(new OrderCreationFailed(msg.CorrelationId, msg.OrderId, "Product quantities must be greater than zero"));
                                return;
                            }

                            var product = await db.Products.FindAsync(new object[] { item.ProductId }, consumeContext.CancellationToken);
                            if (product == null)
                            {
                                var reason = $"Product {item.ProductId} not found";
                                context.Saga.LastError = reason;
                                context.Saga.UpdatedAt = DateTime.UtcNow;
                                await context.Publish(new OrderCreationFailed(msg.CorrelationId, msg.OrderId, reason));
                                return;
                            }

                            if (product.StockQuantity < item.Quantity)
                            {
                                var reason = $"Insufficient stock for product {product.Name}";
                                context.Saga.LastError = reason;
                                context.Saga.UpdatedAt = DateTime.UtcNow;
                                await context.Publish(new OrderCreationFailed(msg.CorrelationId, msg.OrderId, reason));
                                return;
                            }

                            productInfos.Add((product, item));
                            totalAmount += product.Price * item.Quantity;
                        }

                        var order = new Order
                        {
                            Id = msg.OrderId,
                            CustomerId = customer.Id,
                            CreatedAt = DateTime.UtcNow,
                            TotalAmount = totalAmount,
                            Status = OrderStatus.Pending
                        };

                        foreach (var (product, item) in productInfos)
                        {
                            order.OrderItems.Add(new OrderItem
                            {
                                Id = Guid.NewGuid(),
                                OrderId = order.Id,
                                ProductId = product.Id,
                                Quantity = item.Quantity,
                                UnitPrice = product.Price
                            });

                            product.StockQuantity -= item.Quantity;
                        }

                        db.Orders.Add(order);
                        await db.SaveChangesAsync(consumeContext.CancellationToken);

                        context.Saga.OrderId = order.Id;
                        context.Saga.CustomerId = order.CustomerId;
                        context.Saga.CreatedAt = DateTime.UtcNow;
                        context.Saga.UpdatedAt = DateTime.UtcNow;

                        await context.Publish(new OrderCreated(msg.CorrelationId, order.Id, order.TotalAmount));
                    })
                    .IfElse(context => string.IsNullOrEmpty(context.Saga.LastError),
                        binder => binder.TransitionTo(Created).Finalize(),
                        binder => binder.Finalize())
            );

            SetCompletedWhenFinalized();
        }
    }
}
