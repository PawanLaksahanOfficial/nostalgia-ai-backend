# Music loops

The video pipeline picks a background loop from this folder based on the memory's
`MusicMood`. Expected filenames (any of `.mp3`, `.m4a`, `.ogg`, `.wav`):

| File | Mood |
|---|---|
| `warm.mp3` | `warm` — the default, also used when a mood has no file of its own |
| `melancholy.mp3` | `melancholy` |
| `hopeful.mp3` | `hopeful` |
| `playful.mp3` | `playful` |

The four tracks below are committed, so every deploy has music. Each is a public-domain
recording, trimmed to its first 70 seconds (longer than the 60-second maximum video, so it
never loops) with a 1s fade-in, a 4s fade-out and loudness normalised to -18 LUFS.

## Requirements

- **Licensing must permit commercial redistribution inside a rendered video.**
  CC0 / public domain is the safe choice; anything CC-BY needs attribution added
  here *and* somewhere user-visible.
- 30–60 seconds is plenty. The loop is repeated with `-stream_loop -1` to cover
  the whole clip, so it should loop without an obvious seam.
- Quiet, sparse instrumentals work best — the track is mixed underneath the
  narration at `Video:MusicVolume` (0.18 by default) and should not compete with it.

Good sources of public-domain recordings: [Wikimedia Commons](https://commons.wikimedia.org/)
(check each file's licence) and [Musopen](https://musopen.org/). Anything CC-BY needs
attribution added here *and* somewhere user-visible.

## Behaviour when this folder is empty

Nothing breaks. `BundledMusicProvider` logs a single warning and returns no track;
videos are composed with narration and captions only.

## Attributions

All four are marked **Public domain** on their Wikimedia Commons pages (checked 2026-10-05).
No attribution is required; it is recorded here so the source can be re-checked.

| File | Piece | Performer | Source |
|---|---|---|---|
| `warm.mp3` | Erik Satie, *Gymnopédie No. 1* (guitar arrangement) | Michael Laucke | [Commons](https://commons.wikimedia.org/wiki/File:Satie_Gymnopedie_No_1_performed_by_Michael_Laucke.flac) |
| `melancholy.mp3` | Robert Schumann, *Scenes from Childhood*, Op. 15 No. 7 "Dreaming" (Träumerei) | Musopen | [Commons](https://commons.wikimedia.org/wiki/File:Robert_Schumann_-_scenes_from_childhood,_op._15_-_vii._dreaming.ogg) |
| `hopeful.mp3` | Edvard Grieg, *Peer Gynt* Suite No. 1, "Morning Mood" | Musopen Symphony Orchestra (Czech National Symphony Orchestra) | [Commons](https://commons.wikimedia.org/wiki/File:Musopen_-_Morning.ogg) |
| `playful.mp3` | Scott Joplin, *Maple Leaf Rag* | Pracchia-78 (synthesised Yamaha CF3 piano) | [Commons](https://commons.wikimedia.org/wiki/File:Scott_Joplin_-_Maple_Leaf_Rag.ogg) |

