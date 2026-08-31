using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Kleaner.WebHost;

/// <summary>发往 SSE 流的单条事件（工单 07：单一多路复用流，事件名区分类型、payload 内带 jobId）。</summary>
public sealed record SseEvent(string Event, string JobId, object Data);

/// <summary>SSE 事件订阅句柄：只收订阅之后发布的事件（工单 07：不做事件回放）。</summary>
public interface IJobEventSubscription : IDisposable
{
    ChannelReader<SseEvent> Reader { get; }
}

public interface IJobEventBus
{
    IJobEventSubscription Subscribe();

    void Publish(string eventName, string jobId, object data);
}

/// <summary>
/// SSE 事件广播：每个订阅者一条 unbounded channel，发布方 TryWrite、断连方 Dispose 退订。
/// 无历史缓冲——重连语义是 REST 快照 + SSE 增量（工单 07），本类刻意不做回放。
/// </summary>
public sealed class JobEventBus : IJobEventBus
{
    private readonly ConcurrentDictionary<Guid, Channel<SseEvent>> _subscribers = new();

    public IJobEventSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<SseEvent>(new UnboundedChannelOptions { SingleReader = true });
        _subscribers[id] = channel;
        return new Subscription(this, id, channel);
    }

    public void Publish(string eventName, string jobId, object data)
    {
        var evt = new SseEvent(eventName, jobId, data);
        foreach (var subscriber in _subscribers)
        {
            // 订阅者已在断连中退订时静默丢弃，不影响其余订阅者
            subscriber.Value.Writer.TryWrite(evt);
        }
    }

    private sealed class Subscription : IJobEventSubscription
    {
        private readonly JobEventBus _bus;
        private readonly Guid _id;

        public Subscription(JobEventBus bus, Guid id, Channel<SseEvent> channel)
        {
            _bus = bus;
            _id = id;
            Reader = channel.Reader;
        }

        public ChannelReader<SseEvent> Reader { get; }

        public void Dispose()
        {
            if (_bus._subscribers.TryRemove(_id, out var channel))
            {
                channel.Writer.TryComplete();
            }
        }
    }
}
