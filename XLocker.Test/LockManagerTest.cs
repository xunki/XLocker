using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace XLocker.Test
{
    public class LockManagerTest
    {
        private readonly ITestOutputHelper _console;
        private readonly ConnectionMultiplexer _redis;
        private readonly LockManager _lockManager;

        public LockManagerTest(ITestOutputHelper console)
        {
            _console = console;
            _redis = ConnectionMultiplexer.Connect("192.168.2.172:56379");
            _lockManager = LockManager.GetLockManagerAsync(_redis).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        [Fact]
        public async Task TestRedisSub()
        {
            await _redis.GetSubscriber().SubscribeAsync("test:*",
                (key, value) => _console.WriteLine($"Key: {key} Value:{value}"));

            await _redis.GetDatabase().PublishAsync("test:1", "1");
            await _redis.GetDatabase().PublishAsync("test:2", "2");
            await _redis.GetDatabase().PublishAsync("test:3", "3");
            await Task.Delay(100);
        }

        private async Task Lock(string key, Func<Task> func)
        {
            var value = Guid.NewGuid().ToString("N")[..2];
            _console.WriteLine(value + " 等待加锁...");
            var success = await _lockManager.LockAsync(key, value,
                TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(30));
            _console.WriteLine("{0} {1}", value, success ? "加锁成功" : "加锁失败");

            if (success)
            {
                await func();
                success = await _lockManager.UnLockAsync(key, value);
                _console.WriteLine("{0} {1}", value, success ? "解锁成功" : "解锁失败");
            }
        }

        [Fact]
        public async Task TestLock()
        {
            const string TEST_KEY = "test";

            async Task Func()
            {
                _console.WriteLine("===执行逻辑中===");
                await Task.Delay(TimeSpan.FromSeconds(3));
                _console.WriteLine("===执行逻辑完成===");
            }

            await Task.WhenAll(Lock(TEST_KEY, Func), Lock(TEST_KEY, Func), Lock(TEST_KEY, Func));
        }

        [Fact]
        public async Task Benchmark()
        {
            for (var k = 0; k < 10; k++)
            {
                var sw = Stopwatch.StartNew();
                const string KEY_PREFIX = "test";
                var tasks = new List<Task>();
                for (var i = 0; i < 10; i++)
                {
                    for (var j = 0; j < 50; j++)
                    {
                        var index = i;
                        tasks.Add(Task.Run(async () =>
                        {
                            var key = KEY_PREFIX + index;
                            var value = Guid.NewGuid().ToString("N")[..2];
                            var success = await _lockManager.LockAsync(key, value,
                                TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(30));

                            if (success)
                                await _lockManager.UnLockAsync(key, value);
                        }));
                    }
                }
                await Task.WhenAll(tasks);
                _console.WriteLine($"耗时：{sw.ElapsedMilliseconds}ms");
            }
        }

        [Fact]
        public async Task TestLuaScript()
        {
            const string KEY = "test";
            var value = Guid.NewGuid().ToString("n");

            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var lockScript = await new RedisScript.Lock().LoadScriptAsync(server);

            var ttl = (int) await lockScript.EvaluateAsync(_redis.GetDatabase(),
                new RedisScript.Lock
                {
                    Key = KEY,
                    Expire = (int) TimeSpan.FromMinutes(5).TotalMilliseconds,
                    Value = value
                });
            _console.WriteLine($"加锁{(ttl == 0 ? "成功" : "失败")} ttl: {ttl}");

            // 加锁成功后解锁
            if (ttl == 0)
            {
                var unLockScript = await new RedisScript.UnLock().LoadScriptAsync(server);
                var result = (int?) await unLockScript.EvaluateAsync(_redis.GetDatabase(), new RedisScript.UnLock
                {
                    Key = KEY,
                    Value = value
                });
                _console.WriteLine("unlock: " + result);
            }
        }

        [Fact]
        public async Task TestReentryLock()
        {
            const string KEY = "REENTRY";

            var success = await _lockManager.LockAsync(KEY, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30));
            _console.WriteLine(Thread.CurrentThread.ManagedThreadId + ": " + success);
            success = await _lockManager.LockAsync(KEY, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30));
            _console.WriteLine(Thread.CurrentThread.ManagedThreadId + ": " + success);
            success = await _lockManager.LockAsync(KEY, Guid.NewGuid().ToString("N"), TimeSpan.FromMinutes(5),
                TimeSpan.FromSeconds(1));
            _console.WriteLine(Thread.CurrentThread.ManagedThreadId + ": " + success);

            await _lockManager.UnLockAsync(KEY);
            await _lockManager.UnLockAsync(KEY);
        }

        public async Task Lock11(string key, Func<Task> func)
        {
            var value = Guid.NewGuid().ToString("N")[..2];
            var success = await _lockManager.LockAsync(key, value, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(30));
            if (success)
            {
                try
                {
                    await func();
                }
                finally
                {
                    await _lockManager.UnLockAsync(key, value);    
                }
            }
        }
    }
}