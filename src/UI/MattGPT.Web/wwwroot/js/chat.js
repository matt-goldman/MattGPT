export function preventEnterNewline(element) {
    if (!element) return;
    element.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
        }
    });
}

// Auto-scroll: scroll the chat message container to the bottom.
export function scrollToBottom(elementId) {
    const el = document.getElementById(elementId);
    if (!el) return;
    el.scrollTop = el.scrollHeight;
}

// Smoothly scroll the chat message container to the top.
export function scrollToTop(elementId) {
    const el = document.getElementById(elementId);
    if (!el) return;
    el.scrollTo({ top: 0, behavior: 'smooth' });
}

// Show the button only while the container is scrolled more than threshold px from the top.
// Toggled here rather than from Blazor so scrolling doesn't cost a server round-trip per event.
export function toggleOnScroll(containerId, buttonId, threshold) {
    const el = document.getElementById(containerId);
    const button = document.getElementById(buttonId);
    if (!el || !button) return;
    const update = () => button.classList.toggle('hidden', el.scrollTop <= (threshold ?? 300));
    el.addEventListener('scroll', update, { passive: true });
    update();
}

// Returns true if the element is scrolled near the bottom (within threshold px).
export function isNearBottom(elementId, threshold) {
    const el = document.getElementById(elementId);
    if (!el) return true;
    return el.scrollHeight - el.scrollTop - el.clientHeight <= (threshold ?? 100);
}

// Update the browser URL without triggering Blazor navigation.
export function pushState(url) {
    history.replaceState(null, '', url);
}

// Save text as a file in the browser (e.g. an exported conversation record).
export function downloadTextFile(fileName, content, mimeType) {
    const blob = new Blob([content], { type: (mimeType ?? 'text/plain') + ';charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
}
