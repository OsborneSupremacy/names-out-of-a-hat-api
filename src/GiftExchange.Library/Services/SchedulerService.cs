using Amazon.Scheduler;
using Amazon.Scheduler.Model;

namespace GiftExchange.Library.Services;

internal class SchedulerService : ISchedulerService
{
    private readonly ILogger<SchedulerService> _logger;

    private readonly JsonService _jsonService;

    private readonly IAmazonScheduler _schedulerClient;

    private readonly string _cooledOffSchedulerTargetArn;

    private readonly string _cooledOffSchedulerRoleArn;

    private readonly string _cooledOffSchedulerGroupName;

    public SchedulerService(
        ILogger<SchedulerService> logger,
        JsonService jsonService,
        IAmazonScheduler schedulerClient
    )
    {
        _logger =  logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonService = jsonService ?? throw new ArgumentNullException(nameof(jsonService));
        _schedulerClient = schedulerClient ?? throw new ArgumentNullException(nameof(schedulerClient));
        _cooledOffSchedulerTargetArn = EnvReader.GetStringValue("COOLED_OFF_SCHEDULER_TARGET_ARN");
        _cooledOffSchedulerRoleArn = EnvReader.GetStringValue("COOLED_OFF_SCHEDULER_ROLE_ARN");
        _cooledOffSchedulerGroupName = EnvReader.GetStringValue("COOLED_OFF_SCHEDULER_GROUP_NAME");
    }

    public async Task CreateCooledOffScheduleAsync(
        SendInvitationsRequest request,
        DateTimeOffset invitationsQueuedAt
        )
    {
        var scheduleName = $"hat-cooled-off-{request.HatId:N}";
        var scheduleExpression = $"at({invitationsQueuedAt.UtcDateTime.AddMinutes(1):yyyy-MM-ddTHH:mm:ss})";
        var payload = _jsonService.SerializeDefault(new HatCooledOffScheduleRequest
        {
            OrganizerEmail = request.OrganizerEmail,
            HatId = request.HatId
        });

        var createRequest = new CreateScheduleRequest
        {
            Name = scheduleName,
            GroupName = _cooledOffSchedulerGroupName,
            ActionAfterCompletion = ActionAfterCompletion.DELETE,
            FlexibleTimeWindow = new FlexibleTimeWindow
            {
                Mode = FlexibleTimeWindowMode.FLEXIBLE
            },
            ScheduleExpression = scheduleExpression,
            Target = new Target
            {
                Arn = _cooledOffSchedulerTargetArn,
                RoleArn = _cooledOffSchedulerRoleArn,
                Input = payload
            }
        };

        try
        {
            await _schedulerClient
                .CreateScheduleAsync(createRequest)
                .ConfigureAwait(false);
        } catch (Exception exception)
        {
            _logger
                .LogError(
                    exception,
                    "Failed to create cooled off schedule. Will not retry since we don't want to risk sending multiple invitations. Exception: {Exception}, ScheduleName: {ScheduleName}, GroupName: {GroupName}",
                    exception,
                    scheduleName,
                    _cooledOffSchedulerGroupName
                );
        };
    }
}
