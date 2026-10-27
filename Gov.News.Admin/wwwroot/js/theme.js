// Live preview and readability badges for the theme form. The server applies the same
// WCAG AA rule (4.5:1) when publishing; this only shows it sooner.
(function () {
    const rows = [...document.querySelectorAll('.theme-row')];
    if (!rows.length) return;

    function luminance(hex) {
        const n = parseInt(hex.slice(1), 16);
        const channel = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
        return 0.2126 * channel((n >> 16) & 255) + 0.7152 * channel((n >> 8) & 255) + 0.0722 * channel(n & 255);
    }
    function ratio(a, b) {
        const x = luminance(a), y = luminance(b);
        return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05);
    }
    const value = id => document.getElementById(id).value.toUpperCase();
    const style = (selector, property, colour) =>
        document.querySelectorAll(`[data-preview="${selector}"]`).forEach(el => { el.style[property] = colour; });

    function update() {
        for (const row of rows) {
            const input = row.querySelector('.theme-color');
            const against = row.dataset.againstField ? value(row.dataset.againstField) : row.dataset.against;
            const r = ratio(input.value.toUpperCase(), against.toUpperCase());
            const badge = row.querySelector('.theme-badge');
            const ok = r >= 4.5;
            badge.textContent = (ok ? 'Readable ' : 'Too faint ') + r.toFixed(1) + ':1';
            badge.className = 'badge theme-badge ' + (ok ? 'text-bg-success' : 'text-bg-danger');
            row.querySelector('.theme-hex').textContent = input.value.toUpperCase();
        }
        style('primary-border', 'borderBottomColor', value('Input_Primary'));
        style('primary-bg', 'backgroundColor', value('Input_Primary'));
        style('link-text', 'color', value('Input_Link'));
        style('text', 'color', value('Input_Text'));
        style('banner-info', 'backgroundColor', value('Input_BannerInfo'));
        style('banner-emergency', 'backgroundColor', value('Input_BannerEmergency'));
        style('footer', 'backgroundColor', value('Input_FooterBackground'));
        style('footer', 'color', value('Input_Text'));
    }

    rows.forEach(row => row.querySelector('.theme-color').addEventListener('input', update));
    update();
})();
