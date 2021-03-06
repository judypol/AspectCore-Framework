﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AspectCore.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AspectCore.Extensions.DependencyInjection.Internals
{
    internal sealed class ServiceInstanceProvider : IServiceInstanceProvider, IDisposable
    {
        private readonly static ConcurrentDictionary<Type, IList<ServiceDescriptor>> ServiceDescriptorCache = new ConcurrentDictionary<Type, IList<ServiceDescriptor>>();

        private readonly static ConcurrentDictionary<IServiceProvider, ConcurrentDictionary<Type, object>> ScopedResolvedServiceCache = new ConcurrentDictionary<IServiceProvider, ConcurrentDictionary<Type, object>>();

        private readonly IServiceProvider _serviceProvider;

        private readonly object _resolveLock = new object();

        public ServiceInstanceProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public object GetInstance(Type serviceType)
        {
            if (serviceType == null)
            {
                throw new ArgumentNullException(nameof(serviceType));
            }

            if (!ServiceDescriptorCache.TryGetValue(serviceType, out IList<ServiceDescriptor> descriptorList))
            {
                return _serviceProvider.GetRequiredService(serviceType);
            }

            var descriptor = descriptorList.Last();

            if (descriptor.ImplementationInstance != null)
            {
                return descriptor.ImplementationInstance;
            }

            if (descriptor.Lifetime == ServiceLifetime.Transient)
            {
                if (descriptor.ImplementationFactory != null)
                {
                    return descriptor.ImplementationFactory(_serviceProvider);
                }

                return GetObjectFactory()(_serviceProvider, descriptor.ImplementationType);
            }

            var resolvedServices = GetOrAddResolvedCache();

            if (descriptor.ImplementationFactory != null)
            {
                return resolvedServices.GetOrAdd(serviceType, _ => descriptor.ImplementationFactory(_serviceProvider));
            }

            return resolvedServices.GetOrAdd(serviceType, _ => GetObjectFactory()(_serviceProvider, descriptor.ImplementationType));
        }

        private Func<IServiceProvider, Type, object> GetObjectFactory()
        {
            return (servicrProvider, type) => ActivatorUtilities.CreateInstance(servicrProvider, type);
        }

        private ConcurrentDictionary<Type, object> GetOrAddResolvedCache()
        {
            return ScopedResolvedServiceCache.GetOrAdd(_serviceProvider, _ => new ConcurrentDictionary<Type, object>());
        }

        internal static void MapServiceDescriptor(ServiceDescriptor descriptor)
        {
            var descriptorList = ServiceDescriptorCache.GetOrAdd(descriptor.ServiceType, _ => new List<ServiceDescriptor>());

            if (descriptor.ImplementationType != null)
            {
                descriptorList.Add(ServiceDescriptor.Describe(descriptor.ServiceType, descriptor.ImplementationType, descriptor.Lifetime));
            }

            if (descriptor.ImplementationInstance != null)
            {
                descriptorList.Add(new ServiceDescriptor(descriptor.ServiceType, descriptor.ImplementationInstance));
            }

            if (descriptor.ImplementationFactory != null)
            {
                descriptorList.Add(ServiceDescriptor.Describe(descriptor.ServiceType, descriptor.ImplementationFactory, descriptor.Lifetime));
            }
        }

        public void Dispose()
        {
            if (ScopedResolvedServiceCache.TryRemove(_serviceProvider, out ConcurrentDictionary<Type, object> resolvedServices))
            {
                resolvedServices.Clear();
            }
        }
    }
}
