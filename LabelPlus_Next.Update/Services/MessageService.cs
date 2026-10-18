using System.Threading.Tasks;
using Ursa.Controls;

namespace LabelPlus_Next.Update.Services;

public sealed class MessageService : IMessageService
{
    public Task ShowAsync(string message, string title) => MessageBox.ShowAsync(message, title);
    public async Task ShowOverlayAsync(string message, string title) => await OverlayMessageBox.ShowAsync(message, title).ConfigureAwait(false);
}
