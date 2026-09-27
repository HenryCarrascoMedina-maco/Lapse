namespace Lapse.Core.Alerts;

public interface INotifier
{
    string Channel { get; }

    bool CanDeliver(PlannedAlert alert);

    Task<Result<Unit>> SendAsync(PlannedAlert alert, CancellationToken cancellationToken);
}
