using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Redisson.Net.Model;

namespace Redisson.Net
{
    public class LockSemaphoreManager
    {
        private readonly ConcurrentDictionary<string, SemaphoreCache> _caches = new();

        public async Task<bool> WaitAsync(string key, TimeSpan timeout)
        {
            var lastUsedTimestamp = Stopwatch.GetTimestamp();

            SemaphoreCache semaphoreCache;
            lock (string.Intern(key))
            {
                if (_caches.TryGetValue(key, out semaphoreCache))
                {
                    if (semaphoreCache.LastUsedTimestamp < lastUsedTimestamp)
                        semaphoreCache.LastUsedTimestamp = lastUsedTimestamp;
                }
                else
                {
                    semaphoreCache = new SemaphoreCache
                    {
                        Semaphore = new SemaphoreSlim(0),
                        LastUsedTimestamp = lastUsedTimestamp
                    };
                    if (!_caches.TryAdd(key, semaphoreCache))
                    {
                        // 基本不存在下列情况
                        semaphoreCache.Dispose();
                        throw new SynchronizationLockException("获取等待锁失败");
                    }
                }
            }

            // 上面的逻辑即使有锁也执行很快，暂不需要重新计算剩余超时时间
            // var surplusTicks = Stopwatch.GetTimestamp() - lastUsedTimestamp;
            // timeout = TimeSpan.FromTicks(timeout.Ticks - surplusTicks)

            return await semaphoreCache.Semaphore.WaitAsync(timeout);
        }

        public void Release(string key)
        {
            lock (string.Intern(key))
            {
                // 已经不存在则忽略
                if (!_caches.TryRemove(key, out var cache))
                    return;

                cache.Dispose();
            }
        }

        #region 回收缓存 [暂时用不上]
        /// <summary>
        /// 最后回收锁的Ticks
        /// </summary>
        private long _lastRecycleCacheTicks;

        /// <summary>
        /// 回收缓存锁
        /// </summary>
        private readonly SemaphoreSlim _recycleCacheLocker = new(1);

        /// <summary>
        /// 回收锁缓存
        /// </summary>
        /// <param name="expireInterval">过期间隔</param>
        public async Task RecycleCache(TimeSpan expireInterval)
        {
            // 1 秒内没有获取到执行锁则忽略本次回收
            var gotLock = await _recycleCacheLocker.WaitAsync(TimeSpan.FromSeconds(1));
            if (!gotLock) return;

            try
            {
                var expireIntervalTicks = Stopwatch.GetTimestamp() - expireInterval.Ticks;
                if (expireIntervalTicks < _lastRecycleCacheTicks) return;

                var expireCacheKeys = _caches
                    .Where(kv => kv.Value.LastUsedTimestamp < expireIntervalTicks)
                    .Select(kv => kv.Key)
                    .ToArray();
                foreach (var key in expireCacheKeys)
                {
                    Release(key);
                }

                _lastRecycleCacheTicks = expireIntervalTicks;
            }
            catch
            {
                // 忽略
            }
            finally
            {
                _recycleCacheLocker.Release();
            }
        }

        /// <summary>
        /// 启动回收任务 (请勿重复开启)
        /// </summary>
        public Task StartRecycleTask(TimeSpan recycleInterval, TimeSpan expireInterval)
        {
            const long RECYCLE_CACHE_SIZE = 5000;

            var lastRecycleCacheTime = new DateTime();
            return Task.Run(async () =>
            {
                do
                {
                    if (_caches.Count > RECYCLE_CACHE_SIZE || DateTime.Now - lastRecycleCacheTime > recycleInterval)
                        await RecycleCache(expireInterval);

                    await Task.Delay(TimeSpan.FromSeconds(1));
                } while (true);
            });
        }
        #endregion
    }
}