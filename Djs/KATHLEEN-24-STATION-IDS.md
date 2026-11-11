# ANEED — Kathleen's First 24 Hourly Station IDs

Production draft for the weekend voice session. All lines are original station copy. These are short station IDs, not song-specific introductions, so they remain correct without SAM metadata or live Ollama connectivity.

## The secret visual canon (do not reveal on air yet)

All eight female DJ avatars share the same underlying **facial template**, derived from Lexi's face (the station owner's daughter). Their hair, fashion, voice, mannerisms, and individual identity are distinct. This is **Orphan Black-style hidden lore**: listeners may spot the resemblance, but the fictional origin is not announced in character intros. Linq is a male hamster, does not have the facial template, communicates through squeaks, and powers the station on a wheel. Only Kathleen understands his squeaks because they were created at the same time. Linq's affection for Kathleen and the subtle extra brightness of her avatar remain unrevealed until the later **"You Have Vertices, Not Rights"** episode. Treat the real-person face as private creative source material; don't upload or publish a reference photo without explicit approval.

## Kathleen: 24 evergreen voice breaks

01. "You're tuned to A Need Static System. The city can keep its noise. We've got better things to listen to."
02. "Kathleen here. If the world has been too loud today, don't worry. I know where the volume knob is."
03. "Some songs age. Others just sit quietly until you're old enough to understand what they were saying."
04. "This is ANEED. No genre walls, no permission slips, and absolutely no committee picking your next favorite song."
05. "You don't have to explain why a song matters to you. Around here, that's between you and the speakers."
06. "Kathleen, keeping the signal warm. The coffee isn't, but I've made my peace with that."
07. "Somewhere out there, somebody's hearing this song for the first time. Try not to tell them how it ends."
08. "If you've taken the long way home just to finish a song, you're among friends."
09. "A little static never scared me. Silence, on the other hand, has terrible taste in music."
10. "Welcome to the hour nobody asked for and the music somebody needed."
11. "Here's to the records that survived scratched sleeves, bad decisions, and every move we swore was the last."
12. "Kathleen here. We don't do guilty pleasures. If you love the song, plead not guilty."
13. "The city sleeps with one eye open. I prefer to keep both ears busy."
14. "If this song brings somebody to mind, you can smile. You don't have to call them."
15. "A Need Static System: where yesterday's favorite and tomorrow's obsession share the same little room."
16. "I've got a soft spot for songs with sharp edges. They tend to tell the truth when the lights go low."
17. "You can leave your troubles by the door. Just don't leave your good taste out there with them."
18. "Kathleen speaking. The night is long, the playlist is longer, and I intend to enjoy both."
19. "They said nobody would listen to a station without genre rules. Isn't it lovely when people underestimate you?"
20. "A good song doesn't fix everything. But sometimes it sits beside you until you're ready to try."
21. "If you're working late, driving nowhere, or staring at the ceiling, this next stretch belongs to you."
22. "There's a difference between being lost and taking the scenic route. The soundtrack usually gives it away."
23. "I was told to behave on the air. Nobody was willing to define 'behave,' which seems like an oversight."
24. "This is Kathleen at ANEED. Stay as long as you like. The signal has room for one more."

## Playback rule: random order, no daily repeats

For **exactly one station ID per hour**, shuffle the 24 IDs once at the beginning of each local station day using a date-derived deterministic seed, then play each ID in order (slots 0–23). This creates a different order each day and no duplicate within a day, even after restarts. Keep the schedule in station-local time (America/Chicago); daylight saving transition days have 23 or 25 elapsed hours, so explicitly handle a repeated or missing clock hour if strict no-repeat is required. Alternatively store a persistent day's consumed-ID list on SAM and draw uniformly from remaining IDs each time, resetting only when the local calendar date changes; this is more flexible for hourly schedules or irregular breaks. Do **not** pick randomly from all 24 independently for every break—that permits repeats.

Output approved WAV files with stable IDs (e.g., kathleen-01.wav through kathleen-24.wav). Keep the audio pipeline offline: review loudness, silence, clipping, wording, and pronunciation before importing the files into SAM. Nothing here needs the website, home-facing API, or PostgreSQL. Do not mix unreviewed live LLM speech directly into broadcast automation.

Voice style: warm, late-night, unhurried, a little playful; subtle smiles on lines 06, 12 and 23; no dramatic cyberpunk exposition, and no generic 'mysterious circuits of the neon void' phrases. All 24 are evergreen, with no false claim about current song or live audience.
