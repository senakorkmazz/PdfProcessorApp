// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// PDF İşlemleri için JavaScript kodu
document.addEventListener('DOMContentLoaded', function () {
    // Tool menü butonlarını dinle
    const toolButtons = document.querySelectorAll('[data-tool]');
    toolButtons.forEach(button => {
        button.addEventListener('click', function () {
            const toolId = this.getAttribute('data-tool');
            showTool(toolId);
        });
    });

    // URL'den activeTool parametresini al
    const urlParams = new URLSearchParams(window.location.search);
    const activeTool = urlParams.get('activeTool');

    // PDF Sayfası Silme arayüzünü yönet
    setupRemovePdfPagesForm();
});

// Belirtilen tool'u göster, diğerlerini gizle
function showTool(toolId) {
    // Tüm tool section'larını gizle
    document.querySelectorAll('.tool-section').forEach(section => {
        section.classList.add('d-none');
    });

    // Seçilen tool'u göster
    const selectedTool = document.getElementById('tool-' + toolId);
    if (selectedTool) {
        selectedTool.classList.remove('d-none');
    }

    // Menü butonlarının active class'ını güncelle
    document.querySelectorAll('[data-tool]').forEach(button => {
        button.classList.remove('active');
    });

    // Seçilen butonu active yap
    const selectedButton = document.querySelector('[data-tool="' + toolId + '"]');
    if (selectedButton) {
        selectedButton.classList.add('active');
    }
}

// PDF Sayfası Silme formunu yönet
function setupRemovePdfPagesForm() {
    const form = document.querySelector('form[asp-action="RemovePdfPages"]');
    if (form) {
        form.addEventListener('submit', function(e) {
            const pageNumbersInput = this.querySelector('input[name="pageNumbers"]');
            if (pageNumbersInput) {
                const pageNumbers = pageNumbersInput.value.trim();
                if (!pageNumbers) {
                    e.preventDefault();
                    alert('Lütfen silinecek sayfa numaralarını girin.');
                    return false;
                }
                
                // Sayfa numaralarının geçerli olduğunu kontrol et
                const pageNumbersArray = pageNumbers.split(',');
                for (const pageNumber of pageNumbersArray) {
                    const num = parseInt(pageNumber.trim());
                    if (isNaN(num) || num <= 0) {
                        e.preventDefault();
                        alert('Geçersiz sayfa numarası: ' + pageNumber.trim() + '. Sayfa numaraları pozitif tam sayı olmalıdır.');
                        return false;
                    }
                }
            }
        });
    }
}
