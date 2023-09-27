using System.Text.Json;
using Azure.Messaging.ServiceBus;
using WorkerContagem.Data;
using WorkerContagem.Models;

namespace WorkerContagem;

public class Worker : IHostedService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly ContagemRepository _repository;
    private readonly string _topicName;
    private readonly string _subscription;
    private readonly ServiceBusClient _client;
    private readonly ServiceBusProcessor _processor;

    public Worker(ILogger<Worker> logger,
        IConfiguration configuration,
        ContagemRepository repository)
    {
        _logger = logger;
        _configuration = configuration;
        _repository = repository;

        _topicName = _configuration["AzureServiceBus:Topic"]!;
        _subscription= _configuration["AzureServiceBus:Subscription"]!;

        var clientOptions = new ServiceBusClientOptions()
        {
            TransportType = ServiceBusTransportType.AmqpWebSockets
        };
        _client = new ServiceBusClient(
            _configuration.GetConnectionString("AzureServiceBus"), clientOptions);
        _processor = _client.CreateProcessor(
            topicName: _topicName, subscriptionName: _subscription,
            new ServiceBusProcessorOptions());
        _processor.ProcessMessageAsync += MessageHandler;
        _processor.ProcessErrorAsync += ErrorHandler;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Azure Service Bus Topic = {_topicName}");
        _logger.LogInformation($"Azure Service Bus Subscription = {_subscription}");
        _logger.LogInformation(
            "Iniciando o processamento de mensagens...");
        await _processor.StartProcessingAsync();
    }

    public async Task StopAsync(CancellationToken stoppingToken)
    {
        await _processor.CloseAsync();
        await _processor.DisposeAsync();
        await _client.DisposeAsync();
        _logger.LogInformation(
            "Conexao com o Azure Service Bus fechada!");
    }

    private async Task MessageHandler(ProcessMessageEventArgs args)
    {
        var message = args.Message;

        var messageContent = message.Body.ToString();
        _logger.LogInformation(
            $"[{_topicName} | Nova mensagem] " + messageContent);

        ResultadoContador? resultado;
        try
        {
            resultado = JsonSerializer.Deserialize<ResultadoContador>(messageContent,
                new JsonSerializerOptions()
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch
        {
            _logger.LogError("Dados inv�lidos para o Resultado");
            resultado = null;
        }

        if (resultado is not null)
        {
            try
            {
                _repository.Save(resultado);
                _logger.LogInformation("Resultado registrado com sucesso!");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro durante a grava��o: {ex.Message}");
            }
        }

        await args.CompleteMessageAsync(args.Message);
    }

    private Task ErrorHandler(ProcessErrorEventArgs args)
    {
        _logger.LogError("[Falha] " +
            args.Exception.GetType().FullName + " " +
            args.Exception.Message);
        return Task.CompletedTask;
    }
}