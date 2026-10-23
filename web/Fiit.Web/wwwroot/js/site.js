// Мелкие помощники для Blazor: скачать файл и скопировать текст.
window.fiit = {
    saveFile(name, mime, bytes) {
        const blob = new Blob([bytes], { type: mime });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = name;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 10000);
    },
    async copy(text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            return false;
        }
    },
    scrollToBottom(el) {
        if (el) el.scrollTop = el.scrollHeight;
    },
};
