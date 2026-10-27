// Live preview and character count for the banner form. The form works without this script.
(function () {
    const preview = document.getElementById('banner-preview');
    const previewText = document.getElementById('banner-preview-text');
    const previewOff = document.getElementById('banner-preview-off');
    const message = document.getElementById('Input_Message');
    const count = document.getElementById('message-count');
    if (!preview || !message) return;

    function update() {
        const mode = document.querySelector('input[name="Input.Mode"]:checked')?.value ?? 'off';
        preview.hidden = mode === 'off';
        previewOff.hidden = mode !== 'off';
        preview.style.backgroundColor = mode === 'emergency' ? preview.dataset.emergencyColor : preview.dataset.infoColor;
        previewText.textContent = message.value;
        count.textContent = message.value.length + ' / ' + message.maxLength;
    }

    message.addEventListener('input', update);
    document.querySelectorAll('input[name="Input.Mode"]').forEach(radio => radio.addEventListener('change', update));
})();
