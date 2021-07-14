using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Redisson.Net.Test
{
    public class LockSemaphoreManagerTest
    {
        private readonly ITestOutputHelper _console;

        public LockSemaphoreManagerTest(ITestOutputHelper console)
        {
            _console = console;
        }

        [Fact]
        public void Release()
        {
            var semaphoreSlim = new SemaphoreSlim(0);
            Task.Run(async () =>
            {
                await Task.Delay(1000);
                semaphoreSlim.Release(int.MaxValue);
                semaphoreSlim.Dispose();
            });
            _console.WriteLine("等待中：" + DateTime.Now.ToString("mm:ss"));
            var timeout = !semaphoreSlim.Wait(TimeSpan.FromSeconds(2));
            if (timeout)
            {
                _console.WriteLine("等待超时");
                return;
            }
            _console.WriteLine("执行完成：" + DateTime.Now.ToString("mm:ss"));
        }

        [Fact]
        public void Test()
        {
            LockSemaphoreManager.WaitAsync("test", TimeSpan.FromMilliseconds(1));
            LockSemaphoreManager.WaitAsync("test", TimeSpan.FromMilliseconds(1));
            LockSemaphoreManager.WaitAsync("test", TimeSpan.FromMilliseconds(1));
            LockSemaphoreManager.WaitAsync("test", TimeSpan.FromMilliseconds(1));
            LockSemaphoreManager.RecycleCache(TimeSpan.FromDays(1));
            LockSemaphoreManager.WaitAsync("test", TimeSpan.FromMilliseconds(1));
            LockSemaphoreManager.RecycleCache(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task Benchmark()
        {
            for (var i = 0; i < 10; i++)
            {
                var sw = Stopwatch.StartNew();
                var list = Enumerable.Range(1, 10000)
                    .Select(value => Task.Run(async () =>
                        await LockSemaphoreManager.WaitAsync(value.ToString(), TimeSpan.FromSeconds(1))
                    ))
                    .ToList();
                await Task.WhenAll(list);
                _console.WriteLine($"耗时：{sw.ElapsedMilliseconds}ms");
            }
            await LockSemaphoreManager.RecycleCache(TimeSpan.FromSeconds(1));
        }
    }
}