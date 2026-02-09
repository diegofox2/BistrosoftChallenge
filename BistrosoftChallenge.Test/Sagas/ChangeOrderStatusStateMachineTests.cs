using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BistrosoftChallenge.Domain.Entities;
using BistrosoftChallenge.Infrastructure;
using BistrosoftChallenge.MessageContracts;
using BistrosoftChallenge.Worker.Sagas;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BistrosoftChallenge.Worker.Test.Sagas
{
    [TestClass]
    public class ChangeOrderStatusStateMachineTests
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
                cfg.AddSagaStateMachine<ChangeOrderStatusStateMachine, ChangeOrderStatusState>()
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
        public async Task Should_Change_Status_When_Valid_Transition()
        {
            // Arrange
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                Status = OrderStatus.Pending,
                TotalAmount = 100,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();

            var correlationId = Guid.NewGuid();

            // Act
            await _harness.Bus.Publish(new ChangeOrderStatusCommand(correlationId, order.Id, OrderStatus.Paid));

            // Assert
            Assert.IsTrue(await _harness.Consumed.Any<ChangeOrderStatusCommand>());
            Assert.IsTrue(await _harness.Published.Any<OrderStatusChanged>());

            var evt = (await _harness.Published.SelectAsync<OrderStatusChanged>().First()).Context.Message;
            Assert.AreEqual(order.Id, evt.OrderId);
            Assert.AreEqual(OrderStatus.Paid, evt.NewStatus);

            await _dbContext.Entry(order).ReloadAsync();
            Assert.AreEqual(OrderStatus.Paid, order.Status);
        }

        [TestMethod]
        public async Task Should_Fail_When_Invalid_Transition()
        {
            // Arrange (Pending -> Delivered is invalid)
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                Status = OrderStatus.Pending,
                TotalAmount = 100,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();

            var correlationId = Guid.NewGuid();

            // Act
            await _harness.Bus.Publish(new ChangeOrderStatusCommand(correlationId, order.Id, OrderStatus.Delivered));

            // Assert
            Assert.IsTrue(await _harness.Published.Any<OrderStatusChangeFailed>());

            var fail = (await _harness.Published.SelectAsync<OrderStatusChangeFailed>().First()).Context.Message;
            Assert.IsTrue(fail.Reason.Contains("Invalid transition"));

            var dbOrder = await _dbContext.Orders.FindAsync(order.Id);
            Assert.AreEqual(OrderStatus.Pending, dbOrder.Status);
        }

        [TestMethod]
        public async Task Should_Fail_When_Order_Not_Found()
        {
            var correlationId = Guid.NewGuid();
            var orderId = Guid.NewGuid();

            await _harness.Bus.Publish(new ChangeOrderStatusCommand(correlationId, orderId, OrderStatus.Paid));

            Assert.IsTrue(await _harness.Published.Any<OrderStatusChangeFailed>());

            var fail = (await _harness.Published.SelectAsync<OrderStatusChangeFailed>().First()).Context.Message;
            Assert.AreEqual("Order not found", fail.Reason);
        }
    }
}
