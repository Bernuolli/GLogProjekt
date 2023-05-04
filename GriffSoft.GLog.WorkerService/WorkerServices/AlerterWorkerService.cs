using GriffSoft.GLog.WorkerService.Logic.Services.Contracts;
using GriffSoft.GLog.WorkerService.Shared.Extensions;
using GriffSoft.GLog.WorkerService.Shared.Models;
using Microsoft.Extensions.Options;

namespace GriffSoft.GLog.WorkerService.WorkerServices
{
    public class AlerterWorkerService : BackgroundService
    {
        private readonly ILogger<AlerterWorkerService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IGLogWorkerErrorHelper _errorHelper;
        private readonly int _minutesDelay = 3;
        private readonly GLogConfig _config;

        public AlerterWorkerService( ILogger<AlerterWorkerService> logger, IOptions<GLogConfig> config, IServiceScopeFactory scopeFactory, IGLogWorkerErrorHelper errorHelper )
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _errorHelper = errorHelper;
            _config = config.Value;
        }

        protected override async Task ExecuteAsync( CancellationToken stoppingToken )
        {
            while( !stoppingToken.IsCancellationRequested )
            {
                try
                {
                    await RunAlertFactory();
                    await Task.Delay( TimeSpan.FromMinutes( _minutesDelay ), stoppingToken );
                }
                catch( Exception ex )
                {
                    _errorHelper.AddError( out TimeSpan nextTryTimeSpan, out string errorPrefix );
                    _logger.LogError( ex, errorPrefix + ex.Message );
                    await Task.Delay( nextTryTimeSpan, stoppingToken );
                    continue;
                }
                await RunAlertFactory();
                await Task.Delay( TimeSpan.FromMinutes( _minutesDelay ), stoppingToken );
            }
        }

        private async Task RunAlertFactory()
        {
            _logger.LogDebugWithLevelCheck( $"{nameof( RunAlertFactory )} is started" );

            using var scope = _scopeFactory.CreateScope();

            var alertFactory = scope.ServiceProvider.GetRequiredService<IGlogAlertFactory>();
            await alertFactory.CreateAlerts(
                TimeSpan.FromMinutes(_minutesDelay),
                TimeSpan.FromMinutes(_config.AlertRunTimeoutInMinutes)
            );

            _logger.LogDebugWithLevelCheck( $"{nameof( RunAlertFactory )} is finished" );
        }
    }
}