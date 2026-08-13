using System;
using SmartOps.Core.Entities;

namespace SmartOps.Core.Events;

public class TransactionEventArgs : EventArgs
{
    public Transaction Transaction { get; }

    public TransactionEventArgs(Transaction transaction)
    {
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }
}
