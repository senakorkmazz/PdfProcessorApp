using System;

namespace PdfProcessorApp.Models
{
    public class PdfProcessingMessage
    {
        public string FileName { get; set; }
        public string OperationType { get; set; } // "ConvertToPdf", "ExtractText", "ConvertToDocx", "MergePdf", etc.
        public string TargetFormat { get; set; }
        public string SourceFilePath { get; set; }
        public string OutputFilePath { get; set; }
        public string AdditionalData { get; set; } // İkinci PDF yolu, ek parametreler vb. için
        public DateTime RequestTime { get; set; } = DateTime.UtcNow;
        public string RequestId { get; set; } = Guid.NewGuid().ToString();
        
        // İşlem durumu için özellikler
        public int Progress { get; set; } = 0; // 0-100 arasında yüzde değeri
        public string Status { get; set; } = "Waiting"; // Waiting, Processing, Completed, Failed
        public DateTime? CompletionTime { get; set; }
    }
}
