// İşlem durumunu takip etmek için JavaScript kodu
document.addEventListener("DOMContentLoaded", function() {
    console.log('Sayfa yüklendi, işlem durumu kontrol ediliyor...');
    
    // Form gönderimini dinle
    setupFormSubmitListener();
    
    // İşlem durumu bilgisi varsa, ilerleme çubuğunu başlat
    const processingInfo = document.getElementById('processingInfo');
    if (processingInfo) {
        console.log('processingInfo elementi bulundu');
        const requestId = processingInfo.getAttribute('data-request-id');
        console.log('RequestId:', requestId);
        if (requestId) {
            console.log('RequestId bulundu, durumu kontrol ediliyor...');
            startPollingStatus(requestId);
        }
    } else {
        console.log('processingInfo elementi bulunamadı');
    }
    
    // Sayfa yüklenirken işlem durumunu kontrol et (sayfayı yeniledikten sonra)
    const urlParams = new URLSearchParams(window.location.search);
    const activeTool = urlParams.get('activeTool');
    console.log('Aktif araç:', activeTool);
    
    if (activeTool === "mergePdf") {
        // Birleştirilmiş PDF konteynerini kontrol et
        const mergedPdfContainer = document.getElementById('mergedPdfContainer');
        if (mergedPdfContainer) {
            console.log('mergedPdfContainer elementi bulundu');
            mergedPdfContainer.style.display = 'block';
        }
        
        // İşlem durumu bilgisini kontrol et
        const processingId = document.querySelector('[data-request-id]')?.getAttribute('data-request-id');
        console.log('Sayfa parametrelerinden bulunan processingId:', processingId);
        if (processingId) {
            startPollingStatus(processingId);
        }
    }
});

// Form gönderimini dinleyen fonksiyon
function setupFormSubmitListener() {
    const mergePdfForm = document.getElementById('mergePdfForm');
    if (mergePdfForm) {
        console.log('mergePdfForm bulundu, submit dinleyicisi ekleniyor');
        mergePdfForm.addEventListener('submit', function(e) {
            // Form gönderildiğinde ilerleme çubuğunu göster
            const progressContainer = document.getElementById('progressContainer');
            if (progressContainer) {
                progressContainer.style.display = 'block';
                // Başlangıç durumunu ayarla
                const progressBar = document.getElementById('progressBar');
                const progressText = document.getElementById('progressText');
                const statusText = document.getElementById('statusText');
                
                if (progressBar && progressText && statusText) {
                    progressBar.style.width = '10%';
                    progressBar.setAttribute('aria-valuenow', 10);
                    progressText.textContent = '10%';
                    statusText.textContent = 'İşleniyor...';
                }
            }
        });
    }
}

