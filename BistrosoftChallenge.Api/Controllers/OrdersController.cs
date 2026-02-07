using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using BistrosoftChallenge.MessageContracts;
using BistrosoftChallenge.Infrastructure.Repositories;
using BistrosoftChallenge.Domain.Entities;
using MassTransit;

namespace BistrosoftChallenge.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly IProductRepository _productRepository;

        public OrdersController(IPublishEndpoint publishEndpoint, IProductRepository productRepository)
        {
            _publishEndpoint = publishEndpoint;
            _productRepository = productRepository;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateOrderRequest req)
        {
            if (req.Items == null || req.Items.Length == 0)
            {
                return BadRequest("An order must contain at least one item.");
            }

            var orderId = Guid.NewGuid();
            var items = req.Items.Select(i => new OrderItemDto(i.ProductId, i.Quantity)).ToArray();
            var command = new CreateOrderCommand(Guid.NewGuid(), orderId, req.CustomerId, items);
            await _publishEndpoint.Publish(command);

            var totalAmount = 0m;
            foreach (var item in items)
            {
                var product = await _productRepository.GetByIdAsync(item.ProductId);
                if (product != null)
                {
                    totalAmount += product.Price * item.Quantity;
                }
            }

            var response = new CreateOrderResponse(orderId, OrderStatus.Pending, totalAmount, DateTime.UtcNow);
            return Created($"/api/customers/{req.CustomerId}/orders", response);
        }

        [HttpPut("{id:guid}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] ChangeOrderStatusRequest req)
        {
            await _publishEndpoint.Publish(new ChangeOrderStatusCommand(Guid.NewGuid(), id, req.NewStatus));
            return Ok(new OrderStatusResponse(id, req.NewStatus));
        }
    }

    public record CreateOrderRequest(Guid CustomerId, OrderItemRequest[] Items);
    public record OrderItemRequest(Guid ProductId, int Quantity);
    public record CreateOrderResponse(Guid OrderId, OrderStatus Status, decimal TotalAmount, DateTime CreatedAt);
    public record ChangeOrderStatusRequest(OrderStatus NewStatus);
    public record OrderStatusResponse(Guid OrderId, OrderStatus Status);
}
