# Fonts

Place the font named by `Video:FontFileName` here (default `DejaVuSans.ttf`).

It is used for the **"Made with Nostalgia AI" watermark** burned into free-tier
videos. The worker copies it into each job's working directory so FFmpeg can
reference it as a bare relative filename — which avoids FFmpeg's filter-path
escaping rules, where a Windows drive colon has to be written `C\:/...`.

Bundling the font rather than relying on an installed system font is what makes
rendering identical on a Windows dev machine and a Linux container.

## Getting DejaVu Sans

- Download: <https://dejavu-fonts.github.io/> (public domain–style permissive licence)
- Debian/Ubuntu images: installed by `apt-get install fonts-dejavu-core` at
  `/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf`

Any TTF works — set `Video:FontFileName` to match.

## Behaviour when this folder is empty

Nothing breaks. The worker detects the missing font and composes the video
**without a watermark**, logging a warning. Captions are unaffected: they are
rendered by libass, which falls back to a system font when
`Video:SubtitleFontName` is not found.
