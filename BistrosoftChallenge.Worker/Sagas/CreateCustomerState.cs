using System;
using MassTransit;

namespace BistrosoftChallenge.Worker.Sagas
{
    public class CreateCustomerState : SagaStateMachineInstance
    {
        public Guid CorrelationId { get; set; }
        public string CurrentState { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public Guid CustomerId { get; set; }
        public string? LastError { get; set; }
        public bool Completed { get; set; }
    }
}
