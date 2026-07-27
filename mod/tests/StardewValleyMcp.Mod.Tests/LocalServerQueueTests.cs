using System.Threading.Channels;
using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class LocalServerQueueTests
{
    [Test]
    public async Task WriterFaultCancelsBlockedReadLoop()
    {
        using var readLoopStop = new CancellationTokenSource();
        var writer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = LocalServer.CancelReadLoopWhenWriterStopsAsync(writer.Task, readLoopStop);

        writer.SetException(new IOException("writer fault"));
        await monitor;

        Assert.That(readLoopStop.IsCancellationRequested, Is.True);
    }

    [Test]
    public void EventQueueOverflowCompletesWriterAndCancelsReadLoop()
    {
        var outgoing = Channel.CreateBounded<TransportFrame>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
        });
        using var readLoopStop = new CancellationTokenSource();
        Assert.That(outgoing.Writer.TryWrite(new TransportFrame { MessageId = "occupied" }), Is.True);

        var queued = LocalServer.TryQueueEvent(
            outgoing.Writer,
            new TransportFrame { MessageId = "overflow" },
            readLoopStop
        );

        Assert.Multiple(() =>
        {
            Assert.That(queued, Is.False);
            Assert.That(readLoopStop.IsCancellationRequested, Is.True);
            Assert.That(outgoing.Writer.TryWrite(new TransportFrame { MessageId = "after-close" }), Is.False);
        });
    }
}
