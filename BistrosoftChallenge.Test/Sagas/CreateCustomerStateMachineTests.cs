using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using MassTransit;
using MassTransit.Testing;
using BistrosoftChallenge.Worker.Sagas;
using BistrosoftChallenge.Infrastructure.SagaStates;
using BistrosoftChallenge.Infrastructure;
using Microsoft.EntityFrameworkCore;
using BistrosoftChallenge.MessageContracts;
using System.Linq;

namespace BistrosoftChallenge.Worker.Test.Sagas
{
    [TestClass]
    public class CreateCustomerStateMachineTests
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
                cfg.AddSagaStateMachine<CreateCustomerStateMachine, CreateCustomerState>()
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
        public async Task Should_Create_Customer_When_Valid()
        {
            var customerId = Guid.NewGuid();
            var correlationId = customerId;

            await _harness.Bus.Publish(new CreateCustomerCommand(correlationId, customerId, "John Doe", "john@example.com", "123456789"));

            Assert.IsTrue(await _harness.Consumed.Any<CreateCustomerCommand>());
            
            var sagaHarness = _harness.GetSagaStateMachineHarness<CreateCustomerStateMachine, CreateCustomerState>();
            Assert.IsTrue(await sagaHarness.Consumed.Any<CreateCustomerCommand>());
            Assert.IsTrue(await sagaHarness.Created.Any(x => x.CorrelationId == correlationId));

            // Verify State Machine State
            var instance = sagaHarness.Created.Contains(correlationId);
            Assert.IsNotNull(instance);
            
            // Verify DB
            var customer = await _dbContext.Customers.FindAsync(customerId);
            Assert.IsNotNull(customer);
            Assert.AreEqual("john@example.com", customer.Email);
            
            // Verify Event Published
            Assert.IsTrue(await _harness.Published.Any<CustomerCreated>());
        }

        [TestMethod]
        public async Task Should_Fail_When_Email_Exists()
        {
            // Arrange
            var existingCustomer = new BistrosoftChallenge.Domain.Entities.Customer
            {
                Id = Guid.NewGuid(),
                Name = "Existing",
                Email = "existing@example.com",
                PhoneNumber = "000000000"
            };
            _dbContext.Customers.Add(existingCustomer);
            await _dbContext.SaveChangesAsync();

            var customerId = Guid.NewGuid();
            var correlationId = customerId;

            // Act
            await _harness.Bus.Publish(new CreateCustomerCommand(correlationId, customerId, "New User", "existing@example.com", "111111111"));

            // Assert
            Assert.IsTrue(await _harness.Consumed.Any<CreateCustomerCommand>());
            Assert.IsTrue(await _harness.Published.Any<CustomerCreationFailed>());
            
            var failEvent = await _harness.Published.SelectAsync<CustomerCreationFailed>().First();
            Assert.AreEqual("Email already exists", failEvent.Context.Message.Reason);
        }

        [TestMethod]
        public async Task Should_Be_Idempotent_When_Customer_Already_Exists()
        {
            // Arrange
            var customerId = Guid.NewGuid();
            var existingCustomer = new BistrosoftChallenge.Domain.Entities.Customer
            {
                Id = customerId,
                Name = "Existing",
                Email = "existing@example.com",
                PhoneNumber = "000000000"
            };
            _dbContext.Customers.Add(existingCustomer);
            await _dbContext.SaveChangesAsync();

            var correlationId = customerId;

            // Act
            await _harness.Bus.Publish(new CreateCustomerCommand(correlationId, customerId, "New User", "new@example.com", "111111111"));

            // Assert
            Assert.IsTrue(await _harness.Consumed.Any<CreateCustomerCommand>());
            
            // Should NOT publish created or failed, just update saga (this logic depends on the state machine)
            // Looking at the code: 
            /*
             if (existingCustomer != null)
             {
                 context.Saga.CustomerId = existingCustomer.Id;
                 context.Saga.UpdatedAt = DateTime.UtcNow;
                 return;
             }
            */
            // It just returns, so it stays in "Initial" state or whatever state it was?
            // Actually `Initially` transitions are usually defined.
            // But here it returns before transitioning?
            // "When(CreateCustomer).ThenAsync(...)"
            // If it returns, the state transition continues? 
            // The code has:
            /*
            .IfElse(context => string.IsNullOrEmpty(context.Saga.LastError),
                binder => binder.TransitionTo(Created).Finalize(),
                binder => binder.Finalize())
            */
            // Ideally it should transition to Finalized if it handled it.
            // But `existingCustomer != null` path sets Saga properites and returns.
            // The IfElse checks LastError. LastError is null (default).
            // So it transitions to Created and Finalizes.
            
            var sagaHarness = _harness.GetSagaStateMachineHarness<CreateCustomerStateMachine, CreateCustomerState>();
            Assert.IsTrue(await sagaHarness.Consumed.Any<CreateCustomerCommand>());
            
            // It should NOT publish CustomerCreated again in the existing logic? 
            // Code: await context.Publish(new CustomerCreated(...)) is AFTER the check. 
            // So it does NOT publish CustomerCreated.
            
            Assert.IsFalse(await _harness.Published.Any<CustomerCreated>());
            Assert.IsFalse(await _harness.Published.Any<CustomerCreationFailed>());
        }
    }
}
