using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Document = DocumentFormat.OpenXml.Wordprocessing.Document;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;
using PdfProcessorApp.Services;
using PdfProcessorApp.Models;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace PdfProcessorApp.Controllers
{
    public class PdfController : Controller
    {
        private readonly string _uploadPath;
        private readonly string _outputPath;
        private readonly KafkaProducerService _kafkaProducer;
        private readonly ProcessingStatusService _processingStatusService;
        private readonly ILogger<PdfController> _logger;

        public PdfController(IWebHostEnvironment env, KafkaProducerService kafkaProducer, ProcessingStatusService processingStatusService, ILogger<PdfController> logger)
        {
            _uploadPath = Path.Combine(env.WebRootPath, "uploads");
            _outputPath = Path.Combine(env.WebRootPath, "output");
            _kafkaProducer = kafkaProducer;
            _processingStatusService = processingStatusService;
            _logger = logger;

            if (!Directory.Exists(_uploadPath))
                Directory.CreateDirectory(_uploadPath);
            if (!Directory.Exists(_outputPath))
                Directory.CreateDirectory(_outputPath);
        }

        public IActionResult Index(string fileName = null, string activeTool = "view", int activePageNumber = 1)
        {
            ViewBag.FileName = fileName;
            ViewBag.ActiveTool = activeTool;
            ViewBag.ActivePageNumber = activePageNumber;
            ViewBag.PageCount = GetPageCount(fileName);
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> UploadPdf(IFormFile pdfFile)
        {
            if (pdfFile == null || pdfFile.Length == 0)
            {
                TempData["Message"] = "Lütfen bir PDF dosyası seçin.";
                return RedirectToAction("Index");
            }

            var fileName = Guid.NewGuid().ToString("N") + Path.GetExtension(pdfFile.FileName);
            var filePath = Path.Combine(_uploadPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await pdfFile.CopyToAsync(stream);
            }

            return RedirectToAction("Index", new { fileName, activeTool = "view", activePageNumber = 1 });
        }

        [HttpPost]
        public async Task<IActionResult> ExtractText(string fileName)
        {
            ViewBag.FileName = fileName;
            ViewBag.ActiveTool = "extractText";
            ViewBag.ActivePageNumber = 1;
            ViewBag.PageCount = GetPageCount(fileName);

            if (string.IsNullOrEmpty(fileName))
            {
                ViewBag.ExtractedText = "PDF seçilmedi.";
                return View("Index");
            }

            var filePath = Path.Combine(_uploadPath, fileName);

            try
            {
                // Kafka mesajı oluştur
                var message = new PdfProcessingMessage
                {
                    FileName = fileName,
                    OperationType = "ExtractText",
                    SourceFilePath = filePath,
                    OutputFilePath = Path.Combine(_outputPath, $"extracted_{Path.GetFileNameWithoutExtension(fileName)}.txt")
                };

                // Mesajı Kafka'ya gönder
                await _kafkaProducer.ProduceMessageAsync(message);

                ViewBag.Message = "PDF metin çıkarma işlemi başlatıldı. İşlem tamamlandığında sonuçlar burada gösterilecek.";
                ViewBag.ProcessingId = message.RequestId;
                ViewBag.ActiveTool = "extractText";
            }
            catch (System.Exception ex)
            {
                ViewBag.ExtractedText = $"Metin çıkarma işlemi başlatılırken bir hata oluştu: {ex.Message}";
            }
            return View("Index");
        }

        [HttpPost]
        public async Task<IActionResult> ConvertPdf(string fileName, string targetFormat)
        {
            ViewBag.FileName = fileName;
            ViewBag.ActiveTool = "convertPdf";
            ViewBag.ActivePageNumber = 1;
            ViewBag.PageCount = GetPageCount(fileName);

            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(targetFormat))
            {
                ViewBag.Message = "PDF veya hedef format seçilmedi.";
                return View("Index");
            }

            var filePath = Path.Combine(_uploadPath, fileName);
            string outputFileName = $"converted_{Path.GetFileNameWithoutExtension(fileName)}.{targetFormat.ToLower()}";
            string outputFilePath = Path.Combine(_outputPath, outputFileName);

            try
            {
                // Kafka mesajı oluştur
                var message = new PdfProcessingMessage
                {
                    FileName = fileName,
                    OperationType = "ConvertPdf",
                    TargetFormat = targetFormat.ToLower(),
                    SourceFilePath = filePath,
                    OutputFilePath = outputFilePath
                };

                // Mesajı Kafka'ya gönder
                await _kafkaProducer.ProduceMessageAsync(message);

                ViewBag.Message = $"PDF {targetFormat.ToUpper()} formatına dönüştürme işlemi başlatıldı. İşlem tamamlandığında sonuçlar burada gösterilecek.";
                ViewBag.ProcessingId = message.RequestId;
                // URL'yi düzeltiyoruz - başında slash olmalı
                ViewBag.ConvertedFilePath = $"/output/{outputFileName}";
            }
            catch (System.Exception ex)
            {
                ViewBag.Message = $"{targetFormat.ToUpper()}'ye dönüştürme işlemi başlatılırken bir hata oluştu: {ex.Message}";
            }
            return View("Index");
        }

        private List<string> GetContentLinesByPosition(UglyToad.PdfPig.Content.Page page, double headerLimit = 90, double footerLimit = 90)
        {
            var lines = new List<string>();
            var words = page.GetWords().OrderByDescending(w => w.BoundingBox.BottomLeft.Y).ToList();
            var grouped = words.GroupBy(w => w.BoundingBox.BottomLeft.Y)
                .OrderByDescending(g => g.Key);

            double pageHeight = page.Height;

            foreach (var group in grouped)
            {
                double y = group.Key;
                if (y > pageHeight - headerLimit)
                    continue;
                if (y < footerLimit)
                    continue;

                var line = string.Join(" ", group.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text));
                if (!string.IsNullOrWhiteSpace(line))
                    lines.Add(line.Trim());
            }
            return lines;
        }

        private int GetPageCount(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return 1;
            var filePath = Path.Combine(_uploadPath, fileName);
            if (!System.IO.File.Exists(filePath)) return 1;
            try
            {
                using (var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(filePath, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.ReadOnly))
                {
                    return doc.PageCount;
                }
            }
            catch
            {
                return 1;
            }
        }

        [HttpPost]
        public async Task<IActionResult> MergePdf(IFormFile secondPdf, string fileName)
        {
            if (secondPdf == null || string.IsNullOrEmpty(fileName))
            {
                TempData["Error"] = "Lütfen birleştirilecek PDF dosyasını seçin.";
                return RedirectToAction("Index");
            }

            try
            {
                // İlk PDF'in yolunu al
                var firstPdfPath = Path.Combine(_uploadPath, fileName);
                if (!System.IO.File.Exists(firstPdfPath))
                {
                    TempData["Error"] = "Birinci PDF dosyası bulunamadı.";
                    return RedirectToAction("Index");
                }

                // İkinci PDF'i geçici olarak kaydet
                var secondFileName = Path.GetFileName(secondPdf.FileName);
                var secondFilePath = Path.Combine(_uploadPath, $"temp_{secondFileName}");

                using (var stream = new FileStream(secondFilePath, FileMode.Create))
                {
                    await secondPdf.CopyToAsync(stream);
                }

                // Benzersiz bir RequestId oluştur
                var requestId = Guid.NewGuid().ToString();
                
                // Kafka mesajı oluştur
                var outputFileName = $"merged_{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:yyyyMMddHHmmss}.pdf";
                var message = new PdfProcessingMessage
                {
                    RequestId = requestId,
                    FileName = fileName,
                    OperationType = "MergePdf",
                    SourceFilePath = firstPdfPath,
                    OutputFilePath = Path.Combine(_outputPath, outputFileName),
                    AdditionalData = secondFilePath,
                    Status = "Waiting",
                    Progress = 0,
                    RequestTime = DateTime.UtcNow
                };

                // İşlem durumunu kaydet
                _processingStatusService.AddOrUpdateStatus(message);

                // Mesajı Kafka'ya gönder
                await _kafkaProducer.ProduceMessageAsync(message);

                // TempData'ya bilgileri kaydet
                TempData["Message"] = "PDF birleştirme işlemi başlatıldı. İşlem tamamlandığında sonuçlar burada gösterilecek.";
                TempData["ProcessingId"] = requestId;
                TempData["OutputFileName"] = outputFileName;
                TempData["MergedFilePath"] = $"/output/{outputFileName}";

                // Durumu logla
                _logger.LogInformation($"PDF birleştirme işlemi başlatıldı. RequestId: {requestId}, Çıktı dosyası: {outputFileName}");

                return RedirectToAction("Index", new { fileName, activeTool = "mergePdf" });
            }
            catch (Exception ex)
            {
                TempData["Message"] = $"PDF birleştirme işlemi başlatılırken bir hata oluştu: {ex.Message}";
                return RedirectToAction("Index", new { fileName, activeTool = "mergePdf" });
            }
        }

        [HttpPost]
        public IActionResult DeletePage(string fileName, int pageNumber)
        {
            if (string.IsNullOrEmpty(fileName) || pageNumber < 1)
            {
                TempData["Message"] = "Geçersiz istek.";
                return RedirectToAction("Index", new { fileName, activeTool = "deletePages" });
            }

            var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            var inputPath = Path.Combine(uploadsPath, fileName);

            if (!System.IO.File.Exists(inputPath))
            {
                TempData["Message"] = "PDF dosyası bulunamadı.";
                return RedirectToAction("Index", new { fileName, activeTool = "deletePages" });
            }

            var newFileName = $"deleted_{Guid.NewGuid():N}.pdf";
            var outputPath = Path.Combine(uploadsPath, newFileName);

            try
            {
                // Önce PDF'i sadece okuma modunda açalım ve içeriğini yeni bir PDF'e kopyalayalım
                using (var inputDocument = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import))
                {
                    if (pageNumber > inputDocument.PageCount)
                    {
                        TempData["Message"] = "Geçersiz sayfa numarası.";
                        return RedirectToAction("Index", new { fileName, activeTool = "deletePages" });
                    }

                    // Yeni bir PDF oluştur
                    using (var outputDocument = new PdfSharpCore.Pdf.PdfDocument())
                    {
                        // Orijinal PDF'in özelliklerini kopyala
                        outputDocument.Info.Title = inputDocument.Info.Title;
                        outputDocument.Info.Author = inputDocument.Info.Author;
                        outputDocument.Info.Subject = inputDocument.Info.Subject;
                        outputDocument.Info.Keywords = inputDocument.Info.Keywords;
                        outputDocument.Info.Creator = inputDocument.Info.Creator;
                        
                        // Silinecek sayfa hariç tüm sayfaları kopyala
                        for (int i = 0; i < inputDocument.PageCount; i++)
                        {
                            if (i != pageNumber - 1) // Silinecek sayfayı atla
                            {
                                outputDocument.AddPage(inputDocument.Pages[i]);
                            }
                        }
                        
                        // Yeni PDF'i kaydet
                        outputDocument.Save(outputPath);
                    }
                }

                TempData["Message"] = "Sayfa silindi.";
                return RedirectToAction("Index", new
                {
                    fileName = newFileName,
                    activeTool = "deletePages"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PDF sayfa silme işleminde hata: {Message}", ex.Message);
                TempData["Message"] = $"PDF sayfa silme işleminde hata: {ex.Message}";
                return RedirectToAction("Index", new { fileName, activeTool = "deletePages" });
            }
        }

        public IActionResult ViewResult(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return NotFound();
            }

            var filePath = Path.Combine(_outputPath, fileName);
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound();
            }

            var fileExtension = Path.GetExtension(fileName).ToLowerInvariant();

            // Dosya türüne göre işlem yap
            if (fileExtension == ".txt")
            {
                // Metin dosyası içeriğini oku
                var content = System.IO.File.ReadAllText(filePath);
                return Content(content, "text/plain");
            }
            else if (fileExtension == ".docx" || fileExtension == ".pdf")
            {
                // Dosyayı görüntüle
                return Redirect($"/output/{fileName}");
            }

            return NotFound();
        }
        
        [HttpGet]
        public IActionResult DownloadFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return NotFound("Dosya adı belirtilmedi.");
            }
            
            try
            {
                // Dosya yolunu oluştur
                string filePath = Path.Combine(_outputPath, fileName);
                
                // Dosyanın var olup olmadığını kontrol et
                if (!System.IO.File.Exists(filePath))
                {
                    _logger.LogError($"İndirilmek istenen dosya bulunamadı: {filePath}");
                    return NotFound("Dosya bulunamadı.");
                }
                
                // Dosya uzantısına göre MIME türünü belirle
                string contentType = "application/octet-stream";
                string extension = Path.GetExtension(filePath).ToLowerInvariant();
                
                if (extension == ".txt")
                    contentType = "text/plain; charset=utf-8";
                else if (extension == ".docx")
                    contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                else if (extension == ".pdf")
                    contentType = "application/pdf";
                
                // Dosya içeriğini oku
                byte[] fileBytes = System.IO.File.ReadAllBytes(filePath);
                
                // Dosyayı indir
                return File(fileBytes, contentType, Path.GetFileName(filePath));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Dosya indirme hatası: {ex.Message}");
                return StatusCode(500, "Dosya indirilirken bir hata oluştu.");
            }
        }
        
        [HttpGet]
        public IActionResult GetExtractedText(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return BadRequest("Dosya adı belirtilmedi.");

            try
            {
                // Çıkarılan metin dosyasını bul
                var status = _processingStatusService.GetStatus(fileName);
                if (status == null || string.IsNullOrEmpty(status.OutputFilePath))
                    return NotFound("Metin dosyası bulunamadı.");

                var filePath = status.OutputFilePath;
                if (!System.IO.File.Exists(filePath))
                    return NotFound("Metin dosyası bulunamadı.");

                // Dosya içeriğini oku
                var text = System.IO.File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                return Content(text, "text/plain", System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Metin okuma hatası: {ex.Message}");
                return StatusCode(500, "Metin okunurken bir hata oluştu.");
            }
        }


    }
}