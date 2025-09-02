using System.Threading.Tasks;

namespace LabelPlus_Next.Update.Services;

public interface IMessageService
{
    Task ShowAsync(string message, string title);
    Task ShowOverlayAsync(string message, string title);
}
