using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PsiBot.Services.Controllers
{
    public class PushController : Controller
    {
        MeetingLogger logger { get; set; }

        public PushController(MeetingLogger _logger)
        {
            logger = _logger;
        }

        [Route("/Push/Decision")]
        public async Task<IActionResult> Decision(string thread, string text)
        {
            logger.getTranscript(thread).appendDecision(text);
            return Ok();
        }

        [Route("/Push/Task")]
        public async Task<IActionResult> Task(string thread, string text)
        {
            logger.getTranscript(thread).appendTask(text);
            return Ok();
        }

        [Route("/Push/Participant")]
        public async Task<IActionResult> Participant(string thread, string text)
        {
            logger.getTranscript(thread).addParticipant(text);
            return Ok();
        }

        [Route("/Push/NextMeeting")]
        public async Task<IActionResult> NextMeeting(string thread, string time)
        {
            logger.getTranscript(thread).setNextMeeting(time);
            return Ok();
        }
    }
}