﻿using Buildersoft.Andy.X.Core.Abstractions.Factories.Producers;
using Buildersoft.Andy.X.Core.Abstractions.Factories.Tenants;
using Buildersoft.Andy.X.Core.Abstractions.Hubs.Producers;
using Buildersoft.Andy.X.Core.Abstractions.Repositories.Memory;
using Buildersoft.Andy.X.Core.Abstractions.Repositories.Producers;
using Buildersoft.Andy.X.Core.Abstractions.Services.Storages;
using Buildersoft.Andy.X.Model.Producers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Buildersoft.Andy.X.Router.Hubs.Producers
{
    public class ProducerHub : Hub<IProducerHub>
    {
        private readonly ILogger<ProducerHub> logger;
        private readonly IProducerHubRepository producerHubRepository;
        private readonly ITenantRepository tenantRepository;
        private readonly ITenantFactory tenantFactory;
        private readonly IProducerFactory producerFactory;
        private readonly IStorageHubService storageHubService;

        public ProducerHub(ILogger<ProducerHub> logger,
            IProducerHubRepository producerHubRepository,
            ITenantRepository tenantRepository,
            ITenantFactory tenantFactory,
            IProducerFactory producerFactory,
            IStorageHubService storageHubService)
        {
            this.logger = logger;
            this.producerHubRepository = producerHubRepository;
            this.tenantRepository = tenantRepository;
            this.tenantFactory = tenantFactory;
            this.producerFactory = producerFactory;
            this.storageHubService = storageHubService;
        }

        public override Task OnConnectedAsync()
        {
            Producer producerToRegister;
            string clientConnectionId = Context.ConnectionId;
            var headers = Context.GetHttpContext().Request.Headers;

            string tenant = headers["x-andyx-tenant"].ToString();
            string product = headers["x-andyx-product"].ToString();
            string component = headers["x-andyx-component"].ToString();
            string topic = headers["x-andyx-topic"].ToString();
            string producerName = headers["x-andyx-producer"].ToString();

            logger.LogInformation($"ANDYX#PRODUCERS|{tenant}|{product}|{component}|{topic}|{producerName}|ASKED_TO_CONNECT");

            //check if the producer is already connected

            if (tenantRepository.GetTenant(tenant) == null)
            {
                logger.LogInformation($"ANDYX#CONSUMERS|{tenant}|{product}|{component}|{topic}|{consumerName}|TENANT_DOES_NOT_EXISTS");
                return OnDisconnectedAsync(new Exception($"There is no tenant registered with this name '{tenant}'"));
            }

            if (tenantRepository.GetProduct(tenant, product) == null)
            {
                var productDetails = tenantFactory.CreateProduct(product);
                tenantRepository.AddProduct(tenant, product, productDetails);
                storageHubService.CreateProductAsync(productDetails);
            }

            if (tenantRepository.GetComponent(tenant, product, component) == null)
            {
                var componentDetails = tenantFactory.CreateComponent(component);
                tenantRepository.AddComponent(tenant, product, component, componentDetails);
                storageHubService.CreateComponentAsync(componentDetails);
            }

            if (tenantRepository.GetTopic(tenant, product, component, topic) == null)
            {
                var topicDetails = tenantFactory.CreateTopic(topic);
                tenantRepository.AddTopic(tenant, product, component, topic, topicDetails);
                storageHubService.CreateTopicAsync(topicDetails);
            }

            if (producerHubRepository.GetProducerByProducerName(tenant, product, component, topic, producerName).Equals(default(KeyValuePair<string, Producer>)) != true)
            {
                logger.LogInformation($"ANDYX#PRODUCERS|{tenant}|{product}|{component}|{topic}|{producerName}|PRODUCER_ALREADY_CONNECTED");
                return OnDisconnectedAsync(new Exception($"There is a producer with name '{producerName}' connected to this node"));
            }

            producerToRegister = producerFactory.CreateProducer(tenant, product, component, topic, producerName);
            producerHubRepository.AddProducer(clientConnectionId, producerToRegister);
            storageHubService.ConnectProducerAsync(producerToRegister);

            Clients.Caller.ProducerConnected(new Model.Producers.Events.ProducerConnectedDetails()
            {
                Id = producerToRegister.Id,
                Tenant = tenant,
                Product = product,
                Component = component,
                Topic = topic,
                ProducerName = producerName
            });

            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            string clientConnectionId = Context.ConnectionId;
            Producer producerToRemove = producerHubRepository.GetProducerById(clientConnectionId);
            storageHubService.DisconnectProducerAsync(producerToRemove);

            producerHubRepository.RemoveProducer(clientConnectionId);

            logger.LogInformation($"ANDYX#PRODUCERS|{producerToRemove.Tenant}|{producerToRemove.Product}|{producerToRemove.Component}|{producerToRemove.Topic}|{producerToRemove.ProducerName}|{producerToRemove.Id}|DISCONNECTED");

            Clients.Caller.ProducerDisconnected(new Model.Producers.Events.ProducerDisconnectedDetails()
            {
                Id = producerToRemove.Id
            });

            return base.OnDisconnectedAsync(exception);
        }
    }
}