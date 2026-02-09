using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using MassTransit;
using MassTransit.Testing;
using BistrosoftChallenge.Worker.Sagas;
using BistrosoftChallenge.Infrastructure;
using Microsoft.EntityFrameworkCore;
using BistrosoftChallenge.MessageContracts;
using BistrosoftChallenge.Domain.Entities;
using System.Linq;

namespace BistrosoftChallenge.Worker.Test.Sagas
{
    [TestClass]
    public class CreateOrderStateMachineTests
    {
        private ServiceProvider _provider = null!;
        private ITestHarness _harness = null!;
        private AppDbContext _dbContext = null!;
        private string _databaseName = string.Empty;

        [TestInitialize]
        public async Task Setup()
        {
            var services = new ServiceCollection();

            _databaseName = Guid.NewGuid().ToString();

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));

            services.AddMassTransitTestHarness(cfg =>
            {
                cfg.AddSagaStateMachine<CreateOrderStateMachine, CreateOrderState>()
                    .InMemoryRepository();
            });

            _provider = services.BuildServiceProvider();
            _harness = _provider.GetRequiredService<ITestHarness>();
            _dbContext = _provider.GetRequiredService<AppDbContext>();

            await _harness.Start();
        }

        [TestCleanup]
        public async Task Teardown()
        {
            await _harness.Stop();
            await _provider.DisposeAsync();
        }

        [TestMethod]
        public async Task Should_Create_Order_When_Valid()
        {
            // Arrange
            var customer = new Customer { Id = Guid.NewGuid(), Name = "Test", Email = "test@test.com", PhoneNumber = "123" };
            var product = new Product { Id = Guid.NewGuid(), Name = "Prod1", Price = 10m, StockQuantity = 100 };
            
            _dbContext.Customers.Add(customer);
            _dbContext.Products.Add(product);
            await _dbContext.SaveChangesAsync();

            var orderId = Guid.NewGuid();
            var correlationId = orderId;
            var items = new List<OrderItemDto>
            {
                new(product.Id, 2)
            };

            // Act
            await _harness.Bus.Publish(new CreateOrderCommand(correlationId, orderId, customer.Id, items));

            // Assert
            Assert.IsTrue(await _harness.Consumed.Any<CreateOrderCommand>());
            Assert.IsTrue(await _harness.Published.Any<OrderCreated>());

            var orderCreated = (await _harness.Published.SelectAsync<OrderCreated>().First()).Context.Message;
            Assert.AreEqual(20m, orderCreated.TotalAmount);
            Assert.AreEqual(orderId, orderCreated.OrderId);

            var dbOrder = await _dbContext.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == orderId);
            Assert.IsNotNull(dbOrder);
            Assert.AreEqual(OrderStatus.Pending, dbOrder.Status);
            Assert.AreEqual(1, dbOrder.OrderItems.Count);
            Assert.AreEqual(20m, dbOrder.TotalAmount);

            // Verify Stock Reduced
            var dbProduct = await _dbContext.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
            Assert.AreEqual(98, dbProduct.StockQuantity);
        }

        [TestMethod]
        public async Task Should_Fail_When_Customer_Not_Found()
        {
            var orderId = Guid.NewGuid();
            var correlationId = orderId;
            var items = new List<OrderItemDto>();

            await _harness.Bus.Publish(new CreateOrderCommand(correlationId, orderId, Guid.NewGuid(), items));

            Assert.IsTrue(await _harness.Published.Any<OrderCreationFailed>());
            var fail = (await _harness.Published.SelectAsync<OrderCreationFailed>().First()).Context.Message;
            Assert.AreEqual("Customer not found", fail.Reason);
        }

        [TestMethod]
        public async Task Should_Fail_When_Insufficient_Stock()
        {
            var customer = new Customer { Id = Guid.NewGuid(), Name = "Test", Email = "test@test.com", PhoneNumber = "123" };
            var product = new Product { Id = Guid.NewGuid(), Name = "Prod1", Price = 10m, StockQuantity = 1 };
            
            _dbContext.Customers.Add(customer);
            _dbContext.Products.Add(product);
            await _dbContext.SaveChangesAsync();

            var orderId = Guid.NewGuid();
            var correlationId = orderId;
            var items = new List<OrderItemDto>
            {
                new OrderItemDto(product.Id, 2)
            };

            await _harness.Bus.Publish(new CreateOrderCommand(correlationId, orderId, customer.Id, items));

            Assert.IsTrue(await _harness.Published.Any<OrderCreationFailed>());
            var fail = (await _harness.Published.SelectAsync<OrderCreationFailed>().First()).Context.Message;
            Assert.IsTrue(fail.Reason.Contains("Insufficient stock"));
        }
    }
}
