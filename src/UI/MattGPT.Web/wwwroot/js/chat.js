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
