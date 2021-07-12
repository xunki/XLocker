using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace Redisson.Net.Test
{
    public class LockManagerTest
    {
        private readonly ITestOutputHelper _console;
        private static readonly ConnectionMultiplexer Redis = ConnectionMultiplexer.Connect("192.168.2.172:56379");
        private static readonly LockManager LockManager = new(Redis, "", new LockSemaphoreManager());

        public LockManagerTest(ITestOutputHelper console)
        {
            _console = console;
            LockManager.SubscribeAsync().Wait();
        }

        [Fact]
        public async Task TestRedisSub()
        {
            await Redis.GetSubscriber().SubscribeAsync("test:*",
                (key, value) => _console.WriteLine($"Key: {key} Value:{value}"));

            await Redis.GetDatabase().PublishAsync("test:1", "1");
            await Redis.GetDatabase().PublishAsync("test:2", "2");
            await Redis.GetDatabase().PublishAsync("test:3", "3");
            await Task.Delay(100);
        }

        private async Task Lock(string key, Func<Task> func)
        {
            var value = Guid.NewGuid().ToString("N")[..2];
            _console.WriteLine(value + " 等待加锁...");
            var success = await LockManager.Lock(key, value,
                TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(30));
            _console.WriteLine("{0} {1}", value, success ? "加锁成功" : "加锁失败");

            if (success)
            {
                await func();
                success = await LockManager.UnLock(key, value);
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
                    for (var j = 0; j < 20; j++)
                    {
                        var index = i;
                        tasks.Add(Task.Run(async () =>
                        {
                            var key = KEY_PREFIX + index;
                            var value = Guid.NewGuid().ToString("N")[..2];
                            var success = await LockManager.Lock(key, value,
                                TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(30));

                            if (success)
                                await LockManager.UnLock(key, value);
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
            const string SCRIPT = @"";

            
            var loadedLuaScript = await LuaScript.Prepare(SCRIPT).LoadAsync(Redis.GetServer(Redis.GetEndPoints()[0]));
            await loadedLuaScript.EvaluateAsync(Redis.GetDatabase(), new {key = "key", value = 123});
        }
    }
}