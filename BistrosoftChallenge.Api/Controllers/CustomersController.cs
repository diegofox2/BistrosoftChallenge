using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using BistrosoftChallenge.Infrastructure.Repositories;
using BistrosoftChallenge.MessageContracts;
using BistrosoftChallenge.Domain.Entities;
using MassTransit;

namespace BistrosoftChallenge.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CustomersController : ControllerBase
    {
        private readonly ICustomerRepository _repo;
        private readonly IOrderRepository _orderRepository;
        private readonly IPublishEndpoint _publishEndpoint;

        public CustomersController(ICustomerRepository repo, IOrderRepository orderRepository, IPublishEndpoint publishEndpoint)
        {
            _repo = repo;
            _orderRepository = orderRepository;
            _publishEndpoint = publishEndpoint;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateRequest req)
        {
            var id = Guid.NewGuid();
            var cmd = new CreateCustomerCommand(Guid.NewGuid(), id, req.Name, req.Email, req.PhoneNumber);
            await _publishEndpoint.Publish(cmd);
            return CreatedAtAction(nameof(Get), new { id }, new { id, req.Name, req.Email, req.PhoneNumber });
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var c = await _repo.GetByIdAsync(id);
            if (c == null)
            {
                return NotFound();
            }

            return Ok(new
            {
                c.Id,
                c.Name,
                c.Email,
                c.PhoneNumber,
                Orders = c.Orders.Select(o => new { o.Id, o.Status, o.TotalAmount, o.CreatedAt })
            });
        }

        [HttpGet("{id:guid}/orders")]
        public async Task<IActionResult> GetOrders(Guid id)
        {
            var customer = await _repo.GetByIdAsync(id);
            if (customer == null)
            {
                return NotFound();
            }

            var orders = await _orderRepository.GetByCustomerAsync(id);
            var response = orders.Select(order => new CustomerOrderResponse(
                order.Id,
                order.Status,
                order.TotalAmount,
                order.CreatedAt,
                order.OrderItems.Select(item => new CustomerOrderItem(item.ProductId, item.Quantity, item.UnitPrice))));

            return Ok(response);
        }
    }

    public record CreateRequest(string Name, string Email, string? PhoneNumber);
    public record CustomerOrderItem(Guid ProductId, int Quantity, decimal UnitPrice);
    public record CustomerOrderResponse(Guid OrderId, OrderStatus Status, decimal TotalAmount, DateTime CreatedAt, IEnumerable<CustomerOrderItem> Items);
}
