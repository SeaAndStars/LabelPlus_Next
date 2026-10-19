using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace LabelPlus_Next.Services;

/// <summary>Small UI-thread abstraction so view models can be exercised without Avalonia.</summary>
public interface IUiDispatcher
{
    bool CheckAccess();
    Task InvokeAsync(Action action);
    void Post(Action action);
}

[ExcludeFromCodeCoverage]
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();
    public async Task InvokeAsync(Action action) => await Dispatcher.UIThread.InvokeAsync(action);
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}

/// <summary>Runs synchronously on the calling thread; for unit tests and headless hosts.</summary>
public sealed class InlineUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => true;
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
    public void Post(Action action) => action();
}
