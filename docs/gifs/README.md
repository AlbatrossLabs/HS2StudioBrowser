# Recordings for the README

| Section | File | Status |
|---|---|---|
| Overview | `overview.gif` / `.webp` | not recorded (commented out in the README) |
| Outfits | `OutFitBrowser.gif` | in |
| Characters | `CharactersBrowser.webp` | in (converted from the 15 MB GIF) |
| Scenes | `SceneBrowser.gif` | in |
| Poses | `PoseBrowser.webp` | in (converted from the 55 MB GIF) |
| Animations | `AnimBrowser.webp` | in (converted from the 81 MB GIF) |
| Items | `items.gif` | not recorded (commented out) |
| Tags | `tags.gif` | not recorded (commented out) |

The original large GIFs stay on disk but are kept out of git (`.gitignore`): GitHub warns above 50 MB and refuses above 100 MB, and a README full of them loads slowly.

Converting a GIF to animated WebP (about 20x smaller here, same look):

```
ffmpeg -i Input.gif -vf "fps=15,scale=960:-2:flags=lanczos" -c:v libwebp_anim -lossless 0 -q:v 70 -compression_level 4 -loop 0 Output.webp
```

Keep recordings to 5-10 seconds and aim for under 5 MB each.
