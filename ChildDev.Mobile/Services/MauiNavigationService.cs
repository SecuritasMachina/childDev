namespace LevelUp.Services;

public class MauiNavigationService : INavigationService
{
    public Task GoToAsync(string route) =>
#if ANDROID || IOS || MACCATALYST || WINDOWS
        Shell.Current.GoToAsync(AbsoluteRoute(route));
#else
        Task.CompletedTask;
#endif

    // Tab routes ("//goals") and back-nav ("..") pass through unchanged.
    //
    // Sub-routes whose first segment is a TabBar route (journal/entry, goals/entry, todos/entry)
    // resolve with the "///" absolute prefix because the tab page sits beneath them on the stack.
    //
    // Standalone global routes with NO tab beneath them (reminders, settings) must NOT use the
    // absolute prefix: absolute routing to a global route that would be the only page on the stack
    // throws ("Global routes currently cannot be the only page on the stack") and hard-crashes the
    // app (Google Play "crashes after opening"). Navigate to them RELATIVELY instead, which pushes
    // the page onto the current tab's navigation stack (back-nav via "..").
    public static string AbsoluteRoute(string route)
    {
        if (route == ".." || route.StartsWith("//"))
            return route;
        if (route.Contains('/'))
            return "///" + route;
        return route;
    }

    public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel) =>
#if ANDROID || IOS || MACCATALYST || WINDOWS
        Shell.Current.DisplayAlert(title, message, accept, cancel);
#else
        Task.FromResult(false);
#endif

    public Task AlertAsync(string title, string message, string cancel) =>
#if ANDROID || IOS || MACCATALYST || WINDOWS
        Shell.Current.DisplayAlert(title, message, cancel);
#else
        Task.CompletedTask;
#endif

    public Task<string?> DisplayPromptAsync(string title, string message, string accept, string cancel, string placeholder, int maxLength) =>
#if ANDROID || IOS || MACCATALYST || WINDOWS
        Shell.Current.DisplayPromptAsync(title, message, accept: accept, cancel: cancel,
            placeholder: placeholder, maxLength: maxLength, keyboard: Keyboard.Text);
#else
        Task.FromResult<string?>(null);
#endif

    public Task<string?> DisplayActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons) =>
#if ANDROID || IOS || MACCATALYST || WINDOWS
        Shell.Current.DisplayActionSheet(title, cancel, destruction, buttons)!;
#else
        Task.FromResult<string?>(null);
#endif
}
