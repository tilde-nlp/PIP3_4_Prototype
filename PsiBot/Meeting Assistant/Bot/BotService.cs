// <copyright file="BotService.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>

using Microsoft.Graph;
using Microsoft.Graph.Communications.Calls;
using Microsoft.Graph.Communications.Calls.Media;
using Microsoft.Graph.Communications.Client;
using Microsoft.Graph.Communications.Common;
using Microsoft.Graph.Communications.Common.Telemetry;
using Microsoft.Graph.Communications.Resources;
using Microsoft.Skype.Bots.Media;
using PsiBot.Model.Models;
using PsiBot.Services.Authentication;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Net;
using System.IO;
using System.Text;
using System.Runtime.Serialization.Json;
using PsiBot.Service.Settings;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using PsiBot.Model.Constants;
using System.Linq;
using Azure.Identity;
using Microsoft.AspNetCore.SignalR;
using Psibot.Service;
using PsiBot.Services.Logic;

namespace PsiBot.Services.Bot
{
    /// <summary>
    /// Class BotService.
    /// Implements the <see cref="System.IDisposable" />
    /// Implements the <see cref="PsiBot.Services.Contract.IBotService" />
    /// </summary>
    /// <seealso cref="System.IDisposable" />
    /// <seealso cref="PsiBot.Services.Contract.IBotService" />
    public class BotService : IDisposable, IBotService
    {
        /// <summary>
        /// The logger
        /// </summary>
        private readonly IGraphLogger _logger;

        /// <summary>
        /// The bot configuration.
        /// </summary>
        private readonly BotConfiguration botConfiguration;
        private readonly ASRConfiguration asrConfiguration;
        IHubContext<CaptionHub> captionHub;
        private GraphServiceClient graphClient;
        MeetingLogger meetingLogger;
        MachineTranslation translation;
        CaptionHubState hubState;
        public enum CallStatus
        {
            Joining,
            Leaving
        }
        public static ConcurrentDictionary<string, CallStatus> pendingCalls = new ConcurrentDictionary<string, CallStatus>();

        /// <summary>
        /// Bot meeting language for not invited bot
        /// </summary>
        public ConcurrentDictionary<string, string> OfflineBotLanguages { get; set; } = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// Gets the collection of call handlers.
        /// </summary>
        /// <value>The call handlers.</value>
        public ConcurrentDictionary<string, CallHandler> CallHandlers { get; } = new ConcurrentDictionary<string, CallHandler>();

        /// <summary>
        /// Gets the entry point for stateful bot.
        /// </summary>
        /// <value>The client.</value>
        public ICommunicationsClient Client { get; private set; }