// İşlem durumunu kontrol etmek için polling başlat
function startPollingStatus(requestId) {
    console.log('startPollingStatus çağrıldı, requestId:', requestId);
    if (!requestId) {
        console.error('RequestId bulunamadı!');
        return;
    }

    // İlerleme çubuğunu göster
    const progressContainer = document.getElementById('progressContainer');
    if (progressContainer) {
        progressContainer.style.display = 'block';
    }
    
    // İlerleme çubuğunu başlangıç değeriyle güncelle
    updateProgressBarUI(10, 'Processing', 'İşleniyor...');

    // İlerleme çubuğunu güncellemek için yardımcı fonksiyon
    function updateProgressBarUI(progress, status, statusMessage) {
        const progressBar = document.getElementById('progressBar');
        const progressText = document.getElementById('progressText');
        const statusText = document.getElementById('statusText');
        
        if (progressBar && progressText && statusText) {
            // İlerleme çubuğunu güncelle
            progressBar.style.width = `${progress}%`;
            progressBar.setAttribute('aria-valuenow', progress);
            progressText.textContent = `${progress}%`;
            
            // Durum metnini güncelle
            statusText.textContent = statusMessage;
            
            // İşlem durumuna göre renk değiştir
            if (progress < 100 && status !== 'Failed') {
                progressBar.classList.remove('bg-success', 'bg-danger');
                progressBar.classList.add('bg-info');
            } else if (status === 'Completed') {
                progressBar.classList.remove('bg-info', 'bg-danger');
                progressBar.classList.add('bg-success');
            } else if (status === 'Failed') {
                progressBar.classList.remove('bg-info', 'bg-success');
                progressBar.classList.add('bg-danger');
            }
        }
    }
    
    // Durum kontrolü için interval başlat
    const statusInterval = setInterval(function() {
        console.log('İşlem durumu kontrol ediliyor...', requestId);
        fetch(`/api/ProcessingStatus/${requestId}`)
            .then(response => {
                if (!response.ok) {
                    throw new Error('İşlem durumu alınamadı');
                }
                return response.json();
            })
            .then(data => {
                console.log('İşlem durumu:', data);
                
                // Durum metnini belirle
                let statusMessage = '';
                switch (data.status) {
                    case 'Waiting':
                        statusMessage = 'İşlem sıraya alındı...';
                        break;
                    case 'Processing':
                        statusMessage = 'İşleniyor...';
                        break;
                    case 'Completed':
                        statusMessage = 'İşlem tamamlandı!';
                        break;
                    case 'Failed':
                        statusMessage = 'İşlem başarısız oldu!';
                        break;
                    default:
                        statusMessage = data.status;
                }
                
                // İlerleme çubuğunu güncelle
                updateProgressBarUI(data.progress, data.status, statusMessage);
                
                // İşlem tamamlandıysa veya hata oluştuysa polling'i durdur
                if (data.status === 'Completed' || data.status === 'Failed') {
                    clearInterval(statusInterval);
                    console.log('İşlem tamamlandı veya hata oluştu. Durum:', data.status);
                    
                    // İşlem tamamlandıysa ve çıktı dosyası varsa sonucu göster
                    if (data.status === 'Completed' && data.outputFilePath) {
                        console.log('Çıktı dosyası:', data.outputFilePath);
                        
                        // Dosya adını çıkar
                        const fileName = data.outputFilePath.split(/[\\\/]/).pop();
                        const fileUrl = `/output/${fileName}`;
                        console.log('Düzeltilmiş dosya URL:', fileUrl);
                        
                        if (data.operationType === 'ExtractText') {
                            showExtractedText(data.outputFilePath);
                        } else if (data.operationType === 'MergePdf') {
                            // Birleştirilmiş PDF'i göster
                            console.log('Birleştirilmiş PDF gösteriliyor...');
                            showMergedPdf(fileUrl);
                        } else if (data.operationType === 'ConvertPdf') {
                            // Dönüştürme işlemi tamamlandığında indirme linkini göster
                            console.log('Dönüştürme işlemi tamamlandı, indirme linki gösteriliyor...');
                            showConvertedFile(fileUrl, fileName);
                        }
                    }
                }
            })
            .catch(error => {
                console.error('İşlem durumu alınırken hata oluştu:', error);
            });
    }, 2000); // Her 2 saniyede bir kontrol et
}

