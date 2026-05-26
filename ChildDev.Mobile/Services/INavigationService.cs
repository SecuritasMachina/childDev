namespace LevelUp.Services;

public interface INavigationService
{
    Task GoToAsync(string route);
    Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel);
    Task AlertAsync(string title, string message, string cancel);
    Task<string?> DisplayPromptAsync(string title, string message, string accept, string cancel, string placeholder, int maxLength);
}
