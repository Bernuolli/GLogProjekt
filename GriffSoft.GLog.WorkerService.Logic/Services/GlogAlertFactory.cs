using GriffSoft.GLog.DataAccess;
using GriffSoft.GLog.DataAccess.LogDb;
using GriffSoft.GLog.Shared.Functions.AlertingEvents;
using GriffSoft.GLog.WorkerService.Logic.AlertScheduler;
using GriffSoft.GLog.WorkerService.Logic.Services.Contracts;
using GriffSoft.GLog.WorkerService.Shared.Extensions;
using GriffSoft.Shared.Forras.Contracts.Data;
using GriffSoft.Shared.Forras.Contracts.ForrasInstallCore;
using GriffSoft.Shared.Forras.Types.Settings;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GriffSoft.GLog.WorkerService.Logic.Services
{
    public class GlogAlertFactory : IGlogAlertFactory
    {
        private readonly ILogger<GlogAlertFactory> _logger;
        private readonly IGriffDbContextFactory<GLogContext> _gLogContextFactory;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IForrasInstallCoreService _forrasInstallCoreService;
        private readonly ForrasSettingsModel _forrasSettingsModel;

        public GlogAlertFactory( ILogger<GlogAlertFactory> logger, IGriffDbContextFactory<GLogContext> gLogContextFactory,
            IServiceScopeFactory serviceScopeFactory, IForrasInstallCoreService forrasInstallCoreService, IOptions<ForrasSettingsModel> forrasSettingsModel)
        {
            _logger = logger;
            _gLogContextFactory = gLogContextFactory;
            _serviceScopeFactory = serviceScopeFactory;
            _forrasInstallCoreService = forrasInstallCoreService;
            _forrasSettingsModel = forrasSettingsModel.Value;
        }

        public async Task CreateAlerts(TimeSpan timeSpanUntilNextRun, TimeSpan alertRunTimeoutInMinutes)
        {
            await using var context = await _gLogContextFactory.CreateDbContextAsync();

            List<GLogAlertingRule> list = await context.GLogAlertingRules.Where(a => a.IsActive).ToListAsync();

            if (list.Any())
            {
                GLogTaskScheduler scheduler = new GLogTaskScheduler();

                foreach (GLogAlertingRule alertingRule in list.Where(w => !w.ActualRunDate.HasValue || w.ActualRunDate.Value.Add(alertRunTimeoutInMinutes) <= DateTime.Now))
                {
                    var cron = Cronos.CronExpression.Parse(alertingRule.FrequencyCron);
                    DateTime nextAlertCreaterRun = DateTime.UtcNow.Add(timeSpanUntilNextRun);
                    List<DateTime> occurencesInTheNextRun = cron.GetOccurrences(DateTime.UtcNow, nextAlertCreaterRun, toInclusive: true).ToList();

                    if (occurencesInTheNextRun.Any())
                    {
                        alertingRule.ActualRunDate = DateTime.Now;
                        await context.SaveChangesAsync();
                        var occuranceDateTime = occurencesInTheNextRun.OrderBy(o => o).FirstOrDefault();
                        var currentDateTime = occuranceDateTime.ToLocalTime();
                        TimeSpan timeSpanUntilOccurence = occuranceDateTime - DateTime.UtcNow;
                        var taskFunc = CreateTaskFunc(alertingRule.Id);
                        scheduler.AddTask(timeSpanUntilOccurence, taskFunc);
                        _logger.LogDebugWithLevelCheck(
                            $"Alert létrehozva a következő {nameof(CreateAlerts)} futásig. ({currentDateTime} - {nextAlertCreaterRun})");
                    }
                }
            }            
        }

        private Func<Task> CreateTaskFunc(Guid alertingRuleId)
        {
            Func<Task> taskFunc = async () =>
            {
                var function = new AlertingEventGenerateFunction
                {
                    RuleId = alertingRuleId
                };

                await using var scope = _serviceScopeFactory.CreateAsyncScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                var result = await mediator.Send(function);

                if (result.HasError)
                {
                    _logger.LogError(result.AllErrors);
                }
                else
                {
                    if (result.NeedEmail)
                    {
                        await SendMail(result.EmailTo, result.EmailSubject, result.EmailBody);
                    }
                }
            };
            return taskFunc;
        }

        private async Task SendMail(string to, string subject, string body)
        {
            if(_forrasSettingsModel.Install is null )
            {
                throw new ArgumentNullException( $"{nameof(_forrasSettingsModel.Install)} nem lehet null." );
            }

            var dto = new SendMailRequestDto()
            {
                Email = new EmailDTO()
                {
                    ToEmails = to,
                    Subject = subject,
                    Body = body,
                    CreatedOn = DateTime.Now
                },
                Env = new LoginEnvironment()
                {
                    Install = _forrasSettingsModel.Install,
                    User = "",
                    Adopter = "",
                    Group = "",
                    Module = ""
                }
            };
            try
            {
                await _forrasInstallCoreService.SendMailAsync(dto, new CancellationToken());
            }
            catch (Exception ex)
            {

                _logger.LogError( ex, "Error in creating email." );
            }
        }
    }
}