        /// <inheritdoc />
        public void Dispose()
        {
            this.Client?.Dispose();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BotService" /> class.

        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="eventPublisher">The event publisher.</param>
        /// <param name="settings">The bot configuration.</param>
        public BotService(
            IGraphLogger logger,
            IOptions<BotConfiguration> botConfiguration,
            IOptions<ASRConfiguration> asrConfiguration,
            IHubContext<CaptionHub> captionHub,
            MeetingLogger meetingLogger,
            MachineTranslation translation,
            CaptionHubState hubState
        )
        {
            _logger = logger;
            this.botConfiguration = botConfiguration.Value;
            this.asrConfiguration = asrConfiguration.Value;
            this.captionHub = captionHub;
            this.meetingLogger = meetingLogger;
            this.translation = translation;
            this.hubState = hubState;
        }

        /// <summary>
        /// Initialize the instance.
        /// </summary>
        public void Initialize()
        {
            var name = this.GetType().Assembly.GetName().Name;
            var builder = new CommunicationsClientBuilder(
                name,
                botConfiguration.AadAppId,
                _logger);

            var authProvider = new AuthenticationProvider(
                name,
                botConfiguration.AadAppId,
                botConfiguration.AadAppSecret,
                _logger);

            builder.SetAuthenticationProvider(authProvider);
            builder.SetNotificationUrl(botConfiguration.CallControlBaseUrl);
            builder.SetMediaPlatformSettings(botConfiguration.MediaPlatformSettings);
            builder.SetServiceBaseUrl(botConfiguration.PlaceCallEndpointUrl);

            this.Client = builder.Build();
            this.Client.Calls().OnIncoming += this.CallsOnIncoming;
            this.Client.Calls().OnUpdated += this.CallsOnUpdated;

            var graphAuth = new ClientSecretCredential(botConfiguration.AadTenantId, botConfiguration.AadAppId, botConfiguration.AadAppSecret);
            graphClient = new GraphServiceClient(graphAuth, new[] { "https://graph.microsoft.com/.default" });            
        }

        /// <summary>
        /// End a particular call.
        /// </summary>
        /// <param name="callLegId">The call leg id.</param>
        /// <returns>The <see cref="Task" />.</returns>
        public async Task EndCallByCallLegIdAsync(string callLegId)
        {
            try
            {
                await this.GetHandlerOrThrow(callLegId).Call.DeleteAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Manually remove the call from SDK state.
                // This will trigger the ICallCollection.OnUpdated event with the removed resource.
                this.Client.Calls().TryForceRemove(callLegId, out ICall _);
            }
        }

        public async Task EndCallByThreadAsync(string thread)
        {
            string callLegId = null;
            try
            {
                if (pendingCalls.ContainsKey(thread))
                    return;
                if (!pendingCalls.TryAdd(thread, CallStatus.Joining))
                    return;
                await captionHub.Clients.Group(thread).SendAsync("status", new { status = "leaving" });
                callLegId = this.CallHandlers.First(x => x.Value.thread == thread).Key;
                // Remember meeting  language
                var asrLang = CallHandlers.First(x => x.Value.thread == thread).Value?.getAsrLang();
                SetOfflineBotLang(thread, asrLang);
                await this.GetHandlerOrThrow(callLegId).Call.DeleteAsync().ConfigureAwait(false);
                await captionHub.Clients.Group(thread).SendAsync("status", new { status = "" });
            }
            catch (Exception)
            {
                // Manually remove the call from SDK state.
                // This will trigger the ICallCollection.OnUpdated event with the removed resource.
                if (callLegId != null)                
                    this.Client.Calls().TryForceRemove(callLegId, out ICall _);                    
                else
                    await captionHub.Clients.Group(thread).SendAsync("status", new { status = "" }); //probably call already doesn't exist
            }
            finally
            {
                while (pendingCalls.ContainsKey(thread))
                {
                    pendingCalls.TryRemove(thread, out _);
                }
            }
        }

        /// <summary>
        /// Joins the call asynchronously.
        /// </summary>
        /// <param name="joinCallBody">The join call body.</param>
        /// <returns>The <see cref="ICall" /> that was requested to join.</returns>
        public async Task<ICall> JoinCallAsync(JoinCallBody joinCallBody)
        {
            try
            {
                
                // A tracking id for logging purposes. Helps identify this call in logs.
                var scenarioId = Guid.NewGuid();
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Joining call");
                ChatInfo chatInfo = null;
                OrganizerMeetingInfo meetingInfo = null;
                if (joinCallBody.JoinURL != null)
                {
                    (chatInfo, meetingInfo) = ParseJoinURL(joinCallBody.JoinURL);
                }
                else
                {
                    if (pendingCalls.ContainsKey(joinCallBody.Thread))
                        return null;
                    if (!pendingCalls.TryAdd(joinCallBody.Thread, CallStatus.Joining))
                        return null;
                    await captionHub.Clients.Group(joinCallBody.Thread).SendAsync("status", new { status = "joining" });
                    var thread = Encoding.UTF8.GetString(Convert.FromBase64String(joinCallBody.Thread));
                    var regex = new Regex("#(.*)#");
                    var match = regex.Match(thread);
                    chatInfo = new ChatInfo
                    {
                        ThreadId = match.Groups[1].Value,
                        MessageId = "0",
                        ReplyChainMessageId = null,
                    };

                    var graphChatInfo = await GraphLogic.getChatInfo(joinCallBody.Token, joinCallBody.Thread);

                    meetingInfo = new OrganizerMeetingInfo
                    {
                        Organizer = new IdentitySet
                        {
                            User = new Identity { Id = graphChatInfo.onlineMeetingInfo.organizer.id },
                        },
                    };
                    meetingInfo.Organizer.User.SetTenantId(joinCallBody.Tid);
                }

                var tenantId = (meetingInfo as OrganizerMeetingInfo).Organizer.GetPrimaryIdentity().GetTenantId();

                var mediaSession = this.CreateLocalMediaSession();

                var joinParams = new JoinMeetingParameters(chatInfo, meetingInfo, mediaSession)
                {
                    TenantId = tenantId,
                };

                if (!string.IsNullOrWhiteSpace(joinCallBody.DisplayName))
                {
                    // Teams client does not allow changing of ones own display name.
                    // If display name is specified, we join as anonymous (guest) user
                    // with the specified display name.  This will put bot into lobby
                    // unless lobby bypass is disabled.
                    joinParams.GuestIdentity = new Identity
                    {
                        Id = Guid.NewGuid().ToString(),
                        DisplayName = joinCallBody.DisplayName,
                    };
                }

                if (CallHandlers.Any(x => x.Value.thread == joinCallBody.Thread))
                {
                    Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} CallHandler with thread {joinCallBody.Thread} already exists - dropping");
                    //avoid most race conditions 
                    //should no longer be necessary with pendingCalls tracking
                    return null;
                }

                var statefulCall = await this.Client.Calls().AddAsync(joinParams, scenarioId).ConfigureAwait(false);

                //moved here from Calls event handler to access cartUrl
                var transcript = meetingLogger.getTranscript(joinCallBody.Thread);
                transcript.checkAndReset();
                var callHandler = new CallHandler(statefulCall, botConfiguration, asrConfiguration, joinCallBody.CartURL, graphClient, joinCallBody.Thread, captionHub, transcript, translation, hubState, joinCallBody.AsrLang);
                if (!string.IsNullOrWhiteSpace(joinCallBody.AsrLang))
                    callHandler.setAsrLang(joinCallBody.AsrLang);
                this.CallHandlers[statefulCall.Id] = callHandler;

                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Call creation complete: {statefulCall.Id} {joinCallBody.Thread}");
                if (!string.IsNullOrWhiteSpace(joinCallBody.Thread)) //in case joining by URL for debug purposes
                    await captionHub.Clients.Group(joinCallBody.Thread).SendAsync("status", new { status = "joined" });
                return statefulCall;
            }
            catch (Exception e)
            {
                await captionHub.Clients.Group(joinCallBody.Thread).SendAsync("status", new { status = "" });
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Error on call creation: (Processor Count = {Environment.ProcessorCount.ToString()}  {e.Message + (e.InnerException?.Message ?? "") + e.StackTrace }");
                _logger.Log(System.Diagnostics.TraceLevel.Error, e.Message);
                throw;
            }
            finally
            {
                while (pendingCalls.ContainsKey(joinCallBody.Thread))
                {
                    pendingCalls.TryRemove(joinCallBody.Thread, out _);
                }
            }
        }

        /// <summary>
        /// Creates the local media session.
        /// </summary>
        /// <param name="mediaSessionId">The media session identifier.
        /// This should be a unique value for each call.</param>
        /// <returns>The <see cref="ILocalMediaSession" />.</returns>
        private ILocalMediaSession CreateLocalMediaSession(Guid mediaSessionId = default)
        {
            try
            {
                // create media session object, this is needed to establish call connections
                return this.Client.CreateMediaSession(
                    new AudioSocketSettings
                    {
                        StreamDirections = StreamDirection.Sendrecv,
                        // Note! Currently, the only audio format supported when receiving unmixed audio is Pcm16K
                        SupportedAudioFormat = AudioFormat.Pcm16K,
                        ReceiveUnmixedMeetingAudio = true //get the extra buffers for the speakers
                    },
                    default(VideoSocketSettings),
                    default(VideoSocketSettings),
                    mediaSessionId: mediaSessionId);
            }
            catch (Exception e)
            {
                _logger.Log(System.Diagnostics.TraceLevel.Error, e.Message);
                throw;
            }
        }

        /// <summary>
        /// Incoming call handler.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="args">The <see cref="CollectionEventArgs{TResource}" /> instance containing the event data.</param>
        private void CallsOnIncoming(ICallCollection sender, CollectionEventArgs<ICall> args)
        {
            args.AddedResources.ForEach(call =>
            {
                // The context associated with the incoming call.
                IncomingContext incomingContext =
                    call.Resource.IncomingContext;

                // The RP participant.
                string observedParticipantId =
                    incomingContext.ObservedParticipantId;

                // If the observed participant is a delegate.
                IdentitySet onBehalfOfIdentity =
                    incomingContext.OnBehalfOf;

                // If a transfer occured, the transferor.
                IdentitySet transferorIdentity =
                    incomingContext.Transferor;

                string countryCode = null;
                EndpointType? endpointType = null;

                // Note: this should always be true for CR calls.
                if (incomingContext.ObservedParticipantId == incomingContext.SourceParticipantId)
                {
                    // The dynamic location of the RP.
                    countryCode = call.Resource.Source.CountryCode;

                    // The type of endpoint being used.
                    endpointType = call.Resource.Source.EndpointType;
                }

                IMediaSession mediaSession = Guid.TryParse(call.Id, out Guid callId)
                    ? this.CreateLocalMediaSession(callId)
                    : this.CreateLocalMediaSession();

                // Answer call
                call?.AnswerAsync(mediaSession).ForgetAndLogExceptionAsync(
                    call.GraphLogger,
                    $"Answering call {call.Id} with scenario {call.ScenarioId}.");
            });
        }

        /// <summary>
        /// Updated call handler.
        /// </summary>
        /// <param name="sender">The <see cref="ICallCollection" /> sender.</param>
        /// <param name="args">The <see cref="CollectionEventArgs{ICall}" /> instance containing the event data.</param>
        private void CallsOnUpdated(ICallCollection sender, CollectionEventArgs<ICall> args)
        {
            foreach (var call in args.RemovedResources)
            {
                if (this.CallHandlers.TryRemove(call.Id, out CallHandler handler))
                {
                    handler.Dispose();
                }
            }
        }

        /// <summary>
        /// The get handler or throw.
        /// </summary>
        /// <param name="callLegId">The call leg id.</param>
        /// <returns>The <see cref="CallHandler" />.</returns>
        /// <exception cref="ArgumentException">call ({callLegId}) not found</exception>
        private CallHandler GetHandlerOrThrow(string callLegId)
        {
            if (!this.CallHandlers.TryGetValue(callLegId, out CallHandler handler))
            {
                throw new ArgumentException($"call ({callLegId}) not found");
            }

            return handler;
        }

        /// <summary>
        /// Parse Join URL into its components.
        /// </summary>
        /// <param name="joinURL">Join URL from Team's meeting body.</param>
        /// <returns>Parsed data.</returns>
        /// <exception cref="ArgumentException">Join URL cannot be null or empty: {joinURL} - joinURL</exception>
        /// <exception cref="ArgumentException">Join URL cannot be parsed: {joinURL} - joinURL</exception>
        /// <exception cref="ArgumentException">Join URL is invalid: missing Tid - joinURL</exception>
        private (ChatInfo, OrganizerMeetingInfo) ParseJoinURL(string joinURL)
        {
            if (string.IsNullOrEmpty(joinURL))
            {
                throw new ArgumentException($"Join URL cannot be null or empty: {joinURL}", nameof(joinURL));
            }

            var decodedURL = WebUtility.UrlDecode(joinURL);

            //// URL being needs to be in this format.
            //// https://teams.microsoft.com/l/meetup-join/19:cd9ce3da56624fe69c9d7cd026f9126d@thread.skype/1509579179399?context={"Tid":"72f988bf-86f1-41af-91ab-2d7cd011db47","Oid":"550fae72-d251-43ec-868c-373732c2704f","MessageId":"1536978844957"}

            var regex = new Regex("https://teams\\.microsoft\\.com.*/(?<thread>[^/]+)/(?<message>[^/]+)\\?context=(?<context>{.*})");
            var match = regex.Match(decodedURL);
            if (!match.Success)
            {
                throw new ArgumentException($"Join URL cannot be parsed: {joinURL}", nameof(joinURL));
            }

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(match.Groups["context"].Value)))
            {
                var ctxt = (Meeting)new DataContractJsonSerializer(typeof(Meeting)).ReadObject(stream);

                if (string.IsNullOrEmpty(ctxt.Tid))
                {
                    throw new ArgumentException("Join URL is invalid: missing Tid", nameof(joinURL));
                }

                var chatInfo = new ChatInfo
                {
                    ThreadId = match.Groups["thread"].Value,
                    MessageId = match.Groups["message"].Value,
                    ReplyChainMessageId = ctxt.MessageId,
                };

                var meetingInfo = new OrganizerMeetingInfo
                {
                    Organizer = new IdentitySet
                    {
                        User = new Identity { Id = ctxt.Oid },
                    },
                };
                meetingInfo.Organizer.User.SetTenantId(ctxt.Tid);

                return (chatInfo, meetingInfo);
            }
        }

        public void SetOfflineBotLang(string thread, string lang)
        {
            if (OfflineBotLanguages.Any(x => x.Key == thread))
            {
                var record = OfflineBotLanguages.First(x => x.Key == thread);
                OfflineBotLanguages.TryUpdate(thread, lang, record.Value);
            }
            else
            {
                OfflineBotLanguages.TryAdd(thread, lang);
            }
        }

        public string PopOfflineBotLang(string thread)
        {
            if (OfflineBotLanguages.Any(x => x.Key == thread))
            {
                OfflineBotLanguages.TryRemove(thread, out string result);
                return result;
            }
            else
            {
                return string.Empty;
            }

        }

        public string GetOfflineBotLang(string thread)
        {
            OfflineBotLanguages.TryGetValue(thread, out string result);
            return result;
        }
    }
}
