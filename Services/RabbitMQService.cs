using Google.Cloud.SecretManager.V1;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Text;

namespace HL7ParserAPI.Services
{
    public class RabbitMQService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<RabbitMQService> _logger;
        private readonly string _projectId = "spartan-acrobat-452620-t6";
        private const string SecretName = "rabbitmq-config"; // name of your secret

        private class RabbitMQCredentials
        {
            public string Host { get; set; }
            public int Port { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
            public string QueueName { get; set; }
        }

        private RabbitMQCredentials _credentials;

        public RabbitMQService(IConfiguration configuration, ILogger<RabbitMQService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            LoadSecrets();
        }

        private void LoadSecrets()
        {
            try
            {
                SecretManagerServiceClient client = SecretManagerServiceClient.Create();
                var secretVersionName = new SecretVersionName(_projectId, SecretName, "latest");

                var result = client.AccessSecretVersion(secretVersionName);
                string payload = result.Payload.Data.ToStringUtf8();

                _credentials = JsonConvert.DeserializeObject<RabbitMQCredentials>(payload);
                _logger.LogInformation("RabbitMQ credentials loaded from Secret Manager.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load RabbitMQ credentials from Secret Manager.");
                throw;
            }
        }

        public void PublishMessage(Guid recordId, string message)
        {
            try
            {
                var factory = new ConnectionFactory()
                {
                    HostName = _credentials.Host,
                    Port = _credentials.Port,
                    UserName = _credentials.Username,
                    Password = _credentials.Password
                };

                using var connection = factory.CreateConnection();
                using var channel = connection.CreateModel();

                channel.QueueDeclare(queue: _credentials.QueueName,
                                     durable: false,
                                     exclusive: false,
                                     autoDelete: false,
                                     arguments: null);

                var body = Encoding.UTF8.GetBytes(message);

                channel.BasicPublish(exchange: "",
                                     routingKey: _credentials.QueueName,
                                     basicProperties: null,
                                     body: body);

                _logger.LogInformation($"Message published to RabbitMQ. Record ID: {recordId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to publish message to RabbitMQ for Record ID: {recordId}");
            }
        }
    }
}