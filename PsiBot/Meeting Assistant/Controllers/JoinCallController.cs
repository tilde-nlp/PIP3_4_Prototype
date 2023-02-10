// <copyright file="JoinCallController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>

using PsiBot.Model.Constants;
using PsiBot.Model.Models;
using PsiBot.Service.Settings;
using PsiBot.Services.Bot;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Communications.Common.Telemetry;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using PsiBot.Services.Logic;

namespace PsiBot.Services.Controllers
{
    /// <summary>
    /// JoinCallController is a third-party controller (non-Bot Framework) that can be called in CVI scenario to trigger the bot to join a call.
    /// </summary>
    public class JoinCallController : ControllerBase
    {
        /// <summary>
        /// The logger
        /// </summary>
        private readonly IGraphLogger _logger;

        /// <summary>
        /// The bot service
        /// </summary>
        private readonly IBotService _botService;

        /// <summary>
        /// The bot configuration
        /// </summary>
        private readonly BotConfiguration botConfiguration;

        /// <summary>
        /// Initializes a new instance of the <see cref="JoinCallController" /> class.

        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="eventPublisher">The event publisher.</param>
        /// <param name="botService">The bot service.</param>
        /// <param name="settings">The bot configuration.</param>
        public JoinCallController(IBotService botService, IOptions<BotConfiguration> botConfiguration, IGraphLogger logger)
        {
            _logger = logger;
            _botService = botService;
            this.botConfiguration = botConfiguration.Value;
        }

        /// <summary>
        /// The join call async.
        /// </summary>
        /// <param name="joinCallBody">The join call body.</param>
        /// <returns>The <see cref="HttpResponseMessage" />.</returns>
        [HttpPost]
        [Route(HttpRouteConstants.JoinCall)]
        public async Task<IActionResult> JoinCallAsync([FromBody] JoinCallBody joinCallBody)
        {
            try
            {
                var lang = _botService.GetOfflineBotLang(joinCallBody.Thread);
                if (string.IsNullOrEmpty(joinCallBody.AsrLang))
                    joinCallBody.AsrLang = lang;
                var call = await _botService.JoinCallAsync(joinCallBody).ConfigureAwait(false);
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Joining call: {joinCallBody.Thread} {joinCallBody.Oid} {joinCallBody.Tid}");
                if (call == null)
                    return StatusCode(204);
                var callPath = $"/{HttpRouteConstants.CallRoute.Replace("{callLegId}", call.Id)}";
                var callUri = $"{botConfiguration.ServiceCname}{callPath}";

                var values = new JoinURLResponse()
                {
                    Call = callUri,
                    CallId = call.Id,
                    ScenarioId = call.ScenarioId,
                    Logs = callUri.Replace("/calls/", "/logs/")
                };

                GraphChat meetingInfo = null;
                if (!string.IsNullOrEmpty(joinCallBody.Token))
                {
                    meetingInfo = await GraphLogic.getChatInfo(joinCallBody.Token, joinCallBody.Thread);
                }

                var kvp = _botService.CallHandlers.FirstOrDefault(x => x.Value.thread == joinCallBody.Thread);
                if (kvp.Value != null)
                {
                    if (!string.IsNullOrEmpty(lang))
                        kvp.Value.setAsrLang(lang);
                    kvp.Value.setTimeZone(joinCallBody.TimeZone);
                    kvp.Value.setMeetingInfo(meetingInfo);
                }


                var json = JsonConvert.SerializeObject(values);

                return Ok(values);
            }
            catch (ServiceException e)
            {
                HttpResponseMessage response = (int)e.StatusCode >= 300
                    ? new HttpResponseMessage(e.StatusCode)
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError);

                if (e.ResponseHeaders != null)
                {
                    foreach (var responseHeader in e.ResponseHeaders)
                    {
                        response.Headers.TryAddWithoutValidation(responseHeader.Key, responseHeader.Value);
                    }
                }

                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} ServiceException on join call {e.Message} {e.StackTrace}");
                response.Content = new StringContent(e.ToString());
                return StatusCode(500, e.ToString());
            }
            catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Error on join call {e.Message} {e.StackTrace}");
                _logger.Error(e, $"Received HTTP {this.Request.Method}, {this.Request.Path.Value}");
                return StatusCode(500, e.Message);
            }
        }
    }
}
