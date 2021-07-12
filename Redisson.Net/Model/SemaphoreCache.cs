using System;
using System.Threading;

namespace Redisson.Net.Model
{
    public class SemaphoreCache : IDisposable
    {
        /// <summary>
        /// 等待锁
        /// </summary>
        public SemaphoreSlim Semaphore { get; set; }

        /// <summary>
        /// 最后使用时间戳
        /// </summary>
        public long LastUsedTimestamp { get; set; }

        public void Dispose()
        {
            Semaphore?.Release(int.MaxValue);
            GC.SuppressFinalize(this);
        }

        ~SemaphoreCache()
        {
            Dispose();
        }
    }
}