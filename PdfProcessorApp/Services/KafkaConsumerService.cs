using Confluent.Kafka;
using PdfProcessorApp.Models;
using System.Text.Json;
using System.Threading;
using System.IO;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Document = DocumentFormat.OpenXml.Wordprocessing.Document;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;
using System.Text;

namespace PdfProcessorApp.Services
{
    public class KafkaConsumerService : BackgroundService
    {
        private readonly ConsumerConfig _config;
        private readonly string _topic;
        private readonly ILogger<KafkaConsumerService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly string _uploadPath;
        private readonly string _outputPath;
        private readonly ProcessingStatusService _statusService;

        public KafkaConsumerService(IConfiguration configuration, ILogger<KafkaConsumerService> logger, 
            IServiceProvider serviceProvider, IWebHostEnvironment env, ProcessingStatusService statusService)
        {
            _config = new ConsumerConfig
            {
                BootstrapServers = configuration["Kafka:BootstrapServers"],
                GroupId = configuration["Kafka:GroupId"] ?? "pdf-processor-group",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                SessionTimeoutMs = 30000, 
                SocketTimeoutMs = 30000,
                ConnectionsMaxIdleMs = 30000,
                MaxPollIntervalMs = 600000, 
                FetchMaxBytes = 52428800, 
                MaxPartitionFetchBytes = 10485760, 
                EnableAutoCommit = false, 
                EnablePartitionEof = true,
                IsolationLevel = IsolationLevel.ReadCommitted
            };
            _topic = configuration["Kafka:Topic:PdfProcessing"] ?? "pdf-processing-topic";
            _logger = logger;
            _serviceProvider = serviceProvider;
            _uploadPath = Path.Combine(env.WebRootPath, "uploads");
            _outputPath = Path.Combine(env.WebRootPath, "output");
            _statusService = statusService;
            
            if (!Directory.Exists(_uploadPath))
                Directory.CreateDirectory(_uploadPath);
            if (!Directory.Exists(_outputPath))
                Directory.CreateDirectory(_outputPath);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            using (var consumer = new ConsumerBuilder<Ignore, string>(_config).Build())
            {
                consumer.Subscribe(_topic);
                _logger.LogInformation($"Kafka consumer başlatıldı. Topic: {_topic}");

                try
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        try
                        {
                            var consumeResult = consumer.Consume(stoppingToken);
                            if (consumeResult != null && consumeResult.Message != null && !string.IsNullOrEmpty(consumeResult.Message.Value))
                            {
                                try
                                {
                                    var message = JsonSerializer.Deserialize<PdfProcessingMessage>(consumeResult.Message.Value);
                                    
                                    if (message != null)
                                    {
                                        _logger.LogInformation($"Yeni PDF işleme mesajı alındı: {message.OperationType} - {message.FileName}");
                                        
                                        await ProcessMessageAsync(message);
                                        
                                        consumer.Commit(consumeResult);
                                    }
                                    else
                                    {
                                        _logger.LogWarning("Alınan Kafka mesajı deserialize edilemedi.");
                                    }
                                }
                                catch (JsonException ex)
                                {
                                    _logger.LogError($"Kafka mesajı deserialize edilirken hata oluştu: {ex.Message}");
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Alınan Kafka mesajı null veya boş.");
                            }
                        }
                        catch (ConsumeException ex)
                        {
                            _logger.LogError($"Kafka mesajı tüketilirken hata oluştu: {ex.Message}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Kafka consumer durduruldu.");
                }
                finally
                {
                    consumer.Close();
                }
            }
        }

        private async Task ProcessMessageAsync(PdfProcessingMessage message)
        {
            try
            {
                message.Status = "Processing";
                message.Progress = 10;
                _statusService.AddOrUpdateStatus(message);
                
                switch (message.OperationType)
                {
                    case "ExtractText":
                        await ExtractTextAsync(message);
                        break;
                    case "ConvertPdf":
                        await ConvertPdfAsync(message);
                        break;
                    case "MergePdf":
                        await MergePdfAsync(message);
                        break;
                    default:
                        _logger.LogWarning($"Bilinmeyen operasyon türü: {message.OperationType}");
                        message.Status = "Failed";
                        message.Progress = 0;
                        message.CompletionTime = DateTime.UtcNow;
                        _statusService.AddOrUpdateStatus(message);
                        break;
                }
                
                if (message.Status != "Failed" && message.Progress < 100)
                {
                    message.Status = "Completed";
                    message.Progress = 100;
                    message.CompletionTime = DateTime.UtcNow;
                    _statusService.AddOrUpdateStatus(message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Mesaj işlenirken hata oluştu: {ex.Message}");
                
                message.Status = "Failed";
                message.Progress = 0;
                message.CompletionTime = DateTime.UtcNow;
                _statusService.AddOrUpdateStatus(message);
            }
        }

        private async Task ExtractTextAsync(PdfProcessingMessage message)
        {
            var filePath = message.SourceFilePath;
            var extractedText = new StringBuilder();

            try
            {
                _logger.LogInformation($"PDF dosyası açılıyor: {filePath}");
                message.Progress = 20;
                _statusService.AddOrUpdateStatus(message);
                
                using (UglyToad.PdfPig.PdfDocument document = UglyToad.PdfPig.PdfDocument.Open(filePath))
                {
                    var pageCount = document.GetPages().Count();
                    _logger.LogInformation($"PDF sayfa sayısı: {pageCount}");
                    
                    int pageIndex = 0;
                    foreach (var page in document.GetPages())
                    {
                        pageIndex++;
                        var progressPercentage = 20 + (60 * pageIndex / pageCount); 
                        message.Progress = progressPercentage;
                        _statusService.AddOrUpdateStatus(message);
                        
                        var text = string.Join(" ", page.GetWords().Select(w => w.Text));
                        extractedText.AppendLine(text);
                        
                        _logger.LogInformation($"Sayfa {pageIndex}/{pageCount} işlendi. İlerleme: %{progressPercentage}");
                    }
                }

                message.Progress = 90;
                _statusService.AddOrUpdateStatus(message);
                _logger.LogInformation($"Metin dosyaya yazılıyor...");
                
                var outputFilePath = Path.Combine(_outputPath, $"extracted_{Path.GetFileNameWithoutExtension(message.FileName)}.txt");
                await File.WriteAllTextAsync(outputFilePath, extractedText.ToString(), Encoding.UTF8);
                
                message.Progress = 100;
                message.Status = "Completed";
                message.CompletionTime = DateTime.UtcNow;
                _statusService.AddOrUpdateStatus(message);
                
                _logger.LogInformation($"PDF metin çıkarma işlemi tamamlandı: {outputFilePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Metin çıkarma sırasında hata oluştu: {ex.Message}");
                
                message.Status = "Failed";
                message.Progress = 0;
                message.CompletionTime = DateTime.UtcNow;
                _statusService.AddOrUpdateStatus(message);
                
                throw;
            }
        }

        private async Task ConvertPdfAsync(PdfProcessingMessage message)
        {
            var filePath = message.SourceFilePath;
            var targetFormat = message.TargetFormat;
            var outputFileName = $"converted_{Path.GetFileNameWithoutExtension(message.FileName)}.{targetFormat.ToLower()}";
            var outputFilePath = Path.Combine(_outputPath, outputFileName);

            try
            {
                _logger.LogInformation($"PDF dosyası açılıyor: {filePath}");
                message.Progress = 20;
                _statusService.AddOrUpdateStatus(message);
                
                var extractedText = new StringBuilder();
                using (UglyToad.PdfPig.PdfDocument document = UglyToad.PdfPig.PdfDocument.Open(filePath))
                {
                    var pageCount = document.GetPages().Count();
                    _logger.LogInformation($"PDF sayfa sayısı: {pageCount}");
                    
                    int pageIndex = 0;
                    foreach (var page in document.GetPages())
                    {
                        pageIndex++;
                        var progressPercentage = 20 + (40 * pageIndex / pageCount); 
                        message.Progress = progressPercentage;
                        _statusService.AddOrUpdateStatus(message);
                        
                        var text = string.Join(" ", page.GetWords().Select(w => w.Text));
                        extractedText.AppendLine(text);
                        
                        _logger.LogInformation($"Sayfa {pageIndex}/{pageCount} işlendi. İlerleme: %{progressPercentage}");
                    }
                }

                message.Progress = 70;
                _statusService.AddOrUpdateStatus(message);
                _logger.LogInformation($"PDF {targetFormat} formatına dönüştürülüyor...");

                if (targetFormat.ToLower() == "txt")
                {
                    try
                    {
                        var outputDir = Path.GetDirectoryName(outputFilePath);
                        if (!Directory.Exists(outputDir))
                            Directory.CreateDirectory(outputDir);
                            
                        await File.WriteAllTextAsync(outputFilePath, extractedText.ToString(), Encoding.UTF8);
                        
                        if (File.Exists(outputFilePath))
                        {
                            _logger.LogInformation($"PDF başarıyla TXT dosyasına dönüştürüldü: {outputFilePath}");
                        }
                        else
                        {
                            _logger.LogError($"TXT dosyası oluşturuldu ancak dosya bulunamıyor: {outputFilePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"TXT dosyası oluşturulurken hata: {ex.Message}");
                        throw;
                    }
                }
                else if (targetFormat.ToLower() == "docx")
                {
                    try
                    {
                        var outputDir = Path.GetDirectoryName(outputFilePath);
                        if (!Directory.Exists(outputDir))
                            Directory.CreateDirectory(outputDir);
                        
                        message.Progress = 80;
                        _statusService.AddOrUpdateStatus(message);
                        
                        using (var wordDoc = WordprocessingDocument.Create(outputFilePath, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
                        {
                            var mainPart = wordDoc.AddMainDocumentPart();
                            mainPart.Document = new Document();
                            var body = mainPart.Document.AppendChild(new Body());
                            
                            var lines = extractedText.ToString().Split('\n');
                            int lineIndex = 0;
                            int lineCount = lines.Length;
                            
                            foreach (var line in lines)
                            {
                                lineIndex++;
                                if (lineIndex % 100 == 0 || lineIndex == lineCount)
                                {
                                    var docxProgress = 80 + (15 * lineIndex / lineCount); 
                                    message.Progress = docxProgress;
                                    _statusService.AddOrUpdateStatus(message);
                                    _logger.LogInformation($"DOCX oluşturma: {lineIndex}/{lineCount} satır işlendi. İlerleme: %{docxProgress}");
                                }
                                
                                body.AppendChild(new Paragraph(new Run(new Text(line))));
                            }
                        }
                        
                        if (File.Exists(outputFilePath))
                        {
                            _logger.LogInformation($"PDF başarıyla DOCX dosyasına dönüştürüldü: {outputFilePath}");
                        }
                        else
                        {
                            _logger.LogError($"DOCX dosyası oluşturuldu ancak dosya bulunamıyor: {outputFilePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"DOCX dosyası oluşturulurken hata: {ex.Message}");
                        throw;
                    }
                }
                else
                {
                    _logger.LogWarning($"Desteklenmeyen format: {targetFormat}");
                    message.Status = "Failed";
                    message.Progress = 0;
                    message.CompletionTime = DateTime.UtcNow;
                    _statusService.AddOrUpdateStatus(message);
                    return;
                }
                
                message.Progress = 100;
                message.Status = "Completed";
                message.CompletionTime = DateTime.UtcNow;
                _statusService.AddOrUpdateStatus(message);
            }
            catch (Exception ex)
            {
                _logger.LogError($"{targetFormat.ToUpper()}'ye dönüştürme sırasında bir hata oluştu: {ex.Message}");
                
                message.Status = "Failed";
                message.Progress = 0;
                message.CompletionTime = DateTime.UtcNow;
                _statusService.AddOrUpdateStatus(message);
                
                throw;
            }
        }
        
        private async Task MergePdfAsync(PdfProcessingMessage message)
        {
            try
            {
                var firstPdfPath = message.SourceFilePath;
                var secondPdfPath = message.AdditionalData; 
                var outputFilePath = message.OutputFilePath;
                
                _logger.LogInformation($"PDF birleştirme işlemi başlatılıyor: {firstPdfPath} ve {secondPdfPath}");

                message.Status = "Processing";
                message.Progress = 10;
                _statusService.AddOrUpdateStatus(message);
                
                await Task.Delay(500);

                if (!File.Exists(firstPdfPath))
                {
                    _logger.LogError($"Birinci PDF dosyası bulunamadı: {firstPdfPath}");
                    message.Status = "Failed";
                    message.Progress = 0;
                    message.CompletionTime = DateTime.UtcNow;
                    _statusService.AddOrUpdateStatus(message);
                    return;
                }
                
                if (!File.Exists(secondPdfPath))
                {
                    _logger.LogError($"İkinci PDF dosyası bulunamadı: {secondPdfPath}");
                    message.Status = "Failed";
                    message.Progress = 0;
                    message.CompletionTime = DateTime.UtcNow;
                    _statusService.AddOrUpdateStatus(message);
                    return;
                }
                
                message.Progress = 25;
                _statusService.AddOrUpdateStatus(message);
                await Task.Delay(200); 
                
                var outputDir = Path.GetDirectoryName(outputFilePath);
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                
                using (var outputDocument = new PdfSharpCore.Pdf.PdfDocument())
                {
                    _logger.LogInformation($"Birinci PDF açılıyor: {firstPdfPath}");
                    message.Progress = 35;
                    _statusService.AddOrUpdateStatus(message);
                    await Task.Delay(200); 
                    
                    using (var inputDocument1 = PdfSharpCore.Pdf.IO.PdfReader.Open(firstPdfPath, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import))
                    {
                        _logger.LogInformation($"Birinci PDF sayfa sayısı: {inputDocument1.PageCount}");
                        message.Progress = 45;
                        _statusService.AddOrUpdateStatus(message);
                        await Task.Delay(200); 
                        
                        for (int i = 0; i < inputDocument1.PageCount; i++)
                        {
                            outputDocument.AddPage(inputDocument1.Pages[i]);
                            
                            if (inputDocument1.PageCount > 10 && i % 5 == 0)
                            {
                                var progress = 45 + (15 * i / inputDocument1.PageCount);
                                message.Progress = progress;
                                _statusService.AddOrUpdateStatus(message);
                                _logger.LogInformation($"Birinci PDF'den sayfa {i+1}/{inputDocument1.PageCount} kopyalandı. İlerleme: %{progress}");
                            }
                        }
                    }
                    
                    _logger.LogInformation($"İkinci PDF açılıyor: {secondPdfPath}");
                    message.Progress = 60;
                    _statusService.AddOrUpdateStatus(message);
                    await Task.Delay(200); 
                    
                    using (var inputDocument2 = PdfSharpCore.Pdf.IO.PdfReader.Open(secondPdfPath, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import))
                    {
                        _logger.LogInformation($"İkinci PDF sayfa sayısı: {inputDocument2.PageCount}");
                        message.Progress = 70;
                        _statusService.AddOrUpdateStatus(message);
                        await Task.Delay(200); 
                        
                        for (int i = 0; i < inputDocument2.PageCount; i++)
                        {
                            outputDocument.AddPage(inputDocument2.Pages[i]);
                            
                            if (inputDocument2.PageCount > 10 && i % 5 == 0)
                            {
                                var progress = 70 + (15 * i / inputDocument2.PageCount);
                                message.Progress = progress;
                                _statusService.AddOrUpdateStatus(message);
                                _logger.LogInformation($"İkinci PDF'den sayfa {i+1}/{inputDocument2.PageCount} kopyalandı. İlerleme: %{progress}");
                            }
                        }
                    }
                    
                    _logger.LogInformation($"Birleştirilmiş PDF kaydediliyor: {outputFilePath}");
                    message.Progress = 90;
                    _statusService.AddOrUpdateStatus(message);
                    await Task.Delay(200); 
                    
                    outputDocument.Save(outputFilePath);
                    message.Progress = 95;
                    _statusService.AddOrUpdateStatus(message);
                    await Task.Delay(200); 
                    
                    _logger.LogInformation($"Birleştirilmiş PDF kaydedildi. Toplam sayfa sayısı: {outputDocument.PageCount}");
                }
                
                if (File.Exists(secondPdfPath) && secondPdfPath.Contains("temp"))
                {
                    _logger.LogInformation($"Geçici dosya siliniyor: {secondPdfPath}");
                    File.Delete(secondPdfPath);
                }
                
                await Task.Delay(500);
                
                message.Progress = 100;
                message.Status = "Completed";
                message.CompletionTime = DateTime.UtcNow;
                _statusService.AddOrUpdateStatus(message);
                
                _logger.LogInformation($"PDF birleştirme işlemi başarıyla tamamlandı: {outputFilePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"PDF birleştirme sırasında bir hata oluştu: {ex.Message}");
                _logger.LogError($"Hata ayrıntıları: {ex.StackTrace}");
                
                message.Status = "Failed";
                message.Progress = 0;
                message.CompletionTime = DateTime.UtcNow;
                _statusService.AddOrUpdateStatus(message);
            }
        }
    }
}
