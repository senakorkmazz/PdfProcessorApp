using Confluent.Kafka;
using PdfProcessorApp.Models;
using System.Text.Json;

namespace PdfProcessorApp.Services
{
    public class KafkaProducerService
    {
        private readonly ProducerConfig _config;
        private readonly string _topic;
        private readonly ILogger<KafkaProducerService> _logger;
        private readonly ProcessingStatusService _statusService;

        public KafkaProducerService(IConfiguration configuration, ILogger<KafkaProducerService> logger, ProcessingStatusService statusService)
        {
            _config = new ProducerConfig
            {
                BootstrapServers = configuration["Kafka:BootstrapServers"],
                MessageTimeoutMs = 30000, 
                SocketTimeoutMs = 30000,
                ConnectionsMaxIdleMs = 30000,
                BatchSize = 16384, 
                LingerMs = 5, 
                CompressionType = CompressionType.Snappy, 
                EnableIdempotence = true, 
                Acks = Acks.All 
            };
            _topic = configuration["Kafka:Topic:PdfProcessing"] ?? "pdf-processing-topic";
            _logger = logger;
            _statusService = statusService;
        }

        public async Task ProduceMessageAsync(PdfProcessingMessage message)
        {
            try
            {
                message.Status = "Waiting";
                message.Progress = 0;
                
                _statusService.AddOrUpdateStatus(message);
                
                using (var producer = new ProducerBuilder<Null, string>(_config).Build())
                {
                    var messageJson = JsonSerializer.Serialize(message);
                    var result = await producer.ProduceAsync(_topic, new Message<Null, string> { Value = messageJson });
                    _logger.LogInformation($"PDF işleme mesajı Kafka'ya gönderildi: {result.Topic} - {result.Offset}");
                    
                    message.Status = "Processing";
                    message.Progress = 5; 
                    _statusService.AddOrUpdateStatus(message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Kafka'ya mesaj gönderirken hata oluştu: {ex.Message}");
                
                message.Status = "Failed";
                message.Progress = 0;
                message.CompletionTime = DateTime.UtcNow;
                _statusService.AddOrUpdateStatus(message);
                
                throw;
            }
        }
    }
}
