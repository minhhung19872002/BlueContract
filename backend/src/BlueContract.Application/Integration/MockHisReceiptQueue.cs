using System.Collections.Concurrent;

namespace BlueContract.Application.Integration;

/// <summary>In-memory receipts "lập trên HIS giả lập" waiting for the pull job (kept for the process lifetime).</summary>
public sealed class MockHisReceiptQueue : IMockHisReceiptQueue
{
    private readonly ConcurrentQueue<HisReceiptPayload> _receipts = new();

    public void Add(HisReceiptPayload payload)
    {
        _receipts.Enqueue(payload);
        while (_receipts.Count > 500 && _receipts.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<HisReceiptPayload> Snapshot() => [.. _receipts];
}
