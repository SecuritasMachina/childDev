namespace LevelUp.Services;

public class MauiNavigationService : INavigationService
{
    public Task GoToAsync(string route) =>
#if ANDROID || IOS || MACCATALYST || WINDOWS
        Shell.Current.GoToAsync(route);
#else
        Task.CompletedTask;
#endif

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
