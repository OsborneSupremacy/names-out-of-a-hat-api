using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Scheduler;
using Amazon.SimpleEmail;
using Amazon.SimpleNotificationService;
using Amazon.SimpleSystemsManagement;
using Amazon.AuroraDsql.Npgsql;
using AWS.Lambda.Powertools.Tracing;
using GiftExchange.Library.Interceptors;
using Amazon.SQS;
using GiftExchange.Library.Validators;

namespace GiftExchange.Library.Builders;

internal static class ServiceProviderBuilder
{
    public static IServiceProvider Build()
    {
        // Global, static, and therefore here rather than in AddVendorServices: it installs a
        // handler into the AWS SDK's pipeline for every client constructed afterwards, so it has
        // to run before anything resolves one. Once per cold start, which is what this method is.
        //
        // Powertools does not do this as a side effect of the [Tracing] attribute -- the attribute
        // traces the decorated method and nothing else -- so without this line every AWS call the
        // handlers make would be invisible inside an otherwise traced invocation.
        //
        // Registering for all services rather than naming them keeps this from being another list
        // that has to be remembered when a client is added -- the kind of per-item bookkeeping the
        // LIVE_MODE comment in locals.tf describes going wrong.
        //
        // No guard on it. Powertools checks for itself whether it is running in Lambda and
        // disables tracing when it is not, which is why the hand-rolled check that used to be here
        // is gone: two authorities on the same question is one more than the question has.
        Tracing.RegisterForAllServices();

        // Named so it is distinguishable in a trace from the work it makes possible. This
        // measures registration and container construction only -- the expensive singletons are
        // registered lazily and resolve later, inside the request that first needs them, which is
        // exactly the distinction a cold start comment cannot make and a trace can.
        //
        // Assigned out of the closure because Powertools' WithSubsegment takes an Action rather
        // than a Func: there is no overload that returns the value the work produced.
        ServiceProvider? provider = null;
        Tracing.WithSubsegment("container-build", _ =>
            provider = new ServiceCollection()
                .AddUtilities()
                .AddVendorServices()
                .AddBusinessServices()
                .AddValidators()
                .BuildServiceProvider());

        return provider!;
    }

    extension(IServiceCollection services)
    {
        private IServiceCollection AddVendorServices()
        {
            var region = RegionEndpoint.GetBySystemName(EnvReader.GetStringValue("AWS_REGION"));
            return services
                .AddDefaultAWSOptions(new AWSOptions { Region = region })
                .AddAWSService<IAmazonDynamoDB>()
                .AddAWSService<IAmazonSQS>()
                .AddAWSService<IAmazonSimpleNotificationService>()
                .AddAWSService<IAmazonScheduler>()
                .AddAWSService<IAmazonComprehend>()
                .AddAWSService<IAmazonSimpleSystemsManagement>()
                .AddSingleton<IAmazonSimpleEmailService, AmazonSimpleEmailServiceClient>() // AddAWSService fails for SES
                // Measured, because this is the single largest thing a cold start does and the one
                // the memory_size on this function was raised to pay for: the connector signs an
                // IAM auth token and opens a verify-full TLS connection before the first query can
                // run. It is local work as far as the AWS SDK handler is concerned, so nothing
                // automatic sees it; without this subsegment it is an unattributed gap between the
                // invocation starting and the first statement being sent.
                .AddSingleton<DsqlDataSource>(_ =>
                {
                    DsqlDataSource? dataSource = null;
                    Tracing.WithSubsegment("dsql-datasource-create",
                        _ => dataSource = DsqlDataSourceProvider.Create());
                    return dataSource!;
                })
                // A factory rather than AddDbContext: this container has no scopes, so a scoped
                // DbContext would be resolved from the root and shared like a singleton. A
                // DbContext is not thread safe, so each unit of work gets its own.
                .AddDbContextFactory<GiftExchangeDbContext>((provider, options) =>
                    options.UseNpgsql(
                        // The connector wraps an NpgsqlDataSource and exposes it for exactly this.
                        provider.GetRequiredService<DsqlDataSource>().DataSource,
                        // DSQL reports write conflicts as serialization failures (SQLSTATE
                        // 40001), which Npgsql already classifies as transient.
                        npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(2), errorCodesToAdd: null))
                    // DSQL accepts only REPEATABLE READ, including for the transactions EF opens
                    // by itself around a multi-statement SaveChanges.
                    .AddInterceptors(new RepeatableReadTransactionInterceptor())
                    // What the AWS SDK handler cannot see. Every other call this function makes
                    // goes through an AWS client and is traced by the pipeline handler registered
                    // in Build; DSQL is reached over Postgres, so without this a trace shows the
                    // invocation's total time with the database work as an unexplained gap in it.
                    //
                    // false, so the SQL text is recorded without its parameter values. The
                    // parameters here are participant email addresses and hat ids, and a trace is
                    // a place they would be retained on terms nothing else in this application
                    // sets -- see the retention reasoning in cloudwatch-log-groups.tf for the same
                    // argument applied to logs.
                    .AddXRayInterceptor(false));
        }

