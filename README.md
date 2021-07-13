# Redisson.Net 
[English](README_EN.md)

参考 [redisson](https://github.com/redisson/redisson) ，通过 .NET5 实现最基本的分布式锁工具库

### 如何使用

```c#
private static readonly LockManager LockManager = new(StackExchange.Redis.ConnectionMultiplexer.Connect("redis server:port"), "PREFIX", new LockSemaphoreManager());

public async Task Lock(string key, Func<Task> func)
{
    var value = Guid.NewGuid().ToString("N")[..2];
    var success = await LockManager.Lock(key, value, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(30));
    if (success)
    {
        try
        {
            await func();
        }
        finally
        {
            await LockManager.UnLock(key, value);    
        }
    }
}
```

### 开发计划

* [ ] 通过 Redis 脚本执行逻辑，减少请求次数
* [ ] 使用 Netty 实现 Redis Client，测试性能是否有所改善
* [ ] 参考 Redisson 提供的 API，进行功能移植
* [ ] 完善使用文档  (主要是中文)

### 备注

本项目目前仅做个人的学习，闲余时的尝试，所写代码仅供参考，也可以随意使用

暂时只写了两个工具类 `LockManager` `LockSemaphoreManager` 并依赖 [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) 