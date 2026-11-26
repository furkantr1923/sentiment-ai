namespace SentimentAI.App.Services;

/// <summary>ViewModel'lerin arayüze doğrudan bağımlı olmadan kullanıcıya soru sorabilmesi için.</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);
    Task AlertAsync(string title, string message, string ok = "Tamam");
}

public sealed class DialogService : IDialogService
{
    private static Page? CurrentPage
    {
        get
        {
            var root = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (root is NavigationPage nav)
            {
                if (nav.Navigation.ModalStack.Count > 0) return nav.Navigation.ModalStack[^1];
                return nav.CurrentPage ?? nav;
            }
            return root;
        }
    }

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = CurrentPage;
            return page is not null && await page.DisplayAlert(title, message, accept, cancel);
        });

    public Task AlertAsync(string title, string message, string ok = "Tamam") =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = CurrentPage;
            if (page is not null) await page.DisplayAlert(title, message, ok);
        });
}
