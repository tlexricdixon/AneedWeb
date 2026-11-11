# ANEED Static System — Media Universe and Distribution Canon

Last consolidated: 2026-11-11. Restored from prior project conversations, with the newest nighttime podcast plan incorporated. This is a **roadmap**, not a claim that these shows or automations are implemented.

## The strategic thread

ANEED is currently an independent radio **system** and a working demonstration of its growing music-intelligence database. The potential business is the depth, provenance, and utility of analyzed music data, rather than a claim that the station already has a listener business or a valuable licensed dataset. The media properties make the intelligence visible and interesting. The long-term ambition is broader independent distribution and possibly technology/licensing, while keeping the station's infrastructure portable and low cost.

## The distinct channels (DO NOT CONFLATE)

1. **Regular radio on SAM / Live365:** Exactly **one DJ** on air at a time. Occasional scheduled cross-talk is allowed around the top-of-hour Live365 ad/bumper. Do not put all eight talking over normal programming.
2. **Interactive character chat:** Personalities may interact with listeners and each other in controlled environments. Experimental group room is currently not a 24/7 public production chat.
3. **ANEED After Dark — nightly midnight show:** Aspirational **one-hour, unanimated** talk show with **all eight DJs**, plus Linq's occasional squeaks and Kathleen's translations. One approved **four-song emotional/narrative set** structures the discussion. The radio edition includes songs; the following morning a roughly **40-minute music-free conversation** can be published as a podcast. Record dialogue separately from music so the podcast edit does not require stripping licensed tracks from the program. Target runtime, not a guaranteed 40 minutes.
4. **Short animated tech explainers:** Separately, **4–8 minute** character-led animated episodes for TikTok and other video outlets, showing how the technology works: sound analysis, keys, tempo, production, lyric analysis, emotional classifications, and the mechanics of a fictional AI DJ world. These are **not** clips of the one-hour unanimated talk show by default. Can include PG-13 jokes, AI-apocalypse banter, and educational visual gags. Reusable 3D assets, character rigs, voices, captions, and exports are proposed production methods, not completed infrastructure.
5. **Recurring special programming:** Friday Night Sock Hop as a future mixed 12-inch dance program, with Traktor Pro / MIDI experimentation and SAM routing. Musical analyzer results may support beat grids, transitions, energy, and key matching. This is a **future show**, not working autonomous DJ automation.

## Episode and character revelation order

- First **introduce the eight women** as distinctive people, gradually, with Kathleen and Linq being the initial public-facing 'Meet the Staff' characters.
- All eight women are fictional cyberpunk characters sharing the **same original face/template** derived from Kathleen, but each was shaped in a different environment and eventually found the others at ANEED. Real inspiration is a private family connection; do not publicly expose the owner's source photo or personal backstory without explicit direction.
- Kathleen is the original, poised/mature midnight storyteller. Lexi Static carries more of the mischievous 'Tlex' childhood attitude. Vesper Thorn, Roxy Riot, Mara Vale, Sunny Knox, Arden Cross, and Nova Six retain the distinct established personas in `Djs/DjProfiles.cs`.
- Linq is introduced **as though he is Kathleen's pet hamster**; she doesn't label him as a pet. He is secretly the intelligent station infrastructure/systems architect, wheel-powered, squeaks only. Only Kathleen can understand him. They were created at the same moment and have a nearly telepathic 'twin' rapport that is conveyed through behavior, **never explicitly labeled or explained as twins**. Her avatar is subtly brighter because he favors her. These are deliberate mysteries, **not** upfront exposition.
- The later episode **“You Have Vertices, Not Rights”** uses wireframe/50% opacity/muting, character rendering, AI-rights jokes, and a behind-the-scenes educational lesson about 3D and fictional AI embodiments. Do **not** open the entire series with this reveal. The rendering/easter-egg explanation and Linq's technical importance come later, after audience knows the women.
- “Are you pondering what I’m pondering?” is a recurring Kathleen/Linq bit; answers can go charmingly sideways. Sound design squeaks should be restrained and mixed separately from Kathleen TTS.

## Content/production discipline

- The radio music catalog is SAM/Monster SQL for the website, with PostgreSQL and local compute used for analysis and approved publishing. Preserve SAM IDs across the pipeline.
- Keep analysis data provenance, algorithm versions, human approval, and facts separated from fictional DJ interpretation; the AI must not invent catalog availability or fabricate an analysis.
- Build the four-song narrative from approved analysis. Compose and review DJ dialogue, synthesize voices, mix radio version with music under appropriate distribution arrangements, and separately export a **music-free** podcast with clean dialogue transitions. Review copyright/lyrics and platform rules.
- No claim that full nightly production, eight voices, animation, podcast hosting, AI DJ MIDI control, or a 24-hour GPU pipeline is already live.
- Distribution can cross-promote: radio introduces voices; chat lets listeners meet them; podcast provides deeper ensemble stories; short animated videos make the underlying tech comprehensible and discoverable.
- Possible monetization (sponsors, data licensing, etc.) is strategy, not a current contract or revenue.
- Kathleen's first set of 24 evergreen station breaks and her voice generation remain the near-term implementation work; see `Djs/KATHLEEN-24-STATION-IDS.md`. Do not let long-term media roadmap interfere with delivering a functioning station ID rotation.

## Media mogul shorthand

**Radio is the front door. The database is the engine. Eight distinct AI personalities are the storytellers. The nightly talk show gives the stories depth. The 4–8-minute animations teach how the engine works. A podcast keeps those stories available without copyrighted songs.**