function updateProgressBar(requestId) {
    console.log('updateProgressBar çağrıldı, requestId:', requestId);
    if (!requestId) {
        console.error('RequestId bulunamadı');
        return;
    }

    // İlerleme çubuğu elementlerini al
    const progressBar = document.getElementById('progressBar');
    const progressText = document.getElementById('progressText');
    const statusText = document.getElementById('statusText');
    const progressContainer = document.getElementById('progressContainer');
    
    if (!progressBar || !progressText || !statusText || !progressContainer) {
        console.error('İlerleme çubuğu elementleri bulunamadı');
        return;
    }

    // İlerleme çubuğunu görünür yap
    progressContainer.style.display = 'block';
    
    // İşlem durumunu kontrol et
    function checkStatus() {
        console.log('İşlem durumu kontrol ediliyor...');
        fetch(`/api/ProcessingStatus/${requestId}`)
            .then(response => {
                if (!response.ok) {
                    throw new Error('İşlem durumu alınamadı');
                }
                return response.json();
            })
            .then(data => {
                console.log('İşlem durumu:', data);
                // İlerleme çubuğunu güncelle
                progressBar.style.width = `${data.progress}%`;
                progressBar.setAttribute('aria-valuenow', data.progress);
                progressText.textContent = `${data.progress}%`;
                
                // Durum metnini güncelle
                let statusMessage = '';
                switch (data.status) {
                    case 'Waiting':
                        statusMessage = 'İşlem sıraya alındı...';
                        break;
                    case 'Processing':
                        statusMessage = 'İşleniyor...';
                        break;
                    case 'Completed':
                        statusMessage = 'İşlem tamamlandı!';
                        break;
                    case 'Failed':
                        statusMessage = 'İşlem başarısız oldu!';
                        progressBar.classList.remove('bg-info', 'bg-success');
                        progressBar.classList.add('bg-danger');
                        break;
                    default:
                        statusMessage = data.status;
                }
                statusText.textContent = statusMessage;
                
                // İşlem durumuna göre renk değiştir
                if (data.progress < 100 && data.status !== 'Failed') {
                    progressBar.classList.remove('bg-success', 'bg-danger');
                    progressBar.classList.add('bg-info');
                } else if (data.status === 'Completed') {
                    progressBar.classList.remove('bg-info', 'bg-danger');
                    progressBar.classList.add('bg-success');
                }
                
                // İşlem tamamlandıysa veya başarısız olduysa kontrolü durdur
                if (data.status === 'Completed' || data.status === 'Failed') {
                    clearInterval(statusInterval);
                    console.log('İşlem tamamlandı veya başarısız oldu. Durum kontrolü durduruldu.');
                    
                    // İşlem tamamlandıysa, sonuçları göster
                    if (data.status === 'Completed') {
                        console.log('İşlem tamamlandı, sonuçlar gösteriliyor. İşlem türü:', data.operationType);
                        // İşlem türüne göre farklı işlem yap
                        if (data.operationType === 'MergePdf') {
                            console.log('PDF birleştirme işlemi tamamlandı, sonuç gösteriliyor...');
                            // PDF birleştirme işlemi tamamlandığında birleştirilmiş PDF'i göster
                            setTimeout(() => {
                                // Çıktı dosyası yolundan dosya adını al
                                if (data.outputFilePath) {
                                    console.log('Çıktı dosyası yolu:', data.outputFilePath);
                                    let pdfUrl = '';
                                    
                                    // Dosya yolunu düzgün formatta al
                                    if (data.outputFilePath.startsWith('/')) {
                                        // Zaten / ile başlıyorsa
                                        pdfUrl = data.outputFilePath;
                                    } else {
                                        // Tam yoldan dosya adını çıkar
                                        const pathParts = data.outputFilePath.split(/[\\\/]/);
                                        const fileName = pathParts[pathParts.length - 1];
                                        pdfUrl = `/output/${fileName}`;
                                    }
                                    
                                    console.log('PDF URL:', pdfUrl);
                                    showMergedPdf(pdfUrl);
                                } else {
                                    console.error('Çıktı dosyası yolu bulunamadı');
                                }
                            }, 1000);
                        } else if (data.operationType === 'ConvertPdf') {
                            // Dönüştürme işlemi tamamlandığında indirme linkini göster
                            setTimeout(() => {
                                // Çıktı dosyası yolundan dosya adını al
                                if (data.outputFilePath) {
                                    // Tam yoldan dosya adını çıkar
                                    const fullPath = data.outputFilePath;
                                    const pathParts = fullPath.split(/[\\\/]/);
                                    const fileName = pathParts[pathParts.length - 1];
                                    
                                    console.log('Dönüştürme işlemi tamamlandı, dosya adı:', fileName);
                                    
                                    // Dosya uzantısına göre etiket belirle
                                    const extension = fileName.split('.').pop().toLowerCase();
                                    let fileTypeLabel = 'Dosyayı';
                                    
                                    if (extension === 'txt') {
                                        fileTypeLabel = 'TXT dosyasını';
                                    } else if (extension === 'docx') {
                                        fileTypeLabel = 'DOCX dosyasını';
                                    }
                                    
                                    // Bilgi mesajını gizle
                                    const convertingMessage = document.getElementById('convertingMessage');
                                    if (convertingMessage) {
                                        convertingMessage.style.display = 'none';
                                    }
                                    
                                    // İndirme butonunu göster ve güncelle
                                    const convertedFileContainer = document.getElementById('convertedFileContainer');
                                    const convertedFileLink = document.getElementById('convertedFileLink');
                                    
                                    if (convertedFileContainer && convertedFileLink) {
                                        convertedFileLink.href = `/Pdf/DownloadFile?fileName=${fileName}`;
                                        convertedFileLink.innerHTML = `<i class="bi bi-download"></i> ${fileTypeLabel} indir`;
                                        convertedFileContainer.style.display = 'block';
                                        
                                        // İlerleme çubuğu mesajını güncelle
                                        const statusText = document.getElementById('statusText');
                                        if (statusText) {
                                            statusText.textContent = 'Dönüştürme işlemi tamamlandı! Dosyayı indirebilirsiniz.';
                                        }
                                        
                                        console.log('İndirme linki güncellendi ve gösterildi');
                                    } else {
                                        console.error('İndirme buton elemanları bulunamadı');
                                    }
                                }
                            }, 1000);
                        } else if (data.operationType === 'ExtractText') {
                            // Metin çıkarma işlemi tamamlandığında metni göster
                            setTimeout(() => {
                                // Bilgi mesajını gizle
                                const extractingMessage = document.getElementById('extractingMessage');
                                if (extractingMessage) {
                                    extractingMessage.style.display = 'none';
                                }
                                
                                // Çıktı dosyası yolundan dosya adını al
                                if (data.outputFilePath) {
                                    // Metin dosyasının içeriğini oku
                                    fetch(`/Pdf/GetExtractedText?fileName=${data.requestId}`)
                                        .then(response => response.text())
                                        .then(text => {
                                            // Metin içeriğini göster
                                            const extractedTextContainer = document.getElementById('extractedTextContainer');
                                            if (extractedTextContainer) {
                                                extractedTextContainer.style.display = 'block';
                                                extractedTextContainer.innerHTML = `<pre class="extracted-text">${text}</pre>`;
                                                
                                                // İlerleme çubuğu mesajını güncelle
                                                const statusText = document.getElementById('statusText');
                                                if (statusText) {
                                                    statusText.textContent = 'Metin çıkarma işlemi tamamlandı!';
                                                }
                                            }
                                        })
                                        .catch(error => {
                                            console.error('Metin okuma hatası:', error);
                                        });
                                }
                            }, 1000);
                        } else {
                            // Diğer işlemler için herhangi bir özel işlem yapma
                            console.log('Bilinmeyen işlem türü tamamlandı:', data.operationType);
                        }
                    }
                }
            })
            .catch(error => {
                console.error('İşlem durumu alınırken hata oluştu:', error);
            });
    }
    
    // İlk kontrolü hemen yap
    checkStatus();
    
    // Her 2 saniyede bir kontrol et
    const statusInterval = setInterval(checkStatus, 2000);
    
    // Sayfa kapatıldığında interval'i temizle
    window.addEventListener('beforeunload', () => {
        clearInterval(statusInterval);
    });
}

