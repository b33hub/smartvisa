window.ssScroll = (id) => document.getElementById(id)?.scrollIntoView({ block: 'nearest' });

window.downloadFileFromStream = async (fileName, streamRef) => {
    const buffer = await streamRef.arrayBuffer();
    const url = URL.createObjectURL(new Blob([buffer], { type: 'application/pdf' }));
    const a = document.createElement('a');
    a.href = url; a.download = fileName;
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
};

window.showPdf = async (frameId, streamRef) => {
    const buffer = await streamRef.arrayBuffer();
    const frame = document.getElementById(frameId);
    if (!frame) return;
    if (frame.dataset.url) URL.revokeObjectURL(frame.dataset.url);
    const url = URL.createObjectURL(new Blob([buffer], { type: 'application/pdf' }));
    frame.dataset.url = url;
    frame.src = url + '#toolbar=0&navpanes=0';
};

window.toggleTheme = () => {
    const root = document.documentElement;
    const cur = root.dataset.theme || (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
    const next = cur === 'dark' ? 'light' : 'dark';
    root.dataset.theme = next; localStorage.setItem('theme', next);
};
