using System;
using SmartOps.Core.Events;
using SmartOps.Core.Interfaces;

namespace SmartOps.Infrastructure;

public class NoopTransactionPublisher : ITransactionPublisher
{
    public event EventHandler<TransactionEventArgs>? OnTransactionCreated;
}
