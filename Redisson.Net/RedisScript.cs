using System.Threading.Tasks;
using StackExchange.Redis;

namespace Redisson.Net
{
    // ReSharper disable StringLiteralTypo
    internal abstract class RedisScript
    {
        protected abstract string GetScript();

        public LoadedLuaScript LoadScript(IServer server)
        {
            var luaScript = LuaScript.Prepare(GetScript());
            return luaScript.Load(server);
        }

        public Task<LoadedLuaScript> LoadScriptAsync(IServer server)
        {
            var luaScript = LuaScript.Prepare(GetScript());
            return luaScript.LoadAsync(server);
        }

        internal class Lock : RedisScript
        {
            /// <summary>
            /// 锁逻辑 (允许重入，但不续期)
            /// </summary>
            protected override string GetScript() => @"
if (redis.call('exists', @Key) == 0) then 
    redis.call('hincrby', @Key, @Value, 1);
    redis.call('pexpire', @Key, @Expire); 
    return nil;
end; 

if (redis.call('hexists', @Key, @Value) == 1) then 
    redis.call('hincrby', @Key, @Value, 1);
    return nil; 
end; 

return redis.call('pttl', @Key);";

            /// <summary>
            /// 键
            /// </summary>
            public string Key { get; set; }

            /// <summary>
            /// 值
            /// </summary>
            public string Value { get; set; }

            /// <summary>
            /// 过期时间 (毫秒)
            /// </summary>
            public int Expire { get; set; }
        }

        internal class UnLock : RedisScript
        {
            protected override string GetScript() => @"
if (redis.call('hexists', @Key, @Value) == 0) then 
    return nil;
end;
 
local counter = redis.call('hincrby', @Key, @Value, -1); 
if (counter > 0) then 
    return 0; 
else 
    redis.call('del', @Key); 
    redis.call('publish', @Key, ''); 
    return 1;
end;";

            /// <summary>
            /// 键
            /// </summary>
            public string Key { get; set; }

            /// <summary>
            /// 值
            /// </summary>
            public string Value { get; set; }
        }
    }
}