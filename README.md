# XLocker
[English](README_EN.md)

参考 [RedissonLock](https://github.com/redisson/redisson/blob/master/redisson/src/main/java/org/redisson/RedissonLock.java) ，通过 dotnet 实现可等待的分布式锁实现

### 如何使用

```c#
// 暂不提供同步方法，可自行实现，此处暂时通过 Task.Result 来获取，演示使用
private static readonly LockManager _lockManager = LockManager.GetLockManagerAsync(StackExchange.Redis.ConnectionMultiplexer.Connect("redis server:port"), "LOCK:").ConfigureAwait(false).GetAwaiter().GetResult();

/// <summary>
/// 为执行逻辑加锁 
/// </summary>
public async Task Lock(string key, Func<Task> func)
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
```

### 备注

本项目目前仅做个人的学习，闲余时的尝试，所写代码仅供参考，也可以随意复制、使用

暂时只写了两个工具类 `LockManager` `LockSemaphoreManager` 并依赖 [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) 