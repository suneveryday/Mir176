using SystemModule.Data;

namespace M2Server.Actor
{
    /// <summary>
    /// Preserve enqueue order within each explicit message priority.
    /// Producers include both the world loop and actor message handlers.
    /// </summary>
    public sealed class ActorMessageQueue
    {
        private readonly object sync = new();
        private readonly PriorityQueue<SendMessage, (byte Priority, long Sequence)> queue = new();
        private long sequence;

        public int Count
        {
            get
            {
                lock (sync)
                {
                    return queue.Count;
                }
            }
        }

        public void Enqueue(SendMessage message, byte priority)
        {
            lock (sync)
            {
                queue.Enqueue(message, (priority, sequence++));
            }
        }

        public bool TryDequeue(out SendMessage message, out byte priority)
        {
            lock (sync)
            {
                bool found = queue.TryDequeue(out message, out var entry);
                priority = entry.Priority;
                return found;
            }
        }

        public bool TryPeek(out SendMessage message, out byte priority)
        {
            lock (sync)
            {
                bool found = queue.TryPeek(out message, out var entry);
                priority = entry.Priority;
                return found;
            }
        }
    }
}
