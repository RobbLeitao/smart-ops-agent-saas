using System;
using SmartOps.Core;
using SmartOps.Core.Events;

namespace SmartOps.Core.Interfaces;

public interface ITransactionPublisher
{
    event EventHandler<TransactionEventArgs>? OnTransactionCreated;
}
