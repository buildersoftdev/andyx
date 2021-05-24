﻿using Buildersoft.Andy.X.Core.Abstractions.Factories.Consumers;
using Buildersoft.Andy.X.Core.Abstractions.Factories.Tenants;
using Buildersoft.Andy.X.Core.Abstractions.Hubs.Consumers;
using Buildersoft.Andy.X.Core.Abstractions.Repositories.Consumers;
using Buildersoft.Andy.X.Core.Abstractions.Repositories.Memory;
using Buildersoft.Andy.X.Model.Consumers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Buildersoft.Andy.X.Router.Hubs.Consumers
{
    public class ConsumerHub : Hub<IConsumerHub>
    {
        private readonly ILogger<ConsumerHub> logger;
        private readonly IConsumerHubRepository consumerHubRepository;
        private readonly ITenantRepository tenantRepository;
        private readonly ITenantFactory tenantFactory;
        private readonly IConsumerFactory consumerFactory;

        public ConsumerHub(ILogger<ConsumerHub> logger,
            IConsumerHubRepository consumerHubRepository,
            ITenantRepository tenantRepository,
            ITenantFactory tenantFactory,
            IConsumerFactory consumerFactory)
        {
            this.logger = logger;
            this.consumerHubRepository = consumerHubRepository;
            this.tenantRepository = tenantRepository;
            this.tenantFactory = tenantFactory;
            this.consumerFactory = consumerFactory;
        }

        public override Task OnConnectedAsync()
        {
            Consumer consumerToRegister;
            string clientConnectionId = Context.ConnectionId;
            var headers = Context.GetHttpContext().Request.Headers;

            string tenant = headers["x-andyx-tenant"].ToString();
            string product = headers["x-andyx-product"].ToString();
            string component = headers["x-andyx-component"].ToString();
            string topic = headers["x-andyx-topic"].ToString();
            string consumerName = headers["x-andyx-consumer"].ToString();
            ConsumerType consumerType = (ConsumerType)Enum.Parse(typeof(ConsumerType), headers["x-andyx-consumer-type"].ToString());

            logger.LogInformation($"ANDYX#CONSUMERS|{tenant}|{product}|{component}|{topic}|{consumerName}|{consumerType}|ASKED_TO_CONNECT");

            //check if the consumer is already connected

            if (tenantRepository.GetTenant(tenant) == null)
            {
                logger.LogInformation($"ANDYX#CONSUMERS|{tenant}|{product}|{component}|{topic}|{consumerName}|TENANT_DOES_NOT_EXISTS");
                return OnDisconnectedAsync(new Exception($"There is no tenant registered with this name '{tenant}'"));
            }

            if (tenantRepository.GetProduct(tenant, product) == null)
            {
                // Create new product, store this product to ALL DATA STORAGES
                // TODO: Create a new DataStorage Service
                tenantRepository.AddProduct(tenant, product, tenantFactory.CreateProduct(product));
            }

            if (tenantRepository.GetComponent(tenant, product, component) == null)
            {
                // Create new component, store this product to ALL DATA STORAGES
                // TODO: Create a new DataStorage Service
                tenantRepository.AddComponent(tenant, product, component, tenantFactory.CreateComponent(component));
            }

            if (tenantRepository.GetTopic(tenant, product, component, topic) == null)
            {
                // Create new topic, store this product to ALL DATA STORAGES
                // TODO: Create a new DataStorage Service
                tenantRepository.AddTopic(tenant, product, component, topic, tenantFactory.CreateTopic(topic));
            }

            var consumerConencted = consumerHubRepository.GetConsumerByConsumerName(tenant, product, component, topic, consumerName);
            if (consumerConencted.Equals(default(KeyValuePair<string, Consumer>)) != true)
            {
                if (consumerType == ConsumerType.Exclusive)
                {
                    logger.LogInformation($"ANDYX#CONSUMERS|{tenant}|{product}|{component}|{topic}|{consumerName}|CONSUMER_EXCLUSIVE_ALREADY_CONNECTED");
                    return OnDisconnectedAsync(new Exception($"There is a consumer with name '{consumerName}' and with type 'EXCLUSIVE' is connected to this node"));
                }

                if (consumerConencted.Value.ConsumerType == ConsumerType.Shared && consumerType != ConsumerType.Shared)
                {
                    logger.LogInformation($"ANDYX#CONSUMERS|{tenant}|{product}|{component}|{topic}|{consumerName}|CONSUMER_SHARED_ALREADY_CONNECTED");
                    return OnDisconnectedAsync(new Exception($"There is a consumer with name '{consumerName}' and with type 'SHARED' is connected to this node, only shared consumers can connect"));
                }
            }

            consumerToRegister = consumerFactory.CreateConsumer(tenant, product, component, topic, consumerName, consumerType);
            consumerHubRepository.AddConsumer(clientConnectionId, consumerToRegister);
            // Create new consumer, store this product to ALL DATA STORAGES
            // TODO: Create a new DataStorage Service

            Clients.Caller.ConsumerConnected(new Model.Consumers.Events.ConsumerConnectedDetails()
            {
                Id = consumerToRegister.Id,
                Tenant = tenant,
                Product = product,
                Component = component,
                Topic = topic,
                ConsumerName = consumerName,
                ConsumerType = consumerType
            });

            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            string clientConnectionId = Context.ConnectionId;
            Consumer consumerToRemove = consumerHubRepository.GetConsumerById(clientConnectionId);

            consumerHubRepository.RemoveConsumer(clientConnectionId);

            logger.LogInformation($"ANDYX#CONSUMERS|{consumerToRemove.Tenant}|{consumerToRemove.Product}|{consumerToRemove.Component}|{consumerToRemove.Topic}|{consumerToRemove.ConsumerName}|{consumerToRemove.Id}|DISCONNECTED");

            Clients.Caller.ConsumerDisconnected(new Model.Consumers.Events.ConsumerDisconnectedDetails()
            {
                Id = consumerToRemove.Id
            });

            return base.OnDisconnectedAsync(exception);
        }
    }
}
