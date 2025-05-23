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
using IronPdf;

namespace PdfProcessorApp.Controllers
{
    public class PdfController : Controller
    {
        private readonly string _uploadPath;
        private readonly string _outputPath;

        public PdfController(IWebHostEnvironment env)
        {
            _uploadPath = Path.Combine(env.WebRootPath, "uploads");
            _outputPath = Path.Combine(env.WebRootPath, "output");

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
        public IActionResult ExtractText(string fileName)
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
            var extractedText = new StringBuilder();

            try
            {
                using (UglyToad.PdfPig.PdfDocument document = UglyToad.PdfPig.PdfDocument.Open(filePath))
                {
                    foreach (var page in document.GetPages())
                    {
                        var lines = GetContentLinesByPosition(page, 90, 90);
                        foreach (var line in lines)
                            extractedText.AppendLine(line);
                    }
                }
                ViewBag.ExtractedText = extractedText.ToString();
            }
            catch (System.Exception ex)
            {
                ViewBag.ExtractedText = $"Metin çıkarma sırasında bir hata oluştu: {ex.Message}";
            }
            return View("Index");
        }

        [HttpPost]
        public IActionResult ConvertPdf(string fileName, string targetFormat)
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
                var extractedText = new StringBuilder();
                using (UglyToad.PdfPig.PdfDocument document = UglyToad.PdfPig.PdfDocument.Open(filePath))
                {
                    foreach (var page in document.GetPages())
                    {
                        var lines = GetContentLinesByPosition(page, 90, 90);
                        foreach (var line in lines)
                            extractedText.AppendLine(line);
                    }
                }

                if (targetFormat.ToLower() == "txt")
                {
                    System.IO.File.WriteAllText(outputFilePath, extractedText.ToString(), Encoding.UTF8);
                    ViewBag.ConvertedFilePath = Path.Combine("/output", outputFileName);
                    ViewBag.Message = "PDF başarıyla TXT dosyasına dönüştürüldü.";
                }
                else if (targetFormat.ToLower() == "docx")
                {
                    using (var wordDoc = WordprocessingDocument.Create(outputFilePath, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
                    {
                        var mainPart = wordDoc.AddMainDocumentPart();
                        mainPart.Document = new Document();
                        var body = mainPart.Document.AppendChild(new Body());
                        foreach (var line in extractedText.ToString().Split('\n'))
                        {
                            body.AppendChild(new Paragraph(new Run(new Text(line))));
                        }
                    }
                    ViewBag.ConvertedFilePath = Path.Combine("/output", outputFileName);
                    ViewBag.Message = "PDF başarıyla DOCX dosyasına dönüştürüldü.";
                }
                else
                {
                    ViewBag.Message = "Desteklenmeyen format.";
                    return View("Index");
                }
            }
            catch (System.Exception ex)
            {
                ViewBag.Message = $"{targetFormat.ToUpper()}'ye dönüştürme sırasında bir hata oluştu: {ex.Message}";
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
        public async Task<IActionResult> MergePdf(string fileName, IFormFile fileToMerge)
        {
            ViewBag.FileName = fileName;
            ViewBag.ActiveTool = "mergePdf";

            if (string.IsNullOrEmpty(fileName) || fileToMerge == null || fileToMerge.Length == 0)
            {
                TempData["Message"] = "Geçersiz istek veya dosya seçilmedi.";
                return RedirectToAction("Index", new { fileName, activeTool = "mergePdf" });
            }

            var firstPdfPath = Path.Combine(_uploadPath, fileName);
            if (!System.IO.File.Exists(firstPdfPath))
            {
                TempData["Message"] = "Kaynak PDF dosyası bulunamadı.";
                return RedirectToAction("Index", new { fileName, activeTool = "mergePdf" });
            }

            try
            {
                var tempFile = Path.GetTempFileName();
                using (var stream = new FileStream(tempFile, FileMode.Create))
                {
                    await fileToMerge.CopyToAsync(stream);
                }

                var pdf1 = PdfDocument.FromFile(firstPdfPath);
                var pdf2 = PdfDocument.FromFile(tempFile);

                var mergedPdf = PdfDocument.Merge(new[] { pdf1, pdf2 });

                var newFileName = $"merged_{Guid.NewGuid():N}.pdf";
                var outputPath = Path.Combine(_uploadPath, newFileName);
                mergedPdf.SaveAs(outputPath);

                System.IO.File.Delete(tempFile);

                TempData["Message"] = "PDF'ler başarıyla birleştirildi.";
                return RedirectToAction("Index", new
                {
                    fileName = newFileName,
                    activeTool = "mergePdf"
                });
            }
            catch (Exception ex)
            {
                TempData["Message"] = $"PDF birleştirme sırasında bir hata oluştu: {ex.Message}";
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

            var pdf = PdfDocument.FromFile(inputPath);
            if (pageNumber > pdf.PageCount)
            {
                TempData["Message"] = "Geçersiz sayfa numarası.";
                return RedirectToAction("Index", new { fileName, activeTool = "deletePages" });
            }
            pdf.RemovePage(pageNumber - 1); 
            pdf.SaveAs(outputPath);

            TempData["Message"] = "Sayfa silindi.";
            return RedirectToAction("Index", new
            {
                fileName = newFileName,
                activeTool = "deletePages"
            });
        }

    }
    }
