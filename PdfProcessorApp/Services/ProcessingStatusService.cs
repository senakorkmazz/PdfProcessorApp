using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using PdfProcessorApp.Models;

namespace PdfProcessorApp.Services
{
    public class ProcessingStatusService
    {
        private readonly ConcurrentDictionary<string, PdfProcessingMessage> _processingStatus;
        
        public ProcessingStatusService()
        {
            _processingStatus = new ConcurrentDictionary<string, PdfProcessingMessage>();
        }
        
        public void AddOrUpdateStatus(PdfProcessingMessage message)
        {
            _processingStatus[message.RequestId] = message;
        }
        
        public void UpdateProgress(string requestId, int progress, string status = null)
        {
            if (_processingStatus.TryGetValue(requestId, out var message))
            {
                message.Progress = progress;
                if (status != null)
                {
                    message.Status = status;
                }
                
                if (progress == 100)
                {
                    message.Status = "Completed";
                    message.CompletionTime = DateTime.UtcNow;
                }
                
                _processingStatus[requestId] = message;
            }
        }
        
        public void SetFailed(string requestId, string errorMessage = null)
        {
            if (_processingStatus.TryGetValue(requestId, out var message))
            {
                message.Status = "Failed";
                message.CompletionTime = DateTime.UtcNow;
                _processingStatus[requestId] = message;
            }
        }
        
        public PdfProcessingMessage GetStatus(string requestId)
        {
            if (_processingStatus.TryGetValue(requestId, out var message))
            {
                return message;
            }
            
            return null;
        }
        
        public IEnumerable<PdfProcessingMessage> GetAllProcessingMessages()
        {
            return _processingStatus.Values;
        }
        
        public void CleanupOldMessages(TimeSpan olderThan)
        {
            var cutoffTime = DateTime.UtcNow - olderThan;
            
            foreach (var key in _processingStatus.Keys)
            {
                if (_processingStatus.TryGetValue(key, out var message))
                {
                    if ((message.Status == "Completed" || message.Status == "Failed") && 
                        message.CompletionTime.HasValue && 
                        message.CompletionTime.Value < cutoffTime)
                    {
                        _processingStatus.TryRemove(key, out _);
                    }
                }
            }
        }
    }
}