// Birleştirilmiş PDF'i göstermek için fonksiyon
function showMergedPdf(pdfUrl) {
    console.log('showMergedPdf çağrıldı, URL:', pdfUrl);
    
    if (!pdfUrl) {
        console.error('PDF URL\'i bulunamadı!');
        return;
    }
    
    // URL'in doğru formatta olduğundan emin ol
    if (!pdfUrl.startsWith('/')) {
        pdfUrl = `/output/${pdfUrl.split(/[\\\/]/).pop()}`;
    }
    
    console.log('Düzeltilmiş PDF URL:', pdfUrl);
    
    // Birleştirme işlemi tamamlandı mesajını göster
    const alertDiv = document.createElement('div');
    alertDiv.className = 'alert alert-success mb-3';
    alertDiv.innerHTML = 'PDF birleştirme işlemi tamamlandı!';
    
    // Mevcut uyarı mesajlarını temizle
    const existingAlerts = document.querySelectorAll('#tool-mergePdf .alert');
    existingAlerts.forEach(alert => {
        // processingInfo elementini silme
        if (!alert.id || alert.id !== 'processingInfo') {
            alert.remove();
        }
    });
    
    // Yeni uyarı mesajını ekle
    const mergePdfDiv = document.querySelector('#tool-mergePdf');
    if (mergePdfDiv) {
        mergePdfDiv.insertBefore(alertDiv, mergePdfDiv.firstChild);
    } else {
        console.error('#tool-mergePdf elementi bulunamadı');
        // Alternatif olarak sayfadaki herhangi bir yere ekle
        const mainContent = document.querySelector('main') || document.body;
        mainContent.insertBefore(alertDiv, mainContent.firstChild);
    }
    
    // Birleştirilmiş PDF iframe'e yükle
    const mergedPdfContainer = document.getElementById('mergedPdfContainer');
    const mergedPdfFrame = document.getElementById('mergedPdfFrame');
    
    if (mergedPdfContainer && mergedPdfFrame) {
        console.log('PDF iframe\'e yükleniyor...');
        mergedPdfFrame.src = pdfUrl;
        mergedPdfContainer.style.display = 'block';
    } else {
        console.log('mergedPdfContainer veya mergedPdfFrame bulunamadı, alternatif gösterim oluşturuluyor');
        
        // Alternatif olarak yeni bir embed elementi oluştur
        const cardHtml = `
            <div class="card mt-3" id="pdfCard">
                <div class="card-header">
                    <div class="d-flex justify-content-between align-items-center">
                        <h5 class="mb-0">Birleştirilmiş PDF</h5>
                        <a href="${pdfUrl}" class="btn btn-success btn-sm" download>
                            <i class="bi bi-download"></i> İndir
                        </a>
                    </div>
                </div>
                <div class="card-body p-0">
                    <embed src="${pdfUrl}" type="application/pdf" style="width: 100%; height: 500px;" />
                </div>
            </div>
        `;
        
        // Mevcut kartı temizle
        const existingCards = document.querySelectorAll('#tool-mergePdf .card');
        existingCards.forEach(card => {
            if (card.id !== 'pdfCard') {
                card.remove();
            }
        });
        
        // Yeni kartı ekle
        const cardContainer = document.createElement('div');
        cardContainer.innerHTML = cardHtml;
        
        if (mergePdfDiv) {
            mergePdfDiv.appendChild(cardContainer.firstElementChild);
        } else {
            const mainContent = document.querySelector('main') || document.body;
            mainContent.appendChild(cardContainer.firstElementChild);
        }
    }
    
    // İşlem durumu konteynerini gizle
    const progressContainer = document.getElementById('progressContainer');
    if (progressContainer) {
        progressContainer.style.display = 'none';
    }
    
    console.log('PDF gösterimi tamamlandı');
}
