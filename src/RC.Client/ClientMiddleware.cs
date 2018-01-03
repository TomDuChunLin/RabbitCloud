﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rabbit.Cloud.Application.Abstractions;
using Rabbit.Cloud.Client.Abstractions;
using Rabbit.Cloud.Client.Abstractions.Codec;
using Rabbit.Cloud.Client.Abstractions.Features;
using Rabbit.Cloud.Client.Codec;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Rabbit.Cloud.Client
{
    public class ClientMiddleware
    {
        private readonly RabbitRequestDelegate _next;
        private readonly ILogger<ClientMiddleware> _logger;
        private readonly RabbitClientOptions _options;

        public ClientMiddleware(RabbitRequestDelegate next, ILogger<ClientMiddleware> logger, IOptions<RabbitClientOptions> options)
        {
            _next = next;
            _logger = logger;
            _options = options.Value;
        }

        public async Task Invoke(IRabbitContext context)
        {
            var serviceRequestFeature = context.Features.Get<IServiceRequestFeature>();
            try
            {
                await RequestAsync(context, serviceRequestFeature);
            }
            catch (Exception e)
            {
                var clientException = e as RabbitClientException ?? ExceptionUtilities.ServiceRequestFailure(serviceRequestFeature.ServiceName, 400, e);
                context.Response.StatusCode = clientException.StatusCode;

                throw clientException;
            }
        }

        private async Task RequestAsync(IRabbitContext context, IServiceRequestFeature serviceRequestFeature)
        {
            serviceRequestFeature.Codec = GetCodec(context);
            var requestOptions = serviceRequestFeature.RequestOptions ?? _options.DefaultRequestOptions;

            //最少调用一次
            var retries = Math.Max(requestOptions.MaxAutoRetries, 0) + 1;
            //最少使用一个服务
            var retriesNextServer = Math.Max(requestOptions.MaxAutoRetriesNextServer, 0) + 1;

            IList<Exception> exceptions = null;
            for (var i = 0; i < retriesNextServer; i++)
            {
                var getServiceInstance = serviceRequestFeature.GetServiceInstance;
                var serviceInstance = getServiceInstance();
                serviceRequestFeature.GetServiceInstance = () => serviceInstance;

                for (var j = 0; j < retries; j++)
                {
                    try
                    {
                        await _next(context);
                        return;
                    }
                    catch (Exception e)
                    {
                        if (exceptions == null)
                            exceptions = new List<Exception>();

                        exceptions.Add(e);

                        _logger.LogError(e, "请求失败。");

                        //只有服务器错误才进行重试
                        if (!(e is RabbitClientException rabbitClientException) ||
                            rabbitClientException.StatusCode < 500)
                            throw;
                    }
                }
            }
            if (exceptions != null && exceptions.Any())
                throw new AggregateException(exceptions);
        }

        private ICodec GetCodec(IRabbitContext rabbitContext)
        {
            var requestFeature = rabbitContext.Features.Get<IServiceRequestFeature>();

            if (requestFeature.Codec != null)
                return requestFeature.Codec;

            var requestOptions = requestFeature.RequestOptions ?? _options.DefaultRequestOptions;

            var serializerName = requestOptions.SerializerName;

            var serializer = (string.IsNullOrEmpty(serializerName) ? null : _options.SerializerTable.Get(serializerName)) ?? _options.SerializerTable.Get("json");

            return requestFeature.Codec = new SerializerCodec(serializer, requestFeature.RequesType, requestFeature.ResponseType);
        }
    }
}