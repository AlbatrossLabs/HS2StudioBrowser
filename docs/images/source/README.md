# Banner source

- `banner.html` lays out three images: `?layout=banner` (1600 x 400, the README banner), `?layout=social` (1280 x 640, GitHub's social preview) and `?layout=social32` (1200 x 800, a 3:2 post for Discord, itch and the like).
- `bunny.png` is the character cut out of a Camera Sweep render; `shot.png` is a frame of the Outfit Browser recording.
- `shot.py` renders them at 2x into `docs/images` (needs Python with Playwright and Chrome).
