﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;
using StackExchange.Redis;

namespace Harbour.RedisTempData
{
    /// <summary>
    /// An <see cref="ITempDataProvider"/> for Redis.
    /// </summary>
    public class RedisTempDataProvider : ITempDataProvider
    {
        private readonly RedisTempDataProviderOptions options;
        private readonly IDatabase redis;

        public RedisTempDataProvider(IDatabase database)
            : this(new RedisTempDataProviderOptions(), database)
        {

        }

        public RedisTempDataProvider(RedisTempDataProviderOptions options, IDatabase redis)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (redis == null) throw new ArgumentNullException(nameof(redis));

            // Copy so that references can't be modified outside of this class.
            this.options = new RedisTempDataProviderOptions(options);
            this.redis = redis;
        }

        private string GetKey(ControllerContext controllerContext)
        {
            var user = options.UserProvider.GetUser(controllerContext);
            return options.KeyPrefix + options.KeySeparator + user;
        }

        public IDictionary<string, object> LoadTempData(ControllerContext controllerContext)
        {
            var hashKey = GetKey(controllerContext);
            var result = new Dictionary<string, object>();

            var transaction = redis.CreateTransaction();
            var getAllTask = transaction.HashGetAllAsync(hashKey);

            // Remove *after* reading the items since we're done with them.
            transaction.KeyDeleteAsync(hashKey, CommandFlags.FireAndForget);

            transaction.Execute();
            
            foreach (var kvp in transaction.Wait(getAllTask))
            {
                result[kvp.Name] = Deserialize(kvp.Value);
            }

            return result;
        }

        public void SaveTempData(ControllerContext controllerContext, IDictionary<string, object> values)
        {
            var hashKey = GetKey(controllerContext);

            var transaction = redis.CreateTransaction();

            // Clear the hash since we don't want old items.
            transaction.KeyDeleteAsync(hashKey, CommandFlags.FireAndForget);

            if (values != null && values.Count > 0)
            {
                foreach (var kvp in values)
                {
                    var serialized = Serialize(kvp.Value);
                    transaction.HashSetAsync(hashKey, kvp.Key, serialized, flags: CommandFlags.FireAndForget);
                }
            }

            transaction.Execute(CommandFlags.FireAndForget);
        }

        private object Deserialize(RedisValue value)
        {
            if (value.IsNull) return null;

            return options.Serializer.Deserialize(value);
        }

        private RedisValue Serialize(object value)
        {
            return options.Serializer.Serialize(value);
        }
    }
}
