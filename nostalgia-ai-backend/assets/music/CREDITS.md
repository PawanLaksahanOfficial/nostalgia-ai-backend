# Music loops

The video pipeline picks a background loop from this folder based on the memory's
`MusicMood`. Expected filenames (any of `.mp3`, `.m4a`, `.ogg`, `.wav`):

| File | Mood |
|---|---|
| `warm.mp3` | `warm` — the default, also used when a mood has no file of its own |
| `melancholy.mp3` | `melancholy` |
| `hopeful.mp3` | `hopeful` |
| `playful.mp3` | `playful` |

**These files are not committed.** Add your own before deploying.

## Requirements

- **Licensing must permit commercial redistribution inside a rendered video.**
  CC0 / public domain is the safe choice; anything CC-BY needs attribution added
  here *and* somewhere user-visible.
- 30–60 seconds is plenty. The loop is repeated with `-stream_loop -1` to cover
  the whole clip, so it should loop without an obvious seam.
- Quiet, sparse instrumentals work best — the track is mixed underneath the
  narration at `Video:MusicVolume` (0.18 by default) and should not compete with it.

Suggested CC0 sources: [Pixabay Music](https://pixabay.com/music/),
[Free Music Archive (CC0)](https://freemusicarchive.org/), [Incompetech](https://incompetech.com/)
(CC-BY — requires attribution).

## Behaviour when this folder is empty

Nothing breaks. `BundledMusicProvider` logs a single warning and returns no track;
videos are composed with narration and captions only.

## Attributions

