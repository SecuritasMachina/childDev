window.childdev = window.childdev || {};

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
        }
    };
    document.addEventListener('keydown', window._cdSearchHotkeyListener);
};
