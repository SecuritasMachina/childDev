window.childdev = window.childdev || {};

window.childdev.setupSearchHotkey = function (dotNetRef) {
    if (window._cdSearchHotkeyListener) {
        document.removeEventListener('keydown', window._cdSearchHotkeyListener);
    }
    window._cdSearchHotkeyListener = function (e) {
        if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('TriggerOpenSearch');
        } else if (e.key === 'Escape') {
            dotNetRef.invokeMethodAsync('TriggerCloseSearch');
        }
    };
    document.addEventListener('keydown', window._cdSearchHotkeyListener);
};