        internal IServiceCollection AddUtilities()
        {
            JsonSerializerOptions options = new()
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                TypeInfoResolver = GiftExchangeJsonTypeInfoResolver.Default
            };
            return services
                .AddLogging(builder => builder.AddLambdaLogger())
                .AddSingleton(options)
                .AddSingleton<JsonService>()
                .AddSingleton<ApiGatewayAdapter>();
        }

        internal IServiceCollection AddValidators() =>
            services
                .AddSingleton<IValidator<AddParticipantRequest>, AddParticipantRequestValidator>()
                .AddSingleton<IValidator<AssignRecipientsRequest>, AssignRecipientsRequestValidator>()
                .AddSingleton<IValidator<UpdateProfileRequest>, UpdateProfileRequestValidator>()
                .AddSingleton<IValidator<RequestMagicLinkRequest>, RequestMagicLinkRequestValidator>()
                .AddSingleton<IValidator<RedeemMagicLinkRequest>, RedeemMagicLinkRequestValidator>()
                .AddSingleton<IValidator<CloseHatRequest>, CloseHatRequestValidator>()
                .AddSingleton<IValidator<GetHatsRequest>, GetHatsRequestValidator>()
                .AddSingleton<IValidator<CopyHatRequest>, CopyHatRequestValidator>()
                .AddSingleton<IValidator<CreateHatRequest>, CreateHatRequestValidator>()
                .AddSingleton<IValidator<DeleteMyDataRequest>, DeleteMyDataRequestValidator>()
                .AddSingleton<IValidator<EditHatRequest>, EditHatRequestValidator>()
                .AddSingleton<IValidator<EditParticipantRequest>, EditParticipantRequestValidator>()
                .AddSingleton<IValidator<EditParticipantEmojiRequest>, EditParticipantEmojiRequestValidator>()
                .AddSingleton<IValidator<EditParticipantNameRequest>, EditParticipantNameRequestValidator>()
                .AddSingleton<IValidator<PreviewInvitationsRequest>, PreviewInvitationsRequestValidator>()
                .AddSingleton<IValidator<ResetHatRequest>, ResetHatRequestValidator>()
                .AddSingleton<IValidator<SendInvitationsRequest>, SendInvitationsRequestValidator>()
                .AddSingleton<IValidator<EditParticipantAddressRequest>, EditParticipantAddressRequestValidator>()
                .AddSingleton<IValidator<SubmitFeedbackRequest>, SubmitFeedbackRequestValidator>()
                .AddSingleton<IValidator<ValidateHatRequest>, ValidateHatRequestValidator>();

        internal IServiceCollection AddBusinessServices() =>
            services
                .AddSingleton<GiftExchangeProvider>()
                .AddSingleton<LoginTokenProvider>()
                .AddSingleton<SigningSecretProvider>()
                .AddSingleton<SessionTokenService>()
                .AddSingleton<IContentModerationService, ContentModerationService>()
                .AddSingleton<HatPreconditionValidator>()
                .AddSingleton<HatCreationLimiter>()
                .AddSingleton<OrganizerStandingChecker>()

                .AddKeyedSingleton<IApiGatewayHandler, RequestMagicLinkService>("post/auth/requestlink")
                .AddKeyedSingleton<IApiGatewayHandler, RedeemMagicLinkService>("post/auth/redeem")

                // Two methods, one service. The GET renders a confirmation page and the POST
                // behind it performs the Ask, so that a mail scanner following the link in an
                // invitation cannot send it on somebody's behalf.
                .AddKeyedSingleton<IApiGatewayHandler, AskForGiftIdeasService>("get/ask/{token}")
                .AddKeyedSingleton<IApiGatewayHandler, AskForGiftIdeasService>("post/ask/{token}")

                // The same split again. The GET shows the form and the POST behind it shares what
                // was written, so a scanner following the button forwards nothing to anybody.
                .AddKeyedSingleton<IApiGatewayHandler, ShareGiftIdeasService>("get/ideas/{token}")
                .AddKeyedSingleton<IApiGatewayHandler, ShareGiftIdeasService>("post/ideas/{token}")

                // Offering ideas about somebody else, which is the same token on a different
                // resource: /ideas settles the subject from the token, and here the subject is
                // chosen on the page. A resource of its own rather than a mode of the service
                // above, because that one tells its two paths apart by which table the token
                // resolved in, and this is neither of them.
                .AddKeyedSingleton<IApiGatewayHandler, OfferGiftIdeasService>("get/offer/{token}")
                .AddKeyedSingleton<IApiGatewayHandler, OfferGiftIdeasService>("post/offer/{token}")

                // The same split, for the same reason, and here the stakes of getting it wrong are
                // higher: a GET that acted would remove somebody from an exchange because their
                // mail provider checked a link.
                .AddKeyedSingleton<IApiGatewayHandler, LeaveGiftExchangeService>("get/leave/{token}")
                .AddKeyedSingleton<IApiGatewayHandler, LeaveGiftExchangeService>("post/leave/{token}")

                .AddKeyedSingleton<IApiGatewayHandler, GetHatService>("get/hat/{email}/{id}")
                .AddKeyedSingleton<IApiGatewayHandler, ExportHatService>("get/hat/{email}/export/{id}")
                .AddKeyedSingleton<IApiGatewayHandler, GetHatsService>("get/hats/{email}")

                .AddKeyedSingleton<IApiGatewayHandler, CreateHatService>("post/hat")
                .AddKeyedSingleton<IApiGatewayHandler, EditHatService>("put/hat")

                .AddKeyedSingleton<IApiGatewayHandler, UpdateProfileService>("put/profile")

                // Accepts the request and queues the deleting, which has no upper bound on how long
                // it takes. DataDeletionQueueHandlerService does the work.
                .AddKeyedSingleton<IApiGatewayHandler, DeleteMyDataService>("delete/profile")

                // Authenticated, because the footer that opens the contact form only renders on
                // signed-in pages. That is what keeps this off the list of things needing a
                // CAPTCHA: there is no anonymous route to it, and the sender's address comes from
                // the session rather than from a box somebody can type anything into.
                .AddKeyedSingleton<IApiGatewayHandler, SubmitFeedbackService>("post/feedback")

                .AddKeyedSingleton<IApiGatewayHandler, DeleteHatService>("delete/hat")

                .AddKeyedSingleton<IApiGatewayHandler, AddParticipantService>("post/participant")
                .AddKeyedSingleton<IApiGatewayHandler, EditParticipantService>("put/participant")
                .AddKeyedSingleton<IApiGatewayHandler, GetParticipantService>("get/participant/{organizeremail}/{hatid}/{participantemail}")
                .AddKeyedSingleton<IApiGatewayHandler, RemoveParticipantService>("delete/participant")

                // Its own endpoint rather than part of put/participant, which edits eligibility
                // and resets the hat to IN_PROGRESS when it does — correct before the draw, and
                // ruinous after invitations have gone out.
                .AddKeyedSingleton<IApiGatewayHandler, EditParticipantAddressService>("put/participant/address")

                // And its own endpoint for the same reason, less dramatically: a change of face
                // should not un-shake the hat.
                .AddKeyedSingleton<IApiGatewayHandler, EditParticipantEmojiService>("put/participant/emoji")

                // And its own for the same reason again. A name is stored on the person and read
                // back into the domain records every time, so renaming somebody moves nothing the
                // draw is made of and must not un-shake the hat.
                .AddKeyedSingleton<IApiGatewayHandler, EditParticipantNameService>("put/participant/name")

                .AddKeyedSingleton<IApiGatewayHandler, ValidationService>("post/hat/validate")

                .AddKeyedSingleton<IApiGatewayHandler, AssignRecipientsService>("post/recipients")

                .AddKeyedSingleton<IApiGatewayHandler, PreviewInvitationsService>("get/hat/{email}/previewinvitations/{id}")
                .AddKeyedSingleton<IApiGatewayHandler, EnqueueInvitationsService>("post/hat/sendinvitations")
                .AddKeyedSingleton<IApiGatewayHandler, CloseHatService>("post/hat/close")
                .AddKeyedSingleton<IApiGatewayHandler, CopyHatService>("post/hat/copy")

                // Its own endpoint rather than a flavour of put/hat, which edits the details
                // an organizer typed. This throws away the eligibility rules and the draw, and
                // an edit that could do that by omission would be the wrong shape entirely.
                .AddKeyedSingleton<IApiGatewayHandler, ResetHatService>("post/hat/reset")

                .AddSingleton<ValidationService>() // registered separately for direct use
                .AddSingleton<EmailCompositionService>()
                .AddSingleton<CompletionEmailCompositionService>()
                .AddSingleton<GiftIdeaEmailCompositionService>()
                .AddSingleton<GiftIdeaContentPolicy>()
                .AddSingleton<AskQuestionPolicy>()
                .AddSingleton<IReplyThrottleProvider, ReplyThrottleProvider>()
                .AddSingleton<AutomaticEmailSender>()
                .AddSingleton<IEmailQueue, EmailQueue>()
                .AddSingleton<IDataDeletionQueue, DataDeletionQueue>()
                .AddSingleton<DataDeletionQueueHandlerService>()
                .AddSingleton<AskPageComposer>()
                .AddSingleton<ShareIdeasPageComposer>()
                .AddSingleton<OfferIdeasPageComposer>()
                .AddSingleton<LeavePageComposer>()
                .AddSingleton<LeaveEmailCompositionService>()
                .AddSingleton<DoNotAddService>()
                .AddSingleton<InvitationQueueHandlerService>()
                .AddSingleton<DeliveryEventsService>()
                .AddSingleton<UndeliverableInvitationsEmailCompositionService>()
                .AddSingleton<UndeliverableInvitationsService>()
                .AddSingleton<ClosePromptEmailCompositionService>()
                .AddSingleton<ExchangeDateSweepService>()
                .AddSingleton<ISchedulerService, SchedulerService>()
            ;
    }
}
