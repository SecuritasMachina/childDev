namespace ChildDev.Mobile;

public static class TaskExtensions
{
    public static async void FireAndForget(this Task task)
    {
        try { await task; }
        catch { /* intentionally swallowed for fire-and-forget navigation triggers */ }
    }
}
