using System;
using MassTransit;

namespace BistrosoftChallenge.Infrastructure.SagaStates
{
    public class ChangeOrderStatusState : SagaStateMachineInstance
    {
        public Guid CorrelationId { get; set; }
        public string CurrentState { get; set; } = null!;
        public Guid OrderId { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? LastError { get; set; }
    }
}
