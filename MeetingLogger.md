# PIP3_2.3_Prototype
This is the prototype (open-source software) created in activity 2.3 of the project "AI Assistant for Multilingual Meeting Management" (No. of the Contract/Agreement: 1.1.1.1/19/A/082)

All functionality of Meeting Protocol Generation is implemented in file [MeetingLogger.cs](PsiBot/Meeting%20Assistant/Bot/MeetingLogger.cs) and it is included in the Updated Prototype of a Smart Multilingual Meeting Manager.

The MeetingLogger class is used by the SMMM as a singleton and holds all transcripts in a dictionary referenced by the thread id that is assigned by Teams. It has methods to retrieve the transcript in either full or viewmodel form, as well as the ensureTranslation() method to asynchronously translate a transcript to another language (subject to configured Machine Translation functionality) for catching up to real time display in case of a new language being requested within a meeting.

Transcript class represents the transcript of one meeting and holds data on participants and messages transcribed, as well as items identified as decisions and tasks (subject to Natural Language Processing capabilities of configured bot).
It contains methods for adding messages, tasks, decisions, participants.

checkAndReset() method is used to identify a new meeting, in case of repeated and/or channel meetings, as these share the same thread id. Any pause longer than two hours is considered to be a new meeting and the previous data is wiped. Only exported protocols are stored. Normally a meeting protocol is exported when the meeting ends and everyone leaves. checkAndReset() is normally called at the start of a meeting, but if anyone remains in the meeting for more than two hours without anything being said by anyone, there is a risk that the protocol might be lost before being properly exported, for example on a quick reconnection of the bot. This has not been practically observed so far, and is not currently considered a priority for functional improvements.

save() method creates a .docx file with the transcript for long term storage and serving on demand. Language used is the language set by the person adding SMMM to the meeting and is stored in the culture property.  
The transcript is based on the [template file](PsiBot/Meeting%20Assistant/Templates/MeetingNotesTemplate.docx) provided with the solution and relies on a C# port of Apache POI to manipulate it.  
The method runs iterates through paragraphs in the template looking for placeholders for the various elements:  
1. {ph_transcription} - transcript of the meeting
2. {ph_decisions} - items identified as decisions by bot
3. {ph_tasks} - items identified as tasks by bot
4. {ph_participants} - participants
5. {ph_nextmeetingdate} - next meeting date if set on the transcript
6. {ph_listofparticipants} - localized title for list of participants
7. {ph_meetingnotes} - localized title for the whole protocol
8. {ph_decisionstitle} - localized title for decisions section
9. {ph_taskstitle} - localized title for tasks section
10. {ph_nextmeetingtitle} - localized label for next meeting
11. {ph_transcriptiontitle} - localized title for transcript section
12. {ph_date} - localized date of meeting

If any of the placeholders is missing, the corresponding data is omitted.  
Deicisions, tasks and next meeting date are inserted as passed to MeetingLonger.  
Transcript is in the form NAME HH:mm:ss TEXT with each entry in a new line. One speaker can have multiple consecutive entries subject to breaks inserted by the Automatic Speech Recognition service. In this case name and time are not repeatedly printed and only the time of the initial line is shown.

The prepared protocol is saved in configured transcript folder named according the meeting topic (or "Meeting", if unavailable) in the form TOPIC_yyyyMMddhhmmss. File extension is omitted as these are intended to be served through a special endpoint, but functionally these are .docx files.
