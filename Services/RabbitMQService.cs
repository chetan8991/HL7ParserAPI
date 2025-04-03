using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Text;

namespace HL7ParserAPI.Services
{
    public class RabbitMQService
    {
        private readonly string _hostname;
        private readonly int _port;
        private readonly string _username;
        private readonly string _password;
        private readonly string _queueName;

        public RabbitMQService(IConfiguration configuration)
        {
            var rabbitConfig = configuration.GetSection("RabbitMQ");
            _hostname = rabbitConfig["Host"];
            _port = int.Parse(rabbitConfig["Port"]);
            _username = rabbitConfig["Username"];
            _password = rabbitConfig["Password"];
            _queueName = rabbitConfig["QueueName"];
        }

        public void PublishMessage(Guid id, string jsonMessage)
        {
            var factory = new ConnectionFactory()
            {
                HostName = _hostname,
                Port = _port,
                UserName = _username,
                Password = _password
            };

            // Establish connection
            using var connection = factory.CreateConnection(); // <- This should work now
            using var channel = connection.CreateModel();

            channel.QueueDeclare(queue: _queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
            //var consumer = new EventingBasicConsumer(channel);

            //consumer.Received += (model, ea) =>
            //{
            //    var body = ea.Body.ToArray();
            //    var message = Encoding.UTF8.GetString(body);
            //    Console.WriteLine($"Received: {message}");
            //};

            //channel.BasicConsume(queue: "test_queue", autoAck: true, consumer: consumer);

            //Console.WriteLine("Waiting for messages...");
            //Console.ReadLine();


            var body = Encoding.UTF8.GetBytes(jsonMessage);

            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.Headers = new Dictionary<string, object>
            {
                { "UniqueId", id.ToString() }
            };

            channel.BasicPublish(exchange: "", routingKey: _queueName, basicProperties: properties, body: body);
        }
    }
}