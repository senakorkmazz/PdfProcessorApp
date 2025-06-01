using System;

namespace PdfProcessorApp.Models
{
    public class PdfProcessingMessage
    {
        public string FileName { get; set; }
        public string OperationType { get; set; } 
        public string TargetFormat { get; set; }
        public string SourceFilePath { get; set; }
        public string OutputFilePath { get; set; }
        public string AdditionalData { get; set; } 
        public DateTime RequestTime { get; set; } = DateTime.UtcNow;
        public string RequestId { get; set; } = Guid.NewGuid().ToString();
        
        public int Progress { get; set; } = 0; 
        public string Status { get; set; } = "Waiting"; 
        public DateTime? CompletionTime { get; set; }
    }
}
