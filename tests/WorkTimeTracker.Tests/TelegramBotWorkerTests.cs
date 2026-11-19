using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WorkTimeTracker.Telegram;
using Xunit;

namespace WorkTimeTracker.Tests;

public class TelegramBotWorkerTests
{
    [Fact]
    public async Task Worker_WithoutToken_CompletesWithoutClientOrNetwork()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var worker = new TelegramBotWorker(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TelegramOptions()), NullLogger<TelegramBotWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
    }
}
