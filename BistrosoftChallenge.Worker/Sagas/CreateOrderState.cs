using System;
using Automatonymous;

namespace BistrosoftChallenge.Worker.Sagas
{
    public class CreateOrderState : SagaStateMachineInstance
    {
        public Guid CorrelationId { get; set; }
        public string CurrentState { get; set; } = null!;
        public Guid OrderId { get; set; }
        public Guid CustomerId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? LastError { get; set; }
    }
}
