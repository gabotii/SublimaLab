namespace SublimaLab.Blazor.Services;

public enum ToastKind
{
    Success,
    Error,
    Info,
}

public sealed record Toast(Guid Id, string Message, ToastKind Kind, string? ActionText, string? ActionHref);

/// <summary>Short, non-blocking notifications announced to screen readers via an aria-live region.</summary>
public sealed class ToastService
{
    private readonly List<Toast> _toasts = [];

    public IReadOnlyList<Toast> Toasts => _toasts;

    public event Action? Changed;

    public void Success(string message, string? actionText = null, string? actionHref = null) => Show(message, ToastKind.Success, actionText, actionHref);

    public void Error(string message) => Show(message, ToastKind.Error, null, null);

    public void Info(string message) => Show(message, ToastKind.Info, null, null);

    public void Dismiss(Guid id)
    {
        if (_toasts.RemoveAll(t => t.Id == id) > 0)
        {
            Changed?.Invoke();
        }
    }

    private void Show(string message, ToastKind kind, string? actionText, string? actionHref)
    {
        var toast = new Toast(Guid.NewGuid(), message, kind, actionText, actionHref);
        _toasts.Add(toast);
        if (_toasts.Count > 3)
        {
            _toasts.RemoveAt(0);
        }

        Changed?.Invoke();
        _ = DismissLaterAsync(toast.Id, kind == ToastKind.Error ? 7000 : 4500);
    }

    private async Task DismissLaterAsync(Guid id, int delayMs)
    {
        await Task.Delay(delayMs);
        Dismiss(id);
    }
}
