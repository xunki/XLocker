using System;
using System.Diagnostics;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Redisson.Net
{
    public class LockManager
    {
        public LockManager(ConnectionMultiplexer redis, string prefix, LockSemaphoreManager lockSemaphoreManager)
        {
            _redis = redis;
            _prefix = prefix + "LOCK:";
            _lockSemaphoreManager = lockSemaphoreManager;
        }

        private readonly ConnectionMultiplexer _redis;
        private readonly string _prefix;
        private readonly LockSemaphoreManager _lockSemaphoreManager;

        #region 注册订阅/取消订阅解锁事件
        public Task SubscribeAsync()
        {
            return _redis.GetSubscriber().SubscribeAsync(_prefix + "*", UnlockEvent);
        }

        public Task Unsubscribe()
        {
            return _redis.GetSubscriber().UnsubscribeAsync(_prefix + "*");
        }

        private void UnlockEvent(RedisChannel key, RedisValue value)
        {
            _lockSemaphoreManager.Release(key);
        }
        #endregion

        public async Task<bool> Lock(string key, string value, TimeSpan expire, TimeSpan lockTimeout)
        {
            key = _prefix + key;
            var timeoutTicks = Stopwatch.GetTimestamp() + lockTimeout.Ticks;

            do
            {
                // 以下指令可以通过脚本进行一次性操作
                var success = await _redis.GetDatabase().LockTakeAsync(key, value, expire);
                if (success)
                    return true;
                var ttl = await _redis.GetDatabase().KeyTimeToLiveAsync(key);

                // 进行锁等待
                var nowTicks = Stopwatch.GetTimestamp();
                if (nowTicks > timeoutTicks)
                    return false;

                var waitTimeSpan = new TimeSpan(timeoutTicks - nowTicks);
                if (ttl != null && ttl.Value < waitTimeSpan)
                    waitTimeSpan = ttl.Value;

                var waitTask = _lockSemaphoreManager.WaitAsync(key, waitTimeSpan);

                // 再进行一次锁定，如果失败则进行等待，避免在等待本地锁生成前，已触发本地锁释放事件
                success = await _redis.GetDatabase().LockTakeAsync(key, value, expire);
                if (success)
                    return true;
                await waitTask;
            } while (true);
        }

        public async Task<bool> UnLock(string key, string value)
        {
            key = _prefix + key;

            // 以下指令可以通过脚本进行一次性操作
            var success = await _redis.GetDatabase().LockReleaseAsync(key, value);
            if (success)
                await _redis.GetSubscriber().PublishAsync(key, "");
            return success;
        }
    }
}