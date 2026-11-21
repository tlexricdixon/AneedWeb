# CLAP-grounded DJ rehearsal (development only)

AneedWeb already reads the current song's **SAM song ID** from the SAM history
JSON uploaded to the Utah site. The CLAP analysis is authoritative in
**SAM PostgreSQL**, not Utah SQL. This slice introduces a separate, optional
read-only export that connects the two **without opening any connection from
Utah to SAM or rerunning CLAP**.

## What works when a real export is provided

- `/studio` retains its current purple-light three-turn rehearsal.
- The new **Rehearse with current song + CLAP** button reads the current SAM
  song from `BroadcastService` and looks up a curated audio description by
  `samSongId`. The existing eight character prompts are unchanged.
- Each DJ receives the same grounded music brief and the recent discussion,
  with directions to contribute an independent viewpoint, not a copied metaphor.
- If history or a matching verified CLAP export isn't present, the button
  displays an error and **does not call OpenAI**.
- Production DJ generation remains disabled.

## Export contract

Place a JSON array at `Data/dj-clap-context.json` in the running app's
content root. Each entry uses this shape:

```json
[
  {
    "samSongId": 123,
    "aneedId": "00000000-0000-0000-0000-000000000001",
    "descriptions": [
      "A prominent electric guitar over steady rock drums",
      "A mid-tempo rock arrangement with a clear vocal lead"
    ]
  }
]
```

**The values above are illustrative, not actual ANEED analysis results.**
Generate entries only from existing verified SAM PostgreSQL records, using
the real ANEED UUID ↔ SAM song ID crosswalk and meaningful CLAP-derived
audio descriptions. Do not manufacture metadata, use artist/title as the
primary join key, or copy raw vector embeddings into this file.

The file is optional and deliberately not committed to GitHub. The consumer
limits size and the number/length of descriptors included in prompts. It does
not perform analysis and does not make factual claims about lyrics or emotions.

## Remaining integration

1. Inspect the actual PostgreSQL schemas of `music.inventory` and
   `music.audio_embeddings`, including the representation of
   `top_descriptions` and the real SAM ID crosswalk.
2. Implement a trusted, repeatable **SAM-side export** using that verified
   schema. Copy the output to the development web app for rehearsal.
3. Compare side-by-side runs: same song, same DJs, with and without CLAP.
4. Later: feed song-centric event context, persistent character memories and
   music analysis into a durable dialogue pipeline; deploy only after proper
   authentication, budget control and input handling.

No schema migrations or SQL writes. No access to the SAM machine from Utah.
