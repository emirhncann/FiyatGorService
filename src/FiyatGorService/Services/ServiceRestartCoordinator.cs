namespace FiyatGorService.Services;

public sealed class ServiceRestartCoordinator
{
    private readonly ILogger<ServiceRestartCoordinator> _logger;

    public ServiceRestartCoordinator(ILogger<ServiceRestartCoordinator> logger)
    {
        _logger = logger;
    }

    public void ScheduleRestart(TimeSpan delay)
    {
        _ = Task.Run(async () =>
        {
            _logger.LogWarning("Application restart scheduled after port change. DelayMs={DelayMs}", delay.TotalMilliseconds);
            await Task.Delay(delay);
            Environment.Exit(0);
        });
    }
}
