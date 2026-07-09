namespace ThreePl.Web.Services;

/// <summary>Circuit-scoped toast bus; the Home shell subscribes and renders.</summary>
public class ToastService
{
    public event Action<string, string>? OnToast;

    public void Success(string message) => OnToast?.Invoke("success", message);
    public void Error(string message) => OnToast?.Invoke("error", message);
}
