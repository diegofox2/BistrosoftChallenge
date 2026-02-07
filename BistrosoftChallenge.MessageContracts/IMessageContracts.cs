using System;
using System.Collections.Generic;
using BistrosoftChallenge.Domain.Entities;

namespace BistrosoftChallenge.MessageContracts
{
    public record CreateCustomerCommand(Guid CorrelationId, Guid CustomerId, string Name, string Email, string? PhoneNumber);
    public record OrderItemDto(Guid ProductId, int Quantity);
    public record CreateOrderCommand(Guid CorrelationId, Guid OrderId, Guid CustomerId, IReadOnlyList<OrderItemDto> Items);
    public record ChangeOrderStatusCommand(Guid CorrelationId, Guid OrderId, OrderStatus NewStatus);

    public record CustomerCreated(Guid CorrelationId, Guid CustomerId);
    public record CustomerCreationFailed(Guid CorrelationId, string Reason);

    public record OrderCreated(Guid CorrelationId, Guid OrderId, decimal TotalAmount);
    public record OrderCreationFailed(Guid CorrelationId, Guid OrderId, string Reason);

    public record OrderStatusChanged(Guid CorrelationId, Guid OrderId, OrderStatus NewStatus);
    public record OrderStatusChangeFailed(Guid CorrelationId, Guid OrderId, string Reason);
}
