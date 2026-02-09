using System;
using MassTransit;

namespace BistrosoftChallenge.Worker.Sagas
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
