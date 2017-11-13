﻿using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Rabbit.Cloud.Grpc.Client.Extensions
{
    public static class CallInvokerExtensions
    {
        #region Field

        private static readonly ParameterExpression CallInvokerParameterExpression;
        private static readonly ParameterExpression HostParameterExpression;
        private static readonly ParameterExpression CallOptionsParameterExpression;

        #endregion Field

        #region Constructor

        static CallInvokerExtensions()
        {
            CallInvokerParameterExpression = Expression.Parameter(typeof(CallInvoker), "invoker");
            HostParameterExpression = Expression.Parameter(typeof(string), "host");
            CallOptionsParameterExpression = Expression.Parameter(typeof(CallOptions), "callOptions");
        }

        #endregion Constructor

        public static object BlockingUnaryCall(this CallInvoker callInvoker, IMethod method, string host, CallOptions callOptions, object request)
        {
            return callInvoker.Call(nameof(CallInvoker.BlockingUnaryCall), method, host, callOptions, request);
        }

        public static object Call(this CallInvoker callInvoker, IMethod method, string host, CallOptions callOptions, object request)
        {
            return callInvoker.Call(GetCallMethodName(method), method, host, callOptions, request);
        }

        public static object Call(this CallInvoker callInvoker, string callMethodName, IMethod method, string host, CallOptions callOptions, object request)
        {
            var invoker = Cache.GetInvoker(method, callMethodName);
            return invoker.DynamicInvoke(callInvoker, method, host, callOptions, request);
        }

        #region Private Method

        private static string GetCallMethodName(IMethod method)
        {
            switch (method.Type)
            {
                case MethodType.Unary:
                    return nameof(CallInvoker.AsyncUnaryCall);

                case MethodType.ClientStreaming:
                    return nameof(CallInvoker.AsyncClientStreamingCall);

                case MethodType.ServerStreaming:
                    return nameof(CallInvoker.AsyncServerStreamingCall);

                case MethodType.DuplexStreaming:
                    return nameof(CallInvoker.AsyncDuplexStreamingCall);

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        #endregion Private Method

        #region Help Type

        private static class Cache
        {
            private static readonly IDictionary<object, object> Caches = new Dictionary<object, object>();

            private static T GetCache<T>(object key, Func<T> factory)
            {
                if (Caches.TryGetValue(key, out var cache))
                {
                    return (T)cache;
                }
                return (T)(Caches[key] = factory());
            }

            public static Delegate GetInvoker(IMethod method, string callMethodName)
            {
                var key = ("invoker", method.FullName, callMethodName);
                return GetCache(key, () =>
                {
                    var typeArguments = method.GetType().GenericTypeArguments;
                    var requestType = typeArguments[0];
                    var responseType = typeArguments[1];
                    var requestParameterExpression = GetRequestParameter(requestType);

                    var methodParameterExpression = GetMethodParameter(requestType, responseType);

                    Expression[] parameterExpressions;
                    IEnumerable<ParameterExpression> lambdaParameterExpressions;

                    switch (callMethodName)
                    {
                        case nameof(CallInvoker.AsyncClientStreamingCall):
                        case nameof(CallInvoker.AsyncDuplexStreamingCall):
                            parameterExpressions = new Expression[]
                            {
                                methodParameterExpression,
                                HostParameterExpression,
                                CallOptionsParameterExpression
                            };
                            lambdaParameterExpressions = new[]
                            {
                                CallInvokerParameterExpression,
                                methodParameterExpression,
                                HostParameterExpression,
                                CallOptionsParameterExpression
                            };
                            break;

                        default:
                            parameterExpressions = new Expression[]
                            {
                                methodParameterExpression,
                                HostParameterExpression,
                                CallOptionsParameterExpression,
                                requestParameterExpression
                            };
                            lambdaParameterExpressions = new[]
                            {
                                CallInvokerParameterExpression,
                                methodParameterExpression,
                                HostParameterExpression,
                                CallOptionsParameterExpression,
                                requestParameterExpression
                            };
                            break;
                    }

                    var callExpression = Expression.Call(CallInvokerParameterExpression, callMethodName, typeArguments, parameterExpressions);

                    var lambda = Expression.Lambda(callExpression, lambdaParameterExpressions);
                    return lambda.Compile();
                });
            }

            private static ParameterExpression GetMethodParameter(Type requestType, Type responseType)
            {
                var key = ("methodParameter", requestType, responseType);
                return GetCache(key, () => Expression.Parameter(typeof(Method<,>).MakeGenericType(requestType, responseType)));
            }

            private static ParameterExpression GetRequestParameter(Type requestType)
            {
                var key = ("requestParameter", requestType);
                return GetCache(key, () => Expression.Parameter(requestType));
            }
        }

        #endregion Help Type
    }
}