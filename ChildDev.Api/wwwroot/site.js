window.childdev = window.childdev || {};

window.childdev.shareOrCopy = async function (title, text) {
    if (navigator.share) {
        try {
            await navigator.share({ title: title, text: text });
            return 'shared';
        } catch (e) {
            if (e.name === 'AbortError') return 'cancelled';
        }
    }
    await navigator.clipboard.writeText(text);
    return 'copied';
};

window.childdev.registerPageShortcut = function (key, dotNetRef, method) {
    window._cdPageShortcut = { key, dotNetRef, method };
};

window.childdev.unregisterPageShortcut = function () {
    window._cdPageShortcut = null;
};

window.childdev.setupSearchHotkey = function (dotNetRef) {
    if (window._cdSearchHotkeyListener) {
        document.removeEventListener('keydown', window._cdSearchHotkeyListener);
    }
    window._cdSearchHotkeyListener = function (e) {
        if (e.target && (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.isContentEditable)) {
            if (e.key === 'Escape') dotNetRef.invokeMethodAsync('TriggerCloseSearch');
            return;
        }
        if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('TriggerOpenSearch');
        } else if (e.key === 'Escape') {
            dotNetRef.invokeMethodAsync('TriggerCloseAll');
        } else if (e.key === '?') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('TriggerOpenHelp');
        } else if (window._cdPageShortcut && e.key === window._cdPageShortcut.key && !e.ctrlKey && !e.metaKey && !e.altKey) {
            e.preventDefault();
            window._cdPageShortcut.dotNetRef.invokeMethodAsync(window._cdPageShortcut.method);
        }
    };
    document.addEventListener('keydown', window._cdSearchHotkeyListener);
};
