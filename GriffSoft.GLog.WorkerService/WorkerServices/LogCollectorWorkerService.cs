using GriffSoft.Forras.DataAccess.Install.Entities.Log;
using GriffSoft.GLog.DataAccess.DbInitializer;
using GriffSoft.GLog.WorkerService.Logic.Contracts;
using GriffSoft.GLog.WorkerService.Logic.Forras.Services.Contracts;
using GriffSoft.GLog.WorkerService.Logic.Forras.Types;
using GriffSoft.GLog.WorkerService.Logic.Services.Contracts;
using GriffSoft.GLog.WorkerService.Shared.Models;
using Microsoft.Extensions.Options;

namespace GriffSoft.GLog.WorkerService.WorkerServices;

public class LogCollectorWorkerService : BackgroundService
{
    private readonly ILogger<LogCollectorWorkerService> _logger;
    private readonly IDbInitializer _dbInitializer;
    IGLogWorkerErrorHelper _errorHelper;
    private readonly GLogConfig _config;
    readonly int _millisecondsDelay = 5000;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public LogCollectorWorkerService(ILogger<LogCollectorWorkerService> logger, IOptions<GLogConfig> config,
        IDbInitializer dbInitializer, IGLogWorkerErrorHelper errorHelper, IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;        
        _dbInitializer = dbInitializer;
        _config = config.Value;
        _errorHelper = errorHelper;
        _serviceScopeFactory= serviceScopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if( _dbInitializer.IsInitialized )
                {
                    await using var scope = _serviceScopeFactory.CreateAsyncScope();
                    var forrasLogCollector = scope.ServiceProvider.GetRequiredService<IForrasLogCollector>();
                    (List<GLogBrokerData> gLogBrokerDataList, FUVLogCheckoutData? fuvLogCheckoutData) =  await forrasLogCollector.CheckoutLogDataAsync( stoppingToken );                    
                    var fuvGLogHelper = scope.ServiceProvider.GetRequiredService<IFUVGLogHelper>();
                    await forrasLogCollector.ProcessLogDataAsync( stoppingToken, gLogBrokerDataList, fuvLogCheckoutData );
                }
                await Task.Delay( _millisecondsDelay, stoppingToken );
            }
            catch(Exception ex)
            {
                _errorHelper.AddError( out TimeSpan nextTryTimeSpan, out string errorPrefix );
                _logger.LogError( ex, errorPrefix + ex.Message );
                await Task.Delay( nextTryTimeSpan, stoppingToken );
                continue;
            }
        }
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Worker with id of {UniqueId} starting at: {time}", _config.UniqueId,
                DateTimeOffset.Now);
        }

        await ResetLogState();
        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Worker with id of {UniqueId} stopping at: {time}", _config.UniqueId,
                DateTimeOffset.Now);
        }

        await base.StopAsync(cancellationToken);
    }

    public async Task ResetLogState()
    {
        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var forrasLogCollector = scope.ServiceProvider.GetRequiredService<IForrasLogCollector>();
            await forrasLogCollector.ResetLogsWithStuckState();
        }
        catch (Exception ex)
        {
            _logger.LogError( "Error while resetting logs state.{0}", ex.Message );
        }
    }
}