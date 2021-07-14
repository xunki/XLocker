using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Redisson.Net
{
    public class LockManager : IDisposable
    {
        #region 初始化及释放
        public static LockManager GetLockManager(ConnectionMultiplexer redis, string prefix)
        {
            // 注册解锁事件
            redis.GetSubscriber().Subscribe(prefix + "*", UnlockEvent);
            // 注册脚本 [暂时支持单节点，目前测试用，后期会优化]
            var server = redis.GetServer(redis.GetEndPoints().First());
            var lockScript = new RedisScript.Lock().LoadScript(server);
            var unLockScript = new RedisScript.UnLock().LoadScript(server);

            return new LockManager(redis, prefix, lockScript, unLockScript);
        }

        public static async Task<LockManager> GetLockManagerAsync(ConnectionMultiplexer redis, string prefix)
        {
            // 注册解锁事件
            await redis.GetSubscriber().SubscribeAsync(prefix + "*", UnlockEvent);
            // 注册脚本 [暂时支持单节点，目前测试用，后期会优化]
            var server = redis.GetServer(redis.GetEndPoints().First());
            var lockScript = await new RedisScript.Lock().LoadScriptAsync(server);
            var unLockScript = await new RedisScript.UnLock().LoadScriptAsync(server);

            return new LockManager(redis, prefix, lockScript, unLockScript);
        }

        private static void UnlockEvent(RedisChannel key, RedisValue value)
        {
            LockSemaphoreManager.Release(key);
        }

        public void Dispose()
        {
            DisposeImpl();
            GC.SuppressFinalize(this);
        }

        ~LockManager()
        {
            Dispose();
        }

        private void DisposeImpl()
        {
            if (_redis != null)
            {
                if (_redis.IsConnecting)
                    _redis.GetSubscriber().UnsubscribeAsync(_prefix + "*");
                _redis.Dispose();
            }
        }

        private LockManager(ConnectionMultiplexer redis, string prefix, LoadedLuaScript lockScript, LoadedLuaScript unLockScript)
        {
            _redis = redis;
            _prefix = prefix;
            _lockScript = lockScript;
            _unLockScript = unLockScript;
        }

        private readonly ConnectionMultiplexer _redis;
        private readonly string _prefix;
        private readonly LoadedLuaScript _lockScript;
        private readonly LoadedLuaScript _unLockScript;
        #endregion

        public async Task<bool> Lock(string key, string value, TimeSpan expire, TimeSpan lockTimeout)
        {
            key = _prefix + key;
            var timeoutTicks = Stopwatch.GetTimestamp() + lockTimeout.Ticks;

            do
            {
                var ttl = (int?) await _lockScript.EvaluateAsync(_redis.GetDatabase(), new RedisScript.Lock
                {
                    Key = key,
                    Value = value,
                    Expire = (int) expire.TotalMilliseconds
                });
                if (ttl is null or 0)
                    return true;

                // 进行锁等待
                var nowTicks = Stopwatch.GetTimestamp();
                if (nowTicks > timeoutTicks)
                    return false;

                var waitTimeSpan = new TimeSpan(timeoutTicks - nowTicks);
                if (ttl >= 0 && ttl.Value < waitTimeSpan.TotalMilliseconds)
                    waitTimeSpan = TimeSpan.FromMilliseconds(ttl.Value);

                var waitTask = LockSemaphoreManager.WaitAsync(key, waitTimeSpan);

                // 再进行一次锁定，如果失败则进行等待，避免在等待本地锁生成前，已触发本地锁释放事件
                var ttl2 = (int?) await _lockScript.EvaluateAsync(_redis.GetDatabase(), new RedisScript.Lock
                {
                    Key = key,
                    Value = value,
                    Expire = (int) expire.TotalMilliseconds
                });
                if (ttl2 is null or 0)
                    return true;

                await waitTask;
            } while (true);
        }

        public async Task<bool> UnLock(string key, string value)
        {
            key = _prefix + key;
            var result = (int?) await _unLockScript.EvaluateAsync(_redis.GetDatabase(), new RedisScript.UnLock
            {
                Key = key,
                Value = value
            });
            return result.HasValue;
        }
    }
}